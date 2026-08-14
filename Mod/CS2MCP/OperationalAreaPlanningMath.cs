using System.Collections.Generic;
using Unity.Mathematics;

namespace CS2MCP
{
    /// <summary>
    /// A circular exclusion used while growing an operational area. Buildings
    /// and sampled road points are reduced to this shape so the planner can
    /// stay free of ECS state.
    /// </summary>
    internal readonly struct OperationalAreaObstacle
    {
        public OperationalAreaObstacle(float2 center, float radius)
        {
            Center = center;
            Radius = math.max(0f, radius);
        }

        public float2 Center { get; }
        public float Radius { get; }
    }

    /// <summary>
    /// Pure geometry for operational-area expansion. Callers supply the locked
    /// building edge, a target surface, and obstacle disks; the implementation
    /// grows a near-circle on the free side, clips rays to obstacles, and
    /// degrades to a convex ring when a simple concave perimeter would violate
    /// the native 4–16 node budget. Holes are never produced.
    /// </summary>
    internal static class OperationalAreaPlanningMath
    {
        public const int MinNodeCount = 4;
        public const int MaxNodeCount = 16;
        public const float MinNodeDistance = 4f;
        public const float MaxPlanningRadius = 512f;

        private const float kEpsilon = 0.01f;
        private const float kObstacleMargin = 0.25f;
        private const int kArcSamples = 24;
        private const int kRadiusSearchIterations = 18;

        /// <summary>
        /// Tangent offsets from the existing centroid, replacing the old 110°
        /// fan skews so extractors can still rank a few near-circle variants.
        /// </summary>
        public static float[] CenterShifts(float lockedLength)
        {
            float span = math.max(0f, lockedLength);
            return new[]
            {
                0f,
                -0.25f * span,
                0.25f * span,
                -0.5f * span,
                0.5f * span,
            };
        }

        public static bool TryPlanExpansion(
            IReadOnlyList<float2> existing,
            float2 lockedStart,
            float2 lockedEnd,
            float2 tangent,
            float2 normal,
            float targetArea,
            float tangentShift,
            IReadOnlyList<OperationalAreaObstacle> obstacles,
            out List<float2> polygon,
            out float area,
            out string error)
        {
            polygon = null;
            area = 0f;
            error = null;
            if (existing == null
                || existing.Count < MinNodeCount
                || math.lengthsq(tangent) < kEpsilon * kEpsilon
                || math.lengthsq(normal) < kEpsilon * kEpsilon
                || targetArea <= 0f)
            {
                error = "operational area geometry is not expandable";
                return false;
            }

            float2 lockedMid = (lockedStart + lockedEnd) * 0.5f;
            if (!TryPlanningCenter(
                    existing,
                    lockedMid,
                    tangent,
                    normal,
                    tangentShift,
                    obstacles,
                    out float2 center,
                    out error))
            {
                return false;
            }

            List<float2> best = null;
            float lowRadius = 0f;
            float highRadius = math.max(8f, math.distance(lockedStart, lockedEnd) * 0.5f);
            while (highRadius <= MaxPlanningRadius)
            {
                if (TryBuildExpansionRing(
                        existing,
                        lockedStart,
                        lockedEnd,
                        lockedMid,
                        tangent,
                        normal,
                        center,
                        highRadius,
                        obstacles,
                        out List<float2> candidate))
                {
                    best = candidate;
                    if (PolygonArea(candidate) >= targetArea)
                    {
                        break;
                    }
                }
                highRadius *= 1.5f;
            }
            if (best == null || PolygonArea(best) < targetArea)
            {
                error = "target area cannot be reached within 512 m after subtracting obstacles";
                return false;
            }

            for (int iteration = 0; iteration < kRadiusSearchIterations; iteration++)
            {
                float radius = (lowRadius + highRadius) * 0.5f;
                if (TryBuildExpansionRing(
                        existing,
                        lockedStart,
                        lockedEnd,
                        lockedMid,
                        tangent,
                        normal,
                        center,
                        radius,
                        obstacles,
                        out List<float2> candidate)
                    && PolygonArea(candidate) >= targetArea)
                {
                    highRadius = radius;
                    best = candidate;
                }
                else
                {
                    lowRadius = radius;
                }
            }

            polygon = best;
            area = PolygonArea(best);
            return true;
        }

        public static float PolygonArea(IReadOnlyList<float2> nodes)
        {
            if (nodes == null || nodes.Count < 3)
            {
                return 0f;
            }

            float area = 0f;
            for (int i = 0; i < nodes.Count; i++)
            {
                float2 current = nodes[i];
                float2 next = nodes[(i + 1) % nodes.Count];
                area += current.x * next.y - next.x * current.y;
            }
            return math.abs(area) * 0.5f;
        }

        public static float DistanceToPolygon(float2 point, IReadOnlyList<float2> polygon)
        {
            bool inside = false;
            float distance = float.MaxValue;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                float2 a = polygon[j];
                float2 b = polygon[i];
                float2 edge = b - a;
                float t = math.saturate(
                    math.dot(point - a, edge) / math.max(0.001f, math.lengthsq(edge)));
                distance = math.min(distance, math.distance(point, a + edge * t));
                if ((a.y > point.y) != (b.y > point.y)
                    && point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside ? 0f : distance;
        }

        public static bool HasMinimumSpacing(IReadOnlyList<float2> nodes, float minimumSpacing)
        {
            if (nodes == null || nodes.Count < 2)
            {
                return false;
            }

            float minimumSquared = minimumSpacing * minimumSpacing;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (math.distancesq(nodes[i], nodes[(i + 1) % nodes.Count]) < minimumSquared)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryPlanningCenter(
            IReadOnlyList<float2> existing,
            float2 lockedMid,
            float2 tangent,
            float2 normal,
            float tangentShift,
            IReadOnlyList<OperationalAreaObstacle> obstacles,
            out float2 center,
            out string error)
        {
            center = float2.zero;
            error = null;
            for (int i = 0; i < existing.Count; i++)
            {
                center += existing[i];
            }
            center = center / existing.Count + tangent * tangentShift;
            float depth = math.dot(center - lockedMid, normal);
            if (depth < 8f)
            {
                center += normal * (8f - depth);
            }
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Count; i++)
                {
                    OperationalAreaObstacle obstacle = obstacles[i];
                    if (math.distance(center, obstacle.Center) < obstacle.Radius + kObstacleMargin)
                    {
                        error = "expansion center intersects an obstacle";
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool TryBuildExpansionRing(
            IReadOnlyList<float2> existing,
            float2 lockedStart,
            float2 lockedEnd,
            float2 lockedMid,
            float2 tangent,
            float2 normal,
            float2 center,
            float radius,
            IReadOnlyList<OperationalAreaObstacle> obstacles,
            out List<float2> ring)
        {
            ring = null;
            List<float2> concave = SampleClippedArc(
                existing,
                lockedStart,
                lockedEnd,
                lockedMid,
                tangent,
                normal,
                center,
                radius,
                obstacles);
            if (TryFinalizeRing(
                    concave,
                    existing,
                    lockedStart,
                    lockedEnd,
                    obstacles,
                    out ring))
            {
                return true;
            }

            var hullPoints = new List<float2>(existing.Count + concave.Count);
            hullPoints.AddRange(existing);
            hullPoints.AddRange(concave);
            return TryFinalizeRing(
                ConvexHull(hullPoints),
                existing,
                lockedStart,
                lockedEnd,
                obstacles,
                out ring);
        }

        private static List<float2> SampleClippedArc(
            IReadOnlyList<float2> existing,
            float2 lockedStart,
            float2 lockedEnd,
            float2 lockedMid,
            float2 tangent,
            float2 normal,
            float2 center,
            float radius,
            IReadOnlyList<OperationalAreaObstacle> obstacles)
        {
            float endAngle = DirectionAngle(lockedEnd - center, tangent, normal);
            float startAngle = DirectionAngle(lockedStart - center, tangent, normal);
            float sweep = FreeSideSweep(endAngle, startAngle);
            var points = new List<float2>(kArcSamples + existing.Count)
            {
                lockedStart,
                lockedEnd,
            };

            var angles = new List<float>(kArcSamples + existing.Count);
            for (int i = 1; i < kArcSamples; i++)
            {
                angles.Add(endAngle + sweep * (i / (float)kArcSamples));
            }
            for (int i = 2; i < existing.Count; i++)
            {
                float vertexAngle = DirectionAngle(existing[i] - center, tangent, normal);
                float alongSweep = SweepAmount(endAngle, vertexAngle, sweep);
                if (alongSweep > 0f && alongSweep < 1f)
                {
                    angles.Add(vertexAngle);
                }
            }
            angles.Sort((a, b) => SweepAmount(endAngle, a, sweep)
                .CompareTo(SweepAmount(endAngle, b, sweep)));

            float2 previous = lockedEnd;
            for (int i = 0; i < angles.Count; i++)
            {
                float angle = angles[i];
                float2 direction = normal * math.cos(angle) + tangent * math.sin(angle);
                float length = math.length(direction);
                if (length < kEpsilon)
                {
                    continue;
                }
                direction /= length;
                float rayRadius = ClippedRayLength(
                    center,
                    direction,
                    radius,
                    lockedMid,
                    normal,
                    existing,
                    obstacles);
                if (rayRadius < MinNodeDistance * 0.5f)
                {
                    continue;
                }

                float2 point = center + direction * rayRadius;
                if (math.dot(point - lockedMid, normal) < -kEpsilon)
                {
                    continue;
                }
                if (math.distancesq(point, previous) < kEpsilon * kEpsilon
                    || math.distancesq(point, lockedStart) < kEpsilon * kEpsilon
                    || math.distancesq(point, lockedEnd) < kEpsilon * kEpsilon)
                {
                    continue;
                }
                points.Add(point);
                previous = point;
            }
            return points;
        }

        private static bool TryFinalizeRing(
            List<float2> source,
            IReadOnlyList<float2> existing,
            float2 lockedStart,
            float2 lockedEnd,
            IReadOnlyList<OperationalAreaObstacle> obstacles,
            out List<float2> ring)
        {
            ring = null;
            if (source == null
                || !TryOrientLockedEdge(source, lockedStart, lockedEnd, out List<float2> oriented))
            {
                return false;
            }
            if (!TrySimplifyRing(oriented, out List<float2> simplified))
            {
                return false;
            }
            if (!IsSimplePolygon(simplified)
                || !HasMinimumSpacing(simplified, MinNodeDistance)
                || simplified.Count < MinNodeCount
                || simplified.Count > MaxNodeCount
                || !ContainsPolygon(simplified, existing)
                || IntersectsObstacles(simplified, obstacles))
            {
                return false;
            }
            ring = simplified;
            return true;
        }

        private static float ClippedRayLength(
            float2 origin,
            float2 direction,
            float radius,
            float2 lockedMid,
            float2 normal,
            IReadOnlyList<float2> existing,
            IReadOnlyList<OperationalAreaObstacle> obstacles)
        {
            float maxLength = radius;
            float towardBuilding = math.dot(direction, normal);
            if (towardBuilding < -kEpsilon)
            {
                float lineHit = -math.dot(origin - lockedMid, normal) / towardBuilding;
                if (lineHit > 0f)
                {
                    maxLength = math.min(maxLength, lineHit);
                }
            }
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Count; i++)
                {
                    OperationalAreaObstacle obstacle = obstacles[i];
                    if (TryRayDiskHit(
                            origin,
                            direction,
                            obstacle.Center,
                            ObstacleClipRadius(obstacle),
                            out float hit)
                        && hit > 0f)
                    {
                        maxLength = math.min(maxLength, hit);
                    }
                }
            }
            float existingExtent = RadialExtent(origin, direction, existing);
            return math.max(existingExtent, math.max(0f, maxLength));
        }

        private static float RadialExtent(
            float2 origin,
            float2 direction,
            IReadOnlyList<float2> polygon)
        {
            float farthest = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                float2 a = polygon[i];
                float2 b = polygon[(i + 1) % polygon.Count];
                if (TryRaySegmentHit(origin, direction, a, b, out float t))
                {
                    farthest = math.max(farthest, t);
                }
            }
            return farthest;
        }

        private static bool TryRayDiskHit(
            float2 origin,
            float2 direction,
            float2 center,
            float radius,
            out float t)
        {
            t = 0f;
            float2 offset = origin - center;
            float c = math.lengthsq(offset) - radius * radius;
            float halfB = math.dot(offset, direction);
            float discriminant = halfB * halfB - c;
            if (discriminant < 0f)
            {
                return false;
            }

            float root = math.sqrt(discriminant);
            float first = -halfB - root;
            float second = -halfB + root;
            if (first > kEpsilon)
            {
                t = first;
                return true;
            }
            if (second > kEpsilon)
            {
                t = second;
                return true;
            }
            return false;
        }

        private static bool TryRaySegmentHit(
            float2 origin,
            float2 direction,
            float2 segmentStart,
            float2 segmentEnd,
            out float t)
        {
            t = 0f;
            float2 edge = segmentEnd - segmentStart;
            float denominator = Cross(direction, edge);
            if (math.abs(denominator) < kEpsilon)
            {
                return false;
            }

            float2 delta = segmentStart - origin;
            t = Cross(delta, edge) / denominator;
            float u = Cross(delta, direction) / denominator;
            return t > kEpsilon && u >= -kEpsilon && u <= 1f + kEpsilon;
        }

        private static float DirectionAngle(float2 direction, float2 tangent, float2 normal)
        {
            return math.atan2(math.dot(direction, tangent), math.dot(direction, normal));
        }

        private static float FreeSideSweep(float from, float to)
        {
            float shortDelta = NormalizeAngle(to - from);
            float longDelta = shortDelta >= 0f
                ? shortDelta - 2f * math.PI
                : shortDelta + 2f * math.PI;
            float shortMid = math.abs(NormalizeAngle(from + shortDelta * 0.5f));
            float longMid = math.abs(NormalizeAngle(from + longDelta * 0.5f));
            return shortMid <= longMid ? shortDelta : longDelta;
        }

        private static float SweepAmount(float from, float angle, float sweep)
        {
            if (math.abs(sweep) < kEpsilon)
            {
                return 0f;
            }

            float delta = sweep >= 0f
                ? NormalizeAngle(angle - from)
                : NormalizeAngle(from - angle);
            if (delta < 0f)
            {
                delta += 2f * math.PI;
            }
            return math.saturate(delta / math.abs(sweep));
        }

        private static float NormalizeAngle(float radians)
        {
            return math.atan2(math.sin(radians), math.cos(radians));
        }

        private static bool TrySimplifyRing(List<float2> source, out List<float2> simplified)
        {
            simplified = Deduplicate(source);
            if (simplified.Count < MinNodeCount)
            {
                return false;
            }

            while (simplified.Count > MaxNodeCount)
            {
                int remove = LeastImportantVertex(simplified);
                if (remove < 2)
                {
                    return false;
                }
                simplified.RemoveAt(remove);
            }

            int guard = simplified.Count;
            while (guard-- > 0 && simplified.Count > MinNodeCount)
            {
                int close = FirstClosePair(simplified, MinNodeDistance);
                if (close < 0)
                {
                    break;
                }

                int remove;
                if (close == 0)
                {
                    return false;
                }
                if (close == 1)
                {
                    remove = 2;
                }
                else if (close == simplified.Count - 1)
                {
                    remove = close;
                }
                else
                {
                    remove = close + 1;
                }
                if (remove < 2 || remove >= simplified.Count)
                {
                    return false;
                }
                simplified.RemoveAt(remove);
            }
            return simplified.Count >= MinNodeCount
                && simplified.Count <= MaxNodeCount
                && HasMinimumSpacing(simplified, MinNodeDistance);
        }

        private static List<float2> Deduplicate(List<float2> source)
        {
            var unique = new List<float2>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                float2 point = source[i];
                if (unique.Count == 0
                    || math.distancesq(unique[unique.Count - 1], point) > kEpsilon * kEpsilon)
                {
                    unique.Add(point);
                }
            }
            if (unique.Count > 1
                && math.distancesq(unique[0], unique[unique.Count - 1]) <= kEpsilon * kEpsilon)
            {
                unique.RemoveAt(unique.Count - 1);
            }
            return unique;
        }

        private static int LeastImportantVertex(List<float2> ring)
        {
            int selected = 2;
            float best = float.MaxValue;
            for (int i = 2; i < ring.Count; i++)
            {
                float2 previous = ring[i - 1];
                float2 current = ring[i];
                float2 next = ring[(i + 1) % ring.Count];
                float importance = math.abs(Cross(current - previous, next - current));
                if (importance < best)
                {
                    best = importance;
                    selected = i;
                }
            }
            return selected;
        }

        private static int FirstClosePair(List<float2> ring, float minimumSpacing)
        {
            float minimumSquared = minimumSpacing * minimumSpacing;
            for (int i = 0; i < ring.Count; i++)
            {
                if (math.distancesq(ring[i], ring[(i + 1) % ring.Count]) < minimumSquared)
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool ContainsPolygon(
            IReadOnlyList<float2> outer,
            IReadOnlyList<float2> inner)
        {
            for (int i = 0; i < inner.Count; i++)
            {
                if (DistanceToPolygon(inner[i], outer) > 0.5f)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool IntersectsObstacles(
            IReadOnlyList<float2> polygon,
            IReadOnlyList<OperationalAreaObstacle> obstacles)
        {
            if (obstacles == null)
            {
                return false;
            }
            for (int i = 0; i < obstacles.Count; i++)
            {
                OperationalAreaObstacle obstacle = obstacles[i];
                if (DistanceToPolygon(obstacle.Center, polygon) < obstacle.Radius)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsSimplePolygon(List<float2> ring)
        {
            int count = ring.Count;
            for (int i = 0; i < count; i++)
            {
                int iNext = (i + 1) % count;
                for (int j = i + 1; j < count; j++)
                {
                    int jNext = (j + 1) % count;
                    if (i == j || iNext == j || i == jNext || iNext == jNext)
                    {
                        continue;
                    }
                    if (SegmentsIntersect(ring[i], ring[iNext], ring[j], ring[jNext]))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool SegmentsIntersect(float2 a, float2 b, float2 c, float2 d)
        {
            float ab = Cross(b - a, c - a);
            float ac = Cross(b - a, d - a);
            float cd = Cross(d - c, a - c);
            float ce = Cross(d - c, b - c);
            if ((ab > kEpsilon && ac > kEpsilon)
                || (ab < -kEpsilon && ac < -kEpsilon)
                || (cd > kEpsilon && ce > kEpsilon)
                || (cd < -kEpsilon && ce < -kEpsilon))
            {
                return false;
            }
            return (ab > kEpsilon) != (ac > kEpsilon)
                && (cd > kEpsilon) != (ce > kEpsilon);
        }

        private static bool TryOrientLockedEdge(
            List<float2> hull,
            float2 lockedStart,
            float2 lockedEnd,
            out List<float2> oriented)
        {
            oriented = null;
            if (hull == null)
            {
                return false;
            }

            int start = IndexOf(hull, lockedStart);
            int end = IndexOf(hull, lockedEnd);
            if (start < 0 || end < 0)
            {
                return false;
            }
            if ((start + 1) % hull.Count != end)
            {
                if ((end + 1) % hull.Count != start)
                {
                    return false;
                }
                hull = new List<float2>(hull);
                hull.Reverse();
                start = IndexOf(hull, lockedStart);
            }

            oriented = new List<float2>(hull.Count);
            for (int i = 0; i < hull.Count; i++)
            {
                oriented.Add(hull[(start + i) % hull.Count]);
            }
            return oriented.Count >= 3 && math.distancesq(oriented[1], lockedEnd) < 0.01f;
        }

        private static int IndexOf(List<float2> points, float2 target)
        {
            for (int i = 0; i < points.Count; i++)
            {
                if (math.distancesq(points[i], target) < 0.01f)
                {
                    return i;
                }
            }
            return -1;
        }

        private static List<float2> ConvexHull(List<float2> points)
        {
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            var unique = new List<float2>(points.Count);
            foreach (float2 point in points)
            {
                if (unique.Count == 0 || math.distancesq(unique[unique.Count - 1], point) > 0.01f)
                {
                    unique.Add(point);
                }
            }
            if (unique.Count < 3)
            {
                return null;
            }

            var hull = new List<float2>(unique.Count * 2);
            foreach (float2 point in unique)
            {
                while (hull.Count >= 2
                    && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(point);
            }
            int lowerCount = hull.Count;
            for (int i = unique.Count - 2; i >= 0; i--)
            {
                float2 point = unique[i];
                while (hull.Count > lowerCount
                    && Cross(hull[hull.Count - 1] - hull[hull.Count - 2], point - hull[hull.Count - 1]) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(point);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static float ObstacleClipRadius(OperationalAreaObstacle obstacle)
        {
            return obstacle.Radius + kObstacleMargin + MinNodeDistance;
        }

        private static float Cross(float2 a, float2 b) => a.x * b.y - a.y * b.x;
    }
}
