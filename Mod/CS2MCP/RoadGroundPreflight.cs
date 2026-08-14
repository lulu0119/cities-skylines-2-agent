using System;
using Unity.Mathematics;

namespace CS2MCP
{
    internal enum RoadBuildMode : byte
    {
        Ground,
        GradeSeparated,
    }

    internal enum RoadGroundBlock : byte
    {
        None,
        Water,
        SteepSlope,
        InvalidPath,
    }

    /// <summary>
    /// The final cubic course after the caller has adjusted it to terrain.
    /// Ground preflight inspects this exact path rather than reconstructing a
    /// second approximation from the model-facing coordinates.
    /// </summary>
    internal readonly struct RoadPath
    {
        public RoadPath(float3 a, float3 b, float3 c, float3 d)
        {
            A = a;
            B = b;
            C = c;
            D = d;
        }

        public float3 A { get; }
        public float3 B { get; }
        public float3 C { get; }
        public float3 D { get; }

        public static RoadPath Straight(float3 start, float3 end)
        {
            float3 delta = end - start;
            return new RoadPath(
                start,
                start + delta / 3f,
                start + delta * (2f / 3f),
                end);
        }

        public static RoadPath WithControlPoint(
            float3 start,
            float3 controlPoint,
            float3 end)
        {
            return new RoadPath(
                start,
                start + (controlPoint - start) * (2f / 3f),
                end + (controlPoint - end) * (2f / 3f),
                end);
        }
    }

    internal readonly struct RoadSurfaceSample
    {
        public RoadSurfaceSample(float waterHeight, float waterDepth)
        {
            WaterHeight = waterHeight;
            WaterDepth = waterDepth;
        }

        public float WaterHeight { get; }
        public float WaterDepth { get; }
    }

    /// <summary>
    /// Adapts the game's terrain/water snapshot at the ECS seam. The supplied
    /// position includes the adjusted road height so the preflight can decide
    /// whether water actually reaches the road surface.
    /// </summary>
    internal interface IRoadSurfaceSampler
    {
        RoadSurfaceSample Sample(float3 roadPosition);
    }

    internal readonly struct RoadGroundPreflightResult
    {
        public RoadGroundPreflightResult(
            RoadGroundBlock block,
            float3 position = default,
            float waterHeight = 0f,
            float waterDepth = 0f,
            float grade = 0f,
            float maximumGrade = 0f)
        {
            Block = block;
            Position = position;
            WaterHeight = waterHeight;
            WaterDepth = waterDepth;
            Grade = grade;
            MaximumGrade = maximumGrade;
        }

        public bool Allowed => Block == RoadGroundBlock.None;
        public RoadGroundBlock Block { get; }
        public float3 Position { get; }
        public float WaterHeight { get; }
        public float WaterDepth { get; }
        public float Grade { get; }
        public float MaximumGrade { get; }
    }

    /// <summary>
    /// Applies the extra invariants promised by mode=ground to an adjusted road
    /// course. Native tool validation remains authoritative for every mode.
    /// </summary>
    internal static class RoadGroundPreflight
    {
        private const float kMaximumSampleSpacing = 4f;
        private const float kMinimumWaterDepth = 0.2f;
        private const float kDistanceEpsilon = 0.001f;
        private const float kMaximumGroundGrade = 0.10f;
        private const float kGradeTolerance = 0.000001f;
        private const int kMaximumLongitudinalSegments = 4096;

        public static RoadGroundPreflightResult Evaluate(
            RoadPath path,
            float roadHalfWidth,
            float prefabMaximumGrade,
            float minimumSurfaceOffset,
            IRoadSurfaceSampler surface)
        {
            float maximumGrade = prefabMaximumGrade > 0f
                ? math.min(kMaximumGroundGrade, prefabMaximumGrade)
                : kMaximumGroundGrade;
            float maximumControlSpan = math.max(
                math.distance(path.A.xz, path.B.xz),
                math.max(
                    math.distance(path.B.xz, path.C.xz),
                    math.distance(path.C.xz, path.D.xz)));
            // A cubic Bezier's horizontal derivative is three times a convex
            // combination of its control-edge vectors. This count therefore
            // bounds every longitudinal interval to at most about 4m in x/z,
            // even when equal t intervals cover unequal distances.
            double requiredSegments = Math.Ceiling(
                3d * maximumControlSpan / kMaximumSampleSpacing);
            if (double.IsNaN(requiredSegments)
                || double.IsInfinity(requiredSegments)
                || requiredSegments > kMaximumLongitudinalSegments)
            {
                return new RoadGroundPreflightResult(
                    RoadGroundBlock.InvalidPath,
                    Point(path, 0.5f),
                    maximumGrade: maximumGrade);
            }
            int segmentCount = math.max(1, (int)requiredSegments);
            float width = math.max(0f, roadHalfWidth);
            float3 previousCenter = default;

            for (int i = 0; i <= segmentCount; i++)
            {
                float t = i / (float)segmentCount;
                float3 center = Point(path, t);
                float2 tangent = HorizontalTangent(path, t, segmentCount);
                float2 normal = math.normalizesafe(
                    new float2(-tangent.y, tangent.x),
                    new float2(1f, 0f));

                int crossStepsPerSide = width > kDistanceEpsilon
                    ? math.max(
                        1,
                        (int)math.ceil(width / kMaximumSampleSpacing))
                    : 0;
                for (int crossIndex = -crossStepsPerSide;
                    crossIndex <= crossStepsPerSide;
                    crossIndex++)
                {
                    float offset = crossStepsPerSide == 0
                        ? 0f
                        : width * crossIndex / crossStepsPerSide;
                    float3 surfacePosition = center;
                    surfacePosition.xz += normal * offset;
                    RoadGroundPreflightResult waterBlock = CheckWater(
                        surfacePosition,
                        minimumSurfaceOffset,
                        surface);
                    if (!waterBlock.Allowed)
                    {
                        return waterBlock;
                    }
                }

                if (i > 0)
                {
                    float horizontalDistance = math.distance(
                        previousCenter.xz,
                        center.xz);
                    float verticalDistance = math.abs(center.y - previousCenter.y);
                    float grade = horizontalDistance > kDistanceEpsilon
                        ? verticalDistance / horizontalDistance
                        : verticalDistance > kDistanceEpsilon
                            ? float.PositiveInfinity
                            : 0f;
                    if (grade > maximumGrade + kGradeTolerance)
                    {
                        return new RoadGroundPreflightResult(
                            RoadGroundBlock.SteepSlope,
                            math.lerp(previousCenter, center, 0.5f),
                            grade: grade,
                            maximumGrade: maximumGrade);
                    }
                }

                previousCenter = center;
            }

            return new RoadGroundPreflightResult(
                RoadGroundBlock.None,
                maximumGrade: maximumGrade);
        }

        private static RoadGroundPreflightResult CheckWater(
            float3 roadPosition,
            float minimumSurfaceOffset,
            IRoadSurfaceSampler surface)
        {
            RoadSurfaceSample sample = surface.Sample(roadPosition);
            if (sample.WaterDepth >= kMinimumWaterDepth
                && sample.WaterHeight > roadPosition.y + minimumSurfaceOffset)
            {
                return new RoadGroundPreflightResult(
                    RoadGroundBlock.Water,
                    roadPosition,
                    sample.WaterHeight,
                    sample.WaterDepth);
            }

            return new RoadGroundPreflightResult(RoadGroundBlock.None);
        }

        private static float3 Point(RoadPath path, float t)
        {
            float inverse = 1f - t;
            float inverseSquared = inverse * inverse;
            float tSquared = t * t;
            return path.A * (inverseSquared * inverse)
                + path.B * (3f * inverseSquared * t)
                + path.C * (3f * inverse * tSquared)
                + path.D * (tSquared * t);
        }

        private static float2 HorizontalTangent(
            RoadPath path,
            float t,
            int segmentCount)
        {
            float inverse = 1f - t;
            float2 tangent = 3f * inverse * inverse * (path.B.xz - path.A.xz)
                + 6f * inverse * t * (path.C.xz - path.B.xz)
                + 3f * t * t * (path.D.xz - path.C.xz);
            if (math.lengthsq(tangent) > kDistanceEpsilon * kDistanceEpsilon)
            {
                return tangent;
            }

            float delta = 1f / segmentCount;
            float lower = math.max(0f, t - delta);
            float upper = math.min(1f, t + delta);
            tangent = Point(path, upper).xz - Point(path, lower).xz;
            if (math.lengthsq(tangent) > kDistanceEpsilon * kDistanceEpsilon)
            {
                return tangent;
            }

            tangent = path.D.xz - path.A.xz;
            return math.lengthsq(tangent) > kDistanceEpsilon * kDistanceEpsilon
                ? tangent
                : new float2(0f, 1f);
        }
    }
}
