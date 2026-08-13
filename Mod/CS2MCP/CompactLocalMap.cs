using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CS2MCP
{
    /// <summary>
    /// Immutable inputs for the compact local-map serializer. The game-facing
    /// handler owns snapshot collection; this module owns all derived spatial
    /// semantics and the model-facing text budget.
    /// </summary>
    internal sealed class LocalMapSnapshot
    {
        public string Revision;
        public float MinX;
        public float MinZ;
        public float OriginX;
        public float OriginZ;
        public float FocusX;
        public float FocusZ;
        public float Quantum;
        public int Columns;
        public int Rows;
        public float[] Heights;
        public bool[] Water;
        public bool[] Owned;
        public readonly List<LocalMapRoad> Roads = new List<LocalMapRoad>();
    }

    internal sealed class LocalMapRoad
    {
        public int EntityIndex;
        public int EntityVersion;
        public int StartNodeIndex;
        public int StartNodeVersion;
        public int EndNodeIndex;
        public int EndNodeVersion;
        public int StartDegree;
        public int EndDegree;
        public string Prefab;
        public readonly List<LocalMapPoint> Points = new List<LocalMapPoint>();
    }

    internal struct LocalMapPoint
    {
        public float X;
        public float Z;

        public LocalMapPoint(float x, float z)
        {
            X = x;
            Z = z;
        }
    }

    /// <summary>
    /// Turns one high-resolution local snapshot into deterministic, budgeted
    /// semantic-vector text. Callers do not need to know connected-component,
    /// polygonization, quantization, topology, or output-loading policy.
    /// </summary>
    internal static class CompactLocalMap
    {
        private const float kGentleSlopePercent = 5f;
        private const float kSteepSlopePercent = 12f;
        private const int kMinimumBudgetCharacters = 4096;
        private const int kOmittedLineReserve = 512;
        private const int kFeatureGeometryCharacterCap = 2800;

        private sealed class Region
        {
            public string Kind;
            public char Prefix;
            public int Sequence;
            public int Label;
            public int MinX = int.MaxValue;
            public int MinZ = int.MaxValue;
            public int MaxX = int.MinValue;
            public int MaxZ = int.MinValue;
            public bool ContainsFocus;
            public readonly List<int> Cells = new List<int>();
            public readonly List<List<GridPoint>> Rings = new List<List<GridPoint>>();
            public int VertexCount;
        }

        private struct GridPoint : IEquatable<GridPoint>
        {
            public int X;
            public int Z;

            public GridPoint(int x, int z)
            {
                X = x;
                Z = z;
            }

            public bool Equals(GridPoint other) => X == other.X && Z == other.Z;
            public override bool Equals(object obj) => obj is GridPoint other && Equals(other);
            public override int GetHashCode() => (X * 397) ^ Z;
        }

        private sealed class BoundaryEdge
        {
            public GridPoint Start;
            public GridPoint End;
            public int Direction;
            public bool Used;
        }

        private sealed class SectorStats
        {
            public int Total;
            public int Water;
            public int Buildable;
        }

        public static string Serialize(LocalMapSnapshot snapshot, int characterBudget)
        {
            Validate(snapshot);
            characterBudget = Math.Max(characterBudget, kMinimumBudgetCharacters);

            int count = snapshot.Columns * snapshot.Rows;
            float[] slopes = CalculateSlopes(snapshot);
            bool[] steep = new bool[count];
            bool[] buildable = new bool[count];
            int waterCount = 0;
            int ownedCount = 0;
            int steepCount = 0;
            int buildableCount = 0;
            int gentleCount = 0;
            int moderateCount = 0;
            for (int i = 0; i < count; i++)
            {
                steep[i] = slopes[i] > kSteepSlopePercent;
                buildable[i] = snapshot.Owned[i] && !snapshot.Water[i] && !steep[i];
                if (snapshot.Water[i]) waterCount++;
                if (snapshot.Owned[i]) ownedCount++;
                if (steep[i]) steepCount++;
                if (buildable[i]) buildableCount++;
                if (slopes[i] <= kGentleSlopePercent) gentleCount++;
                else if (slopes[i] <= kSteepSlopePercent) moderateCount++;
            }

            int focusColumn = Clamp(
                (int)Math.Floor((snapshot.FocusX - snapshot.MinX) / snapshot.Quantum),
                0,
                snapshot.Columns - 1);
            int focusRow = Clamp(
                (int)Math.Floor((snapshot.FocusZ - snapshot.MinZ) / snapshot.Quantum),
                0,
                snapshot.Rows - 1);
            int focusIndex = focusRow * snapshot.Columns + focusColumn;
            int minLocalX = LocalX(snapshot, snapshot.MinX);
            int minLocalZ = LocalZ(snapshot, snapshot.MinZ);
            int focusLocalX = LocalX(snapshot, snapshot.FocusX);
            int focusLocalZ = LocalZ(snapshot, snapshot.FocusZ);

            var regions = new List<Region>();
            regions.AddRange(ExtractRegions("water", 'W', snapshot.Water, snapshot, focusIndex));
            regions.AddRange(ExtractRegions("steep", 'S', steep, snapshot, focusIndex));
            regions.AddRange(ExtractRegions("buildable", 'B', buildable, snapshot, focusIndex));
            regions.AddRange(ExtractRegions("owned", 'O', snapshot.Owned, snapshot, focusIndex));
            regions.Sort(CompareRegions);

            float[] sortedHeights = (float[])snapshot.Heights.Clone();
            Array.Sort(sortedHeights);
            float heightMin = sortedHeights[0];
            float heightMedian = sortedHeights[sortedHeights.Length / 2];
            float heightMax = sortedHeights[sortedHeights.Length - 1];

            Dictionary<string, SectorStats> sectors = CalculateSectors(snapshot, buildable);
            var output = new StringBuilder(Math.Min(characterBudget, 16384));
            output.Append("LOCAL_MAP v1 revision=").Append(SafeToken(snapshot.Revision)).Append('\n');
            output.Append("frame origin_world=(").Append(F(snapshot.OriginX, "0.0"))
                .Append(',').Append(F(snapshot.OriginZ, "0.0"))
                .Append(") axes=(+x,+z) unit=m quantum_m=").Append(F(snapshot.Quantum, "0.0"))
                .Append(" bounds_local=[").Append(minLocalX).Append(',').Append(minLocalZ).Append(',')
                .Append(minLocalX + snapshot.Columns).Append(',').Append(minLocalZ + snapshot.Rows)
                .Append("] focus_local=(").Append(focusLocalX).Append(',').Append(focusLocalZ).Append(")\n");
            output.Append("budget unit=characters limit=").Append(characterBudget).Append('\n');
            output.Append("summary elevation_m={min:").Append(F(heightMin, "0.0"))
                .Append(",p50:").Append(F(heightMedian, "0.0"))
                .Append(",max:").Append(F(heightMax, "0.0"))
                .Append("} water=").Append(Percent(waterCount, count))
                .Append(" owned=").Append(Percent(ownedCount, count))
                .Append(" candidate_buildable=").Append(Percent(buildableCount, count))
                .Append(" candidate_rule=owned&&!water&&slope<=12%")
                .Append(" source_resolution_m=").Append(F(snapshot.Quantum, "0.0"))
                .Append(" simplify_tolerance_m=0\n");
            output.Append("slope bands={0..5%:").Append(Percent(gentleCount, count))
                .Append(",5..12%:").Append(Percent(moderateCount, count))
                .Append(",>12%:").Append(Percent(steepCount, count)).Append("}\n");
            output.Append("sectors ")
                .Append("+x=").Append(FormatSector(sectors["+x"]))
                .Append(" -x=").Append(FormatSector(sectors["-x"]))
                .Append(" +z=").Append(FormatSector(sectors["+z"]))
                .Append(" -z=").Append(FormatSector(sectors["-z"]))
                .Append('\n');
            output.Append("regions\n");

            int omittedRegions = 0;
            int omittedRegionVertices = 0;
            var omittedByKind = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Region region in regions)
            {
                string line = FormatRegion(region, snapshot);
                if (!TryAppend(output, line, characterBudget - kOmittedLineReserve))
                {
                    omittedRegions++;
                    omittedRegionVertices += region.VertexCount;
                    Increment(omittedByKind, region.Kind);
                }
            }

            output.Append("networks\n");
            List<LocalMapRoad> roads = new List<LocalMapRoad>(snapshot.Roads);
            roads.Sort((left, right) => CompareRoads(left, right, snapshot));
            var selectedRoadLines = new List<string>();
            var selectedNodeLines = new Dictionary<string, string>(StringComparer.Ordinal);
            int selectedNetworkCharacters = 0;
            int omittedRoads = 0;
            int omittedRoadVertices = 0;
            foreach (LocalMapRoad road in roads)
            {
                List<GridPoint> points = QuantizeRoad(road, snapshot);
                if (points.Count < 2)
                {
                    continue;
                }
                string startId = NodeId(road.StartNodeIndex, road.StartNodeVersion);
                string endId = NodeId(road.EndNodeIndex, road.EndNodeVersion);
                string startLine = FormatNode(startId, points[0], road.StartDegree);
                string endLine = FormatNode(endId, points[points.Count - 1], road.EndDegree);
                string roadLine = FormatRoad(road, startId, endId, points);
                int added = roadLine.Length;
                if (!selectedNodeLines.ContainsKey(startId)) added += startLine.Length;
                if (!selectedNodeLines.ContainsKey(endId)) added += endLine.Length;
                if (output.Length + selectedNetworkCharacters + added > characterBudget - kOmittedLineReserve)
                {
                    omittedRoads++;
                    omittedRoadVertices += points.Count;
                    continue;
                }
                if (!selectedNodeLines.ContainsKey(startId)) selectedNodeLines.Add(startId, startLine);
                if (!selectedNodeLines.ContainsKey(endId)) selectedNodeLines.Add(endId, endLine);
                selectedRoadLines.Add(roadLine);
                selectedNetworkCharacters += added;
            }
            var nodeIds = new List<string>(selectedNodeLines.Keys);
            nodeIds.Sort(StringComparer.Ordinal);
            foreach (string nodeId in nodeIds) output.Append(selectedNodeLines[nodeId]);
            foreach (string roadLine in selectedRoadLines) output.Append(roadLine);

            output.Append("relations focus={water:").Append(Bool(snapshot.Water[focusIndex]))
                .Append(",steep:").Append(Bool(steep[focusIndex]))
                .Append(",owned:").Append(Bool(snapshot.Owned[focusIndex]))
                .Append(",candidate_buildable:").Append(Bool(buildable[focusIndex]))
                .Append("}; ground_writes=must_validate_full_corridor\n");

            int omittedFeatures = omittedRegions + omittedRoads;
            output.Append("omitted features=").Append(omittedFeatures)
                .Append(" regions=").Append(omittedRegions)
                .Append(" roads=").Append(omittedRoads)
                .Append(" vertices=").Append(omittedRegionVertices + omittedRoadVertices)
                .Append(" by_kind=").Append(FormatCounts(omittedByKind))
                .Append(" detail_patches=0 reason=")
                .Append(omittedFeatures > 0 ? "output_budget" : "none")
                .Append('\n');

            // The fixed reserve above should make this unreachable. Keep the
            // interface honest if a future field grows unexpectedly.
            if (output.Length > characterBudget)
            {
                throw new InvalidOperationException("compact local-map serializer exceeded its declared character budget");
            }

            return output.ToString();
        }

        private static void Validate(LocalMapSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.Columns <= 0 || snapshot.Rows <= 0 || snapshot.Quantum <= 0f)
            {
                throw new ArgumentException("local-map dimensions and quantum must be positive", nameof(snapshot));
            }
            int count = snapshot.Columns * snapshot.Rows;
            if (snapshot.Heights == null || snapshot.Heights.Length != count
                || snapshot.Water == null || snapshot.Water.Length != count
                || snapshot.Owned == null || snapshot.Owned.Length != count)
            {
                throw new ArgumentException("local-map raster layers must match columns * rows", nameof(snapshot));
            }
        }

        private static float[] CalculateSlopes(LocalMapSnapshot snapshot)
        {
            var result = new float[snapshot.Heights.Length];
            for (int row = 0; row < snapshot.Rows; row++)
            {
                for (int col = 0; col < snapshot.Columns; col++)
                {
                    int left = row * snapshot.Columns + Math.Max(0, col - 1);
                    int right = row * snapshot.Columns + Math.Min(snapshot.Columns - 1, col + 1);
                    int down = Math.Max(0, row - 1) * snapshot.Columns + col;
                    int up = Math.Min(snapshot.Rows - 1, row + 1) * snapshot.Columns + col;
                    float dxDistance = Math.Max(1, Math.Min(snapshot.Columns - 1, col + 1) - Math.Max(0, col - 1)) * snapshot.Quantum;
                    float dzDistance = Math.Max(1, Math.Min(snapshot.Rows - 1, row + 1) - Math.Max(0, row - 1)) * snapshot.Quantum;
                    float dx = (snapshot.Heights[right] - snapshot.Heights[left]) / dxDistance;
                    float dz = (snapshot.Heights[up] - snapshot.Heights[down]) / dzDistance;
                    result[row * snapshot.Columns + col] = (float)Math.Sqrt(dx * dx + dz * dz) * 100f;
                }
            }
            return result;
        }

        private static List<Region> ExtractRegions(
            string kind,
            char prefix,
            bool[] mask,
            LocalMapSnapshot snapshot,
            int focusIndex)
        {
            int count = snapshot.Columns * snapshot.Rows;
            var labels = new int[count];
            for (int i = 0; i < labels.Length; i++) labels[i] = -1;
            var regions = new List<Region>();
            var queue = new Queue<int>();
            for (int start = 0; start < count; start++)
            {
                if (!mask[start] || labels[start] >= 0) continue;
                int label = regions.Count;
                var region = new Region { Kind = kind, Prefix = prefix, Label = label };
                labels[start] = label;
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    int row = index / snapshot.Columns;
                    int col = index - row * snapshot.Columns;
                    region.Cells.Add(index);
                    region.MinX = Math.Min(region.MinX, col);
                    region.MaxX = Math.Max(region.MaxX, col + 1);
                    region.MinZ = Math.Min(region.MinZ, row);
                    region.MaxZ = Math.Max(region.MaxZ, row + 1);
                    if (index == focusIndex) region.ContainsFocus = true;
                    EnqueueNeighbour(col - 1, row, label, mask, labels, snapshot, queue);
                    EnqueueNeighbour(col + 1, row, label, mask, labels, snapshot, queue);
                    EnqueueNeighbour(col, row - 1, label, mask, labels, snapshot, queue);
                    EnqueueNeighbour(col, row + 1, label, mask, labels, snapshot, queue);
                }
                TraceRings(region, labels, snapshot);
                regions.Add(region);
            }
            regions.Sort((left, right) =>
            {
                int compare = right.ContainsFocus.CompareTo(left.ContainsFocus);
                if (compare != 0) return compare;
                compare = right.Cells.Count.CompareTo(left.Cells.Count);
                if (compare != 0) return compare;
                compare = left.MinZ.CompareTo(right.MinZ);
                return compare != 0 ? compare : left.MinX.CompareTo(right.MinX);
            });
            for (int i = 0; i < regions.Count; i++) regions[i].Sequence = i + 1;
            return regions;
        }

        private static void EnqueueNeighbour(
            int col,
            int row,
            int label,
            bool[] mask,
            int[] labels,
            LocalMapSnapshot snapshot,
            Queue<int> queue)
        {
            if (col < 0 || row < 0 || col >= snapshot.Columns || row >= snapshot.Rows) return;
            int index = row * snapshot.Columns + col;
            if (!mask[index] || labels[index] >= 0) return;
            labels[index] = label;
            queue.Enqueue(index);
        }

        private static void TraceRings(Region region, int[] labels, LocalMapSnapshot snapshot)
        {
            var edges = new List<BoundaryEdge>();
            var outgoing = new Dictionary<GridPoint, List<BoundaryEdge>>();
            foreach (int index in region.Cells)
            {
                int row = index / snapshot.Columns;
                int col = index - row * snapshot.Columns;
                if (!SameLabel(col, row - 1, region.Label, labels, snapshot))
                    AddBoundary(edges, outgoing, new GridPoint(col, row), new GridPoint(col + 1, row), 0);
                if (!SameLabel(col + 1, row, region.Label, labels, snapshot))
                    AddBoundary(edges, outgoing, new GridPoint(col + 1, row), new GridPoint(col + 1, row + 1), 1);
                if (!SameLabel(col, row + 1, region.Label, labels, snapshot))
                    AddBoundary(edges, outgoing, new GridPoint(col + 1, row + 1), new GridPoint(col, row + 1), 2);
                if (!SameLabel(col - 1, row, region.Label, labels, snapshot))
                    AddBoundary(edges, outgoing, new GridPoint(col, row + 1), new GridPoint(col, row), 3);
            }
            edges.Sort((left, right) =>
            {
                int compare = left.Start.Z.CompareTo(right.Start.Z);
                if (compare != 0) return compare;
                compare = left.Start.X.CompareTo(right.Start.X);
                return compare != 0 ? compare : left.Direction.CompareTo(right.Direction);
            });
            foreach (BoundaryEdge first in edges)
            {
                if (first.Used) continue;
                var ring = new List<GridPoint> { first.Start };
                first.Used = true;
                GridPoint current = first.End;
                int incomingDirection = first.Direction;
                int guard = 0;
                while (!current.Equals(first.Start) && guard++ <= edges.Count)
                {
                    ring.Add(current);
                    if (!outgoing.TryGetValue(current, out List<BoundaryEdge> candidates)) break;
                    BoundaryEdge next = ChooseNext(candidates, incomingDirection);
                    if (next == null) break;
                    next.Used = true;
                    current = next.End;
                    incomingDirection = next.Direction;
                }
                if (current.Equals(first.Start) && ring.Count >= 4)
                {
                    RemoveCollinear(ring);
                    if (ring.Count >= 4)
                    {
                        region.VertexCount += ring.Count;
                        region.Rings.Add(ring);
                    }
                }
            }
            region.Rings.Sort((left, right) => Math.Abs(SignedArea(right)).CompareTo(Math.Abs(SignedArea(left))));
        }

        private static bool SameLabel(
            int col,
            int row,
            int label,
            int[] labels,
            LocalMapSnapshot snapshot)
        {
            return col >= 0 && row >= 0 && col < snapshot.Columns && row < snapshot.Rows
                && labels[row * snapshot.Columns + col] == label;
        }

        private static void AddBoundary(
            List<BoundaryEdge> edges,
            Dictionary<GridPoint, List<BoundaryEdge>> outgoing,
            GridPoint start,
            GridPoint end,
            int direction)
        {
            var edge = new BoundaryEdge { Start = start, End = end, Direction = direction };
            edges.Add(edge);
            if (!outgoing.TryGetValue(start, out List<BoundaryEdge> list))
            {
                list = new List<BoundaryEdge>();
                outgoing.Add(start, list);
            }
            list.Add(edge);
        }

        private static BoundaryEdge ChooseNext(List<BoundaryEdge> candidates, int incomingDirection)
        {
            BoundaryEdge best = null;
            int bestPriority = int.MaxValue;
            foreach (BoundaryEdge candidate in candidates)
            {
                if (candidate.Used) continue;
                int turn = (candidate.Direction - incomingDirection + 4) % 4;
                int priority = TurnPriority(turn);
                if (priority < bestPriority)
                {
                    best = candidate;
                    bestPriority = priority;
                }
            }
            return best;
        }

        private static int TurnPriority(int quarterTurns)
        {
            switch (quarterTurns)
            {
                case 3: return 0; // Keep the occupied cells on the left edge of the ring.
                case 0: return 1;
                case 1: return 2;
                default: return 3;
            }
        }

        private static void RemoveCollinear(List<GridPoint> ring)
        {
            bool changed = true;
            while (changed && ring.Count >= 4)
            {
                changed = false;
                for (int i = 0; i < ring.Count; i++)
                {
                    GridPoint previous = ring[(i + ring.Count - 1) % ring.Count];
                    GridPoint current = ring[i];
                    GridPoint next = ring[(i + 1) % ring.Count];
                    int cross = (current.X - previous.X) * (next.Z - current.Z)
                        - (current.Z - previous.Z) * (next.X - current.X);
                    if (cross == 0)
                    {
                        ring.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }
        }

        private static long SignedArea(List<GridPoint> ring)
        {
            long area = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                GridPoint current = ring[i];
                GridPoint next = ring[(i + 1) % ring.Count];
                area += (long)current.X * next.Z - (long)next.X * current.Z;
            }
            return area;
        }

        private static Dictionary<string, SectorStats> CalculateSectors(LocalMapSnapshot snapshot, bool[] buildable)
        {
            var result = new Dictionary<string, SectorStats>(StringComparer.Ordinal)
            {
                ["+x"] = new SectorStats(),
                ["-x"] = new SectorStats(),
                ["+z"] = new SectorStats(),
                ["-z"] = new SectorStats(),
            };
            for (int row = 0; row < snapshot.Rows; row++)
            {
                for (int col = 0; col < snapshot.Columns; col++)
                {
                    float worldX = snapshot.MinX + (col + 0.5f) * snapshot.Quantum;
                    float worldZ = snapshot.MinZ + (row + 0.5f) * snapshot.Quantum;
                    float dx = worldX - snapshot.FocusX;
                    float dz = worldZ - snapshot.FocusZ;
                    string key = SectorKey(dx, dz);
                    SectorStats stats = result[key];
                    int index = row * snapshot.Columns + col;
                    stats.Total++;
                    if (snapshot.Water[index]) stats.Water++;
                    if (buildable[index]) stats.Buildable++;
                }
            }
            return result;
        }

        private static int CompareRegions(Region left, Region right)
        {
            int compare = right.ContainsFocus.CompareTo(left.ContainsFocus);
            if (compare != 0) return compare;
            compare = KindPriority(left.Kind).CompareTo(KindPriority(right.Kind));
            if (compare != 0) return compare;
            compare = right.Cells.Count.CompareTo(left.Cells.Count);
            if (compare != 0) return compare;
            return left.Sequence.CompareTo(right.Sequence);
        }

        private static string SectorKey(float dx, float dz)
        {
            if (Math.Abs(dx) >= Math.Abs(dz))
            {
                return dx >= 0f ? "+x" : "-x";
            }
            return dz >= 0f ? "+z" : "-z";
        }

        private static int KindPriority(string kind)
        {
            switch (kind)
            {
                case "water": return 0;
                case "steep": return 1;
                case "buildable": return 2;
                default: return 3;
            }
        }

        private static string FormatRegion(Region region, LocalMapSnapshot snapshot)
        {
            string id = region.Prefix.ToString() + region.Sequence.ToString(CultureInfo.InvariantCulture);
            string touches = FormatTouches(region, snapshot);
            var line = new StringBuilder();
            line.Append("  ").Append(id).Append(" kind=").Append(region.Kind)
                .Append(" area_m2=").Append(F(region.Cells.Count * snapshot.Quantum * snapshot.Quantum, "0"))
                .Append(" bbox=[").Append(GridX(snapshot, region.MinX)).Append(',').Append(GridZ(snapshot, region.MinZ)).Append(',')
                .Append(GridX(snapshot, region.MaxX)).Append(',').Append(GridZ(snapshot, region.MaxZ)).Append(']')
                .Append(" contains_focus=").Append(Bool(region.ContainsFocus))
                .Append(" touches_bounds=").Append(touches).Append(' ');
            string rings = FormatRings(region.Rings, snapshot);
            if (line.Length + rings.Length + 1 <= kFeatureGeometryCharacterCap)
            {
                line.Append("rings=").Append(rings).Append('\n');
                return line.ToString();
            }
            string rows = FormatCellRows(region, snapshot);
            if (line.Length + rows.Length + 1 <= kFeatureGeometryCharacterCap)
            {
                line.Append("cell_rows=").Append(rows).Append(" geometry=exact_grid_fallback\n");
                return line.ToString();
            }
            line.Append("geometry=omitted vertices=").Append(region.VertexCount)
                .Append(" reason=feature_geometry_cap\n");
            return line.ToString();
        }

        private static string FormatRings(List<List<GridPoint>> rings, LocalMapSnapshot snapshot)
        {
            var result = new StringBuilder();
            result.Append('[');
            for (int i = 0; i < rings.Count; i++)
            {
                if (i > 0) result.Append('|');
                result.Append('[');
                List<GridPoint> ring = rings[i];
                for (int j = 0; j < ring.Count; j++)
                {
                    if (j > 0) result.Append(',');
                    result.Append('(').Append(GridX(snapshot, ring[j].X)).Append(',')
                        .Append(GridZ(snapshot, ring[j].Z)).Append(')');
                }
                result.Append(']');
            }
            result.Append(']');
            return result.ToString();
        }

        private static string FormatCellRows(Region region, LocalMapSnapshot snapshot)
        {
            var cells = new List<int>(region.Cells);
            cells.Sort();
            var result = new StringBuilder();
            result.Append('[');
            int i = 0;
            bool firstRun = true;
            while (i < cells.Count)
            {
                int row = cells[i] / snapshot.Columns;
                int start = cells[i] - row * snapshot.Columns;
                int end = start;
                i++;
                while (i < cells.Count)
                {
                    int nextRow = cells[i] / snapshot.Columns;
                    int nextCol = cells[i] - nextRow * snapshot.Columns;
                    if (nextRow != row || nextCol != end + 1) break;
                    end = nextCol;
                    i++;
                }
                if (!firstRun) result.Append(',');
                firstRun = false;
                result.Append("z=").Append(GridZ(snapshot, row)).Append(":x=").Append(GridX(snapshot, start));
                if (end != start) result.Append("..").Append(GridX(snapshot, end));
            }
            result.Append(']');
            return result.ToString();
        }

        private static string FormatTouches(Region region, LocalMapSnapshot snapshot)
        {
            var values = new List<string>();
            if (region.MinX == 0) values.Add("-x");
            if (region.MaxX == snapshot.Columns) values.Add("+x");
            if (region.MinZ == 0) values.Add("-z");
            if (region.MaxZ == snapshot.Rows) values.Add("+z");
            return values.Count == 0 ? "[]" : "[" + string.Join(",", values) + "]";
        }

        private static int CompareRoads(LocalMapRoad left, LocalMapRoad right, LocalMapSnapshot snapshot)
        {
            float leftDistance = RoadDistanceSquared(left, snapshot.FocusX, snapshot.FocusZ);
            float rightDistance = RoadDistanceSquared(right, snapshot.FocusX, snapshot.FocusZ);
            int compare = leftDistance.CompareTo(rightDistance);
            if (compare != 0) return compare;
            compare = left.EntityIndex.CompareTo(right.EntityIndex);
            return compare != 0 ? compare : left.EntityVersion.CompareTo(right.EntityVersion);
        }

        private static float RoadDistanceSquared(LocalMapRoad road, float x, float z)
        {
            float best = float.PositiveInfinity;
            foreach (LocalMapPoint point in road.Points)
            {
                float dx = point.X - x;
                float dz = point.Z - z;
                best = Math.Min(best, dx * dx + dz * dz);
            }
            return best;
        }

        private static List<GridPoint> QuantizeRoad(LocalMapRoad road, LocalMapSnapshot snapshot)
        {
            var result = new List<GridPoint>();
            foreach (LocalMapPoint point in road.Points)
            {
                var quantized = new GridPoint(
                    (int)Math.Round((point.X - snapshot.OriginX) / snapshot.Quantum),
                    (int)Math.Round((point.Z - snapshot.OriginZ) / snapshot.Quantum));
                if (result.Count == 0 || !result[result.Count - 1].Equals(quantized)) result.Add(quantized);
            }
            for (int i = result.Count - 2; i > 0; i--)
            {
                GridPoint previous = result[i - 1];
                GridPoint current = result[i];
                GridPoint next = result[i + 1];
                int cross = (current.X - previous.X) * (next.Z - current.Z)
                    - (current.Z - previous.Z) * (next.X - current.X);
                if (cross == 0) result.RemoveAt(i);
            }
            return result;
        }

        private static string FormatNode(string id, GridPoint point, int degree)
        {
            return "  node " + id + " at=(" + point.X.ToString(CultureInfo.InvariantCulture) + ","
                + point.Z.ToString(CultureInfo.InvariantCulture) + ") degree="
                + degree.ToString(CultureInfo.InvariantCulture) + "\n";
        }

        private static string FormatRoad(
            LocalMapRoad road,
            string startId,
            string endId,
            List<GridPoint> points)
        {
            var line = new StringBuilder();
            line.Append("  road R").Append(road.EntityIndex).Append('v').Append(road.EntityVersion)
                .Append(" class=").Append(SafeToken(road.Prefab))
                .Append(" from=").Append(startId).Append(" to=").Append(endId).Append(" line=[");
            for (int i = 0; i < points.Count; i++)
            {
                if (i > 0) line.Append(',');
                line.Append('(').Append(points[i].X).Append(',').Append(points[i].Z).Append(')');
            }
            line.Append("]\n");
            return line.ToString();
        }

        private static string NodeId(int index, int version)
        {
            return "N" + index.ToString(CultureInfo.InvariantCulture)
                + "v" + version.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatSector(SectorStats stats)
        {
            return "{water:" + Percent(stats.Water, stats.Total)
                + ",buildable:" + Percent(stats.Buildable, stats.Total) + "}";
        }

        private static string FormatCounts(Dictionary<string, int> counts)
        {
            if (counts.Count == 0) return "{}";
            var keys = new List<string>(counts.Keys);
            keys.Sort(StringComparer.Ordinal);
            var result = new StringBuilder("{");
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) result.Append(',');
                result.Append(keys[i]).Append(':').Append(counts[keys[i]]);
            }
            return result.Append('}').ToString();
        }

        private static void Increment(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int value);
            counts[key] = value + 1;
        }

        private static bool TryAppend(StringBuilder output, string value, int maximumLength)
        {
            if (output.Length + value.Length > maximumLength) return false;
            output.Append(value);
            return true;
        }

        private static string SafeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            var result = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                result.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_'
                    ? character
                    : '_');
            }
            return result.ToString();
        }

        private static string Percent(int value, int total)
        {
            if (total <= 0) return "0%";
            return F(value * 100f / total, "0.#") + "%";
        }

        private static string F(float value, string format)
        {
            return value.ToString(format, CultureInfo.InvariantCulture);
        }

        private static string Bool(bool value) => value ? "true" : "false";

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private static int GridX(LocalMapSnapshot snapshot, int column)
        {
            return LocalX(snapshot, snapshot.MinX) + column;
        }

        private static int GridZ(LocalMapSnapshot snapshot, int row)
        {
            return LocalZ(snapshot, snapshot.MinZ) + row;
        }

        private static int LocalX(LocalMapSnapshot snapshot, float worldX)
        {
            return (int)Math.Round((worldX - snapshot.OriginX) / snapshot.Quantum);
        }

        private static int LocalZ(LocalMapSnapshot snapshot, float worldZ)
        {
            return (int)Math.Round((worldZ - snapshot.OriginZ) / snapshot.Quantum);
        }
    }
}
