using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2MCP
{
    [Flags]
    internal enum UtilityNetworkKinds : byte
    {
        None = 0,
        Water = 1,
        Sewage = 2,
        LowVoltage = 4,
    }

    /// <summary>
    /// A stable placement snapshot of one connectable utility lane. It carries
    /// the owning edge solely as the native split anchor; road prefab identity
    /// and simulated flow state are deliberately absent.
    /// </summary>
    internal readonly struct PlacementUtilityPath
    {
        public PlacementUtilityPath(
            UtilityNetworkKinds kinds,
            Entity parentEdge,
            float2 edgeDelta,
            float3[] points)
        {
            Kinds = kinds;
            ParentEdge = parentEdge;
            EdgeDelta = edgeDelta;
            Points = points;
        }

        public UtilityNetworkKinds Kinds { get; }
        public Entity ParentEdge { get; }
        public float2 EdgeDelta { get; }
        public float3[] Points { get; }
    }

    internal readonly struct UtilityConnectionTarget
    {
        public UtilityConnectionTarget(
            float3 position,
            Entity parentEdge,
            float parentSplit)
        {
            Position = position;
            ParentEdge = parentEdge;
            ParentSplit = parentSplit;
        }

        public float3 Position { get; }
        public Entity ParentEdge { get; }
        public float ParentSplit { get; }
    }

    /// <summary>
    /// Pure geometry used by building-placement preflight. Keeping these
    /// calculations free of ECS state makes the placement seam deterministic
    /// and lets them be checked without a running city.
    /// </summary>
    internal static class PlacementSearchMath
    {
        private const float kEpsilon = 0.01f;

        public static bool OrientedBoxesOverlap(
            float2 firstCenter,
            float2 firstHalfExtents,
            float2 firstRight,
            float2 firstForward,
            float2 secondCenter,
            float2 secondHalfExtents,
            float2 secondRight,
            float2 secondForward)
        {
            float2 delta = secondCenter - firstCenter;
            return OverlapsOnAxis(
                    delta,
                    firstHalfExtents,
                    firstRight,
                    firstForward,
                    secondHalfExtents,
                    secondRight,
                    secondForward,
                    firstRight)
                && OverlapsOnAxis(
                    delta,
                    firstHalfExtents,
                    firstRight,
                    firstForward,
                    secondHalfExtents,
                    secondRight,
                    secondForward,
                    firstForward)
                && OverlapsOnAxis(
                    delta,
                    firstHalfExtents,
                    firstRight,
                    firstForward,
                    secondHalfExtents,
                    secondRight,
                    secondForward,
                    secondRight)
                && OverlapsOnAxis(
                    delta,
                    firstHalfExtents,
                    firstRight,
                    firstForward,
                    secondHalfExtents,
                    secondRight,
                    secondForward,
                    secondForward);
        }

        public static bool SegmentIntersectsBox(
            float2 segmentStart,
            float2 segmentEnd,
            float2 boxCenter,
            float2 boxHalfExtents,
            float2 boxRight,
            float2 boxForward)
        {
            float2 startOffset = segmentStart - boxCenter;
            float2 localStart = new float2(
                math.dot(startOffset, boxRight),
                math.dot(startOffset, boxForward));
            float2 endOffset = segmentEnd - boxCenter;
            float2 localEnd = new float2(
                math.dot(endOffset, boxRight),
                math.dot(endOffset, boxForward));
            float2 direction = localEnd - localStart;

            float minimum = 0f;
            float maximum = 1f;
            return ClipAxis(localStart.x, direction.x, boxHalfExtents.x, ref minimum, ref maximum)
                && ClipAxis(localStart.y, direction.y, boxHalfExtents.y, ref minimum, ref maximum);
        }

        public static float2 ClosestPointOnSegment(
            float2 point,
            float2 segmentStart,
            float2 segmentEnd)
        {
            return math.lerp(
                segmentStart,
                segmentEnd,
                ClosestPointAmount(point, segmentStart, segmentEnd));
        }

        public static float ClosestPointAmount(
            float2 point,
            float2 segmentStart,
            float2 segmentEnd)
        {
            float2 direction = segmentEnd - segmentStart;
            float lengthSquared = math.lengthsq(direction);
            if (lengthSquared < kEpsilon * kEpsilon)
            {
                return 0f;
            }
            return math.saturate(math.dot(point - segmentStart, direction) / lengthSquared);
        }

        public static float DistanceSquaredToSegment(
            float2 point,
            float2 segmentStart,
            float2 segmentEnd)
        {
            return math.distancesq(
                point,
                ClosestPointOnSegment(point, segmentStart, segmentEnd));
        }

        public static bool TryFindNearestUtilityPoint(
            IReadOnlyList<PlacementUtilityPath> paths,
            UtilityNetworkKinds required,
            float3 from,
            float maxDistance,
            out UtilityConnectionTarget nearest)
        {
            nearest = default;
            if (paths == null
                || required == UtilityNetworkKinds.None
                || maxDistance < 0f)
            {
                return false;
            }

            float bestDistanceSquared = maxDistance * maxDistance;
            bool found = false;
            foreach (PlacementUtilityPath path in paths)
            {
                if ((path.Kinds & required) != required
                    || path.Points == null
                    || path.Points.Length < 2)
                {
                    continue;
                }

                for (int i = 1; i < path.Points.Length; i++)
                {
                    float amount = ClosestPointAmount(
                        from.xz,
                        path.Points[i - 1].xz,
                        path.Points[i].xz);
                    float3 candidate = math.lerp(
                        path.Points[i - 1],
                        path.Points[i],
                        amount);
                    float distanceSquared = math.distancesq(from.xz, candidate.xz);
                    if (distanceSquared <= bestDistanceSquared
                        && (!found || distanceSquared < bestDistanceSquared))
                    {
                        float childAmount = (i - 1 + amount) / (path.Points.Length - 1f);
                        bestDistanceSquared = distanceSquared;
                        nearest = new UtilityConnectionTarget(
                            candidate,
                            path.ParentEdge,
                            math.lerp(path.EdgeDelta.x, path.EdgeDelta.y, childAmount));
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <summary>
        /// Mirrors NetToolSystem.GetCoursePos: an endpoint at either end of an
        /// edge attaches to that edge's node; an interior endpoint splits the
        /// edge at the mapped parameter.
        /// </summary>
        public static void ResolveConnectionAnchor(
            Entity edge,
            Entity startNode,
            Entity endNode,
            float split,
            out Entity anchor,
            out float anchorSplit)
        {
            anchorSplit = math.saturate(split);
            anchor = anchorSplit <= 0f
                ? startNode
                : anchorSplit >= 1f
                    ? endNode
                    : edge;
        }

        public static bool SegmentIntersectsCircle(
            float2 segmentStart,
            float2 segmentEnd,
            float2 center,
            float radius)
        {
            return DistanceSquaredToSegment(center, segmentStart, segmentEnd)
                <= radius * radius;
        }

        public static bool SegmentIntersectsExpandedBox(
            float2 segmentStart,
            float2 segmentEnd,
            float2 boxCenter,
            float2 boxHalfExtents,
            float2 boxRight,
            float2 boxForward,
            float expansion)
        {
            if (SegmentIntersectsBox(
                    segmentStart,
                    segmentEnd,
                    boxCenter,
                    boxHalfExtents + new float2(expansion, 0f),
                    boxRight,
                    boxForward)
                || SegmentIntersectsBox(
                    segmentStart,
                    segmentEnd,
                    boxCenter,
                    boxHalfExtents + new float2(0f, expansion),
                    boxRight,
                    boxForward))
            {
                return true;
            }

            float2 right = boxRight * boxHalfExtents.x;
            float2 forward = boxForward * boxHalfExtents.y;
            return SegmentIntersectsCircle(
                    segmentStart,
                    segmentEnd,
                    boxCenter + right + forward,
                    expansion)
                || SegmentIntersectsCircle(
                    segmentStart,
                    segmentEnd,
                    boxCenter + right - forward,
                    expansion)
                || SegmentIntersectsCircle(
                    segmentStart,
                    segmentEnd,
                    boxCenter - right + forward,
                    expansion)
                || SegmentIntersectsCircle(
                    segmentStart,
                    segmentEnd,
                    boxCenter - right - forward,
                    expansion);
        }

        private static bool OverlapsOnAxis(
            float2 delta,
            float2 firstHalfExtents,
            float2 firstRight,
            float2 firstForward,
            float2 secondHalfExtents,
            float2 secondRight,
            float2 secondForward,
            float2 axis)
        {
            float firstRadius =
                math.abs(math.dot(axis, firstRight)) * firstHalfExtents.x
                + math.abs(math.dot(axis, firstForward)) * firstHalfExtents.y;
            float secondRadius =
                math.abs(math.dot(axis, secondRight)) * secondHalfExtents.x
                + math.abs(math.dot(axis, secondForward)) * secondHalfExtents.y;
            return math.abs(math.dot(delta, axis)) + kEpsilon < firstRadius + secondRadius;
        }

        private static bool ClipAxis(
            float start,
            float direction,
            float halfExtent,
            ref float minimum,
            ref float maximum)
        {
            if (math.abs(direction) < kEpsilon)
            {
                return math.abs(start) <= halfExtent;
            }

            float first = (-halfExtent - start) / direction;
            float second = (halfExtent - start) / direction;
            if (first > second)
            {
                float swap = first;
                first = second;
                second = swap;
            }
            minimum = math.max(minimum, first);
            maximum = math.min(maximum, second);
            return minimum <= maximum;
        }
    }
}
