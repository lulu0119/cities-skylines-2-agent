using System;
using System.Collections.Generic;
using System.Globalization;

namespace CS2MCP
{
    /// <summary>
    /// The validated model-facing options for one build_road call. This module
    /// owns the distinctions between omitted, malformed and incompatible
    /// values so the ECS handler only has to construct the requested course.
    /// </summary>
    internal readonly struct NetworkBuildArguments
    {
        private NetworkBuildArguments(
            RoadBuildMode? roadMode,
            bool hasControlPoint,
            float controlX,
            float controlZ,
            bool hasElevation,
            float startElevation,
            float endElevation)
        {
            RoadMode = roadMode;
            HasControlPoint = hasControlPoint;
            ControlX = controlX;
            ControlZ = controlZ;
            HasElevation = hasElevation;
            StartElevation = startElevation;
            EndElevation = endElevation;
        }

        public RoadBuildMode? RoadMode { get; }
        public bool HasControlPoint { get; }
        public float ControlX { get; }
        public float ControlZ { get; }
        public bool HasElevation { get; }
        public float StartElevation { get; }
        public float EndElevation { get; }

        public static bool TryParse(
            IReadOnlyDictionary<string, string> query,
            bool isRoad,
            out NetworkBuildArguments arguments,
            out string error)
        {
            arguments = default;
            error = null;

            if (!TryParseMode(query, isRoad, out RoadBuildMode? roadMode, out error)
                || !TryParseControlPoint(
                    query,
                    out bool hasControlPoint,
                    out float controlX,
                    out float controlZ,
                    out error)
                || !TryParseElevations(
                    query,
                    isRoad,
                    roadMode,
                    out bool hasElevation,
                    out float startElevation,
                    out float endElevation,
                    out error))
            {
                return false;
            }

            arguments = new NetworkBuildArguments(
                roadMode,
                hasControlPoint,
                controlX,
                controlZ,
                hasElevation,
                startElevation,
                endElevation);
            return true;
        }

        private static bool TryParseMode(
            IReadOnlyDictionary<string, string> query,
            bool isRoad,
            out RoadBuildMode? mode,
            out string error)
        {
            bool provided = query.TryGetValue("mode", out string rawMode);
            mode = null;
            error = null;

            if (!isRoad)
            {
                if (provided)
                {
                    error = "mode is only valid for road prefabs; omit it for pipes, cables, power lines and other utility networks";
                    return false;
                }
                return true;
            }

            if (!provided)
            {
                mode = RoadBuildMode.Ground;
                return true;
            }

            string normalized = rawMode == null ? string.Empty : rawMode.Trim();
            if (string.Equals(normalized, "ground", StringComparison.OrdinalIgnoreCase))
            {
                mode = RoadBuildMode.Ground;
                return true;
            }
            if (string.Equals(normalized, "grade-separated", StringComparison.OrdinalIgnoreCase))
            {
                mode = RoadBuildMode.GradeSeparated;
                return true;
            }

            error = "mode must be 'ground' or 'grade-separated' for road prefabs";
            return false;
        }

        private static bool TryParseControlPoint(
            IReadOnlyDictionary<string, string> query,
            out bool hasControlPoint,
            out float controlX,
            out float controlZ,
            out string error)
        {
            bool hasX = query.TryGetValue("cx", out string rawX);
            bool hasZ = query.TryGetValue("cz", out string rawZ);
            hasControlPoint = false;
            controlX = 0f;
            controlZ = 0f;
            error = null;

            if (hasX != hasZ)
            {
                error = "provide both cx and cz for a curved segment, or omit both for a straight segment";
                return false;
            }
            if (!hasX)
            {
                return true;
            }
            if (!TryParseFinite(rawX, out controlX)
                || !TryParseFinite(rawZ, out controlZ))
            {
                error = "cx and cz must both be finite world coordinates";
                return false;
            }

            hasControlPoint = true;
            return true;
        }

        private static bool TryParseElevations(
            IReadOnlyDictionary<string, string> query,
            bool isRoad,
            RoadBuildMode? roadMode,
            out bool hasElevation,
            out float startElevation,
            out float endElevation,
            out string error)
        {
            bool hasStart = query.TryGetValue("e1", out string rawStart);
            bool hasEnd = query.TryGetValue("e2", out string rawEnd);
            hasElevation = hasStart || hasEnd;
            startElevation = 0f;
            endElevation = 0f;
            error = null;

            if (hasStart && !TryParseFinite(rawStart, out startElevation))
            {
                error = "e1 must be a finite elevation in meters";
                return false;
            }
            if (hasEnd && !TryParseFinite(rawEnd, out endElevation))
            {
                error = "e2 must be a finite elevation in meters";
                return false;
            }
            if (hasStart && (startElevation < -30f || startElevation > 60f))
            {
                error = $"e1={startElevation:F0} out of range; e1/e2 are elevation in meters relative to terrain (-30..60), not entity indexes.";
                return false;
            }
            if (hasEnd && (endElevation < -30f || endElevation > 60f))
            {
                error = $"e2={endElevation:F0} out of range; e1/e2 are elevation in meters relative to terrain (-30..60), not entity indexes.";
                return false;
            }

            if (!isRoad)
            {
                return true;
            }
            if (roadMode == RoadBuildMode.Ground && hasElevation)
            {
                error = "mode=ground does not accept e1/e2; omit elevation for an ordinary road, or explicitly use mode=grade-separated with both e1/e2";
                return false;
            }
            if (roadMode != RoadBuildMode.GradeSeparated)
            {
                return true;
            }
            if (!hasStart || !hasEnd)
            {
                error = "mode=grade-separated requires both e1 and e2 elevation values";
                return false;
            }
            if (startElevation == 0f && endElevation == 0f)
            {
                error = "mode=grade-separated requires a nonzero elevation at one or both endpoints; positive is elevated/bridge and negative is underground";
                return false;
            }

            return true;
        }

        private static bool TryParseFinite(string raw, out float value)
        {
            return float.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value)
                && !float.IsNaN(value)
                && !float.IsInfinity(value);
        }
    }
}
