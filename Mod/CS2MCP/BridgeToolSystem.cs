using System;
using System.Collections.Generic;
using System.Text;
using Colossal.Mathematics;
using Game;
using Game.City;
using Game.Common;
using Game.Net;
using Game.Notifications;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.Zones;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Scripting;
using AgeMask = Game.Tools.AgeMask;
using Transform = Game.Objects.Transform;

namespace CS2MCP
{
    /// <summary>
    /// Headless placement tool. Activated programmatically for exactly three
    /// tool-update frames per operation:
    ///   1. CreateDefinitions — build definition entities (ported LineTool job),
    ///      applyMode=Clear lets the game generate preview Temp entities.
    ///   2. Apply — if validation passed (GetAllowApply), applyMode=Apply commits
    ///      the Temp entities to permanent ones; otherwise reject.
    ///   3. Finish — restore the previously active tool.
    /// </summary>
    public sealed partial class BridgeToolSystem : ObjectToolBaseSystem
    {
        private enum Stage
        {
            Idle,
            CreateDefinitions,
            Apply,
            ProbeCreate,
            ProbeValidate,
            ProbeClear,
            Finish,
        }

        private enum OperationKind
        {
            Object,
            Net,
            Probe,
            SearchPlace,
            Demolish,
            Upgrade,
            Area,
            Zone,
        }

        private Stage m_Stage = Stage.Idle;
        private OperationKind m_PendingKind;
        private Entity m_PendingPrefabEntity;
        private Entity m_PendingTarget;
        private PrefabBase m_PendingPrefab;
        private string m_PendingLabel;
        private float3 m_PendingPosition;
        private float3 m_PendingEnd;
        private float3 m_PendingMid;
        private bool m_PendingHasMid;
        private CompositionFlags m_PendingUpgradeFlags;
        private float3[] m_PendingAreaNodes;
        private ZoneType m_PendingZone;
        private float2 m_PendingZoneCenter;
        private float m_PendingZoneRadius;
        private float2 m_PendingZoneSize;
        private float m_PendingZoneRotationDegrees;
        private bool m_PendingZoneIsRectangle;
        private float2 m_PendingElevations;
        private quaternion m_PendingRotation;
        private readonly List<float3> m_ProbePositions = new List<float3>();
        private readonly List<float> m_ProbeRotations = new List<float>();
        private int m_ProbeIndex;
        private int m_ProbeTried;
        private int m_ProbeClearFrames;
        private float m_ProbeRotationDegrees;
        private string m_ProbeLastError = "";
        private BridgeRequest m_PendingRequest;
        private ToolBaseSystem m_PreviousTool;
        private bool m_AutoConnectQueued;
        private Entity m_AutoConnectPrefabEntity;
        private PrefabBase m_AutoConnectPrefab;
        private float3 m_AutoConnectStart;
        private float3 m_AutoConnectEnd;
        private string m_PlacedBuildingName;
        private float3 m_PlacedBuildingPosition;

        private CityConfigurationSystem m_CityConfigurationSystem;

        public override string toolID => "CS2MCP.Bridge";

        public bool IsBusy => m_Stage != Stage.Idle;

        /// <summary>
        /// Resets a bridge operation that has not finished within the watchdog
        /// window. Must be called on the simulation thread.
        /// </summary>
        public void AbortStuckOperation()
        {
            if (m_Stage == Stage.Idle)
            {
                return;
            }
            CompletePending(BridgeResponse.Error(504,
                "build operation aborted: it did not finish within the bridge watchdog window; " +
                "stage=" + m_Stage + " probeIndex=" + m_ProbeIndex + "/" + m_ProbePositions.Count +
                " tried=" + m_ProbeTried + " lastError=" + m_ProbeLastError));
            applyMode = ApplyMode.None;
            Deactivate();
        }

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_CityConfigurationSystem = base.World.GetOrCreateSystemManaged<CityConfigurationSystem>();
        }

        public override PrefabBase GetPrefab()
        {
            return m_PendingPrefab;
        }

        public override bool TrySetPrefab(PrefabBase prefab)
        {
            // Never let the game UI select this tool via asset selection.
            return false;
        }

        /// <summary>Must be called on the simulation thread.</summary>
        public bool TryQueuePlacement(
            Entity prefabEntity,
            PrefabBase prefab,
            float3 position,
            quaternion rotation,
            BridgeRequest request,
            Entity autoConnectPrefabEntity = default,
            PrefabBase autoConnectPrefab = null,
            float3 autoConnectStart = default,
            float3 autoConnectEnd = default)
        {
            if (m_Stage != Stage.Idle)
            {
                return false;
            }
            m_PendingKind = OperationKind.Object;
            m_PendingPrefabEntity = prefabEntity;
            m_PendingPrefab = prefab;
            m_PendingPosition = position;
            m_PendingRotation = rotation;
            m_PendingRequest = request;
            SetAutoConnect(autoConnectPrefabEntity, autoConnectPrefab, autoConnectStart, autoConnectEnd);
            Activate();
            return true;
        }

        /// <summary>
        /// Must be called on the simulation thread. Probes candidate object
        /// placements through the same validation pipeline (CreateDefinitions +
        /// GetAllowApply) without committing any of them, then completes the
        /// request with the first valid position found.
        /// </summary>
        public bool TryQueueProbe(
            Entity prefabEntity,
            PrefabBase prefab,
            IReadOnlyList<float3> positions,
            float rotationDegrees,
            BridgeRequest request)
        {
            if (m_Stage != Stage.Idle)
            {
                return false;
            }
            m_PendingKind = OperationKind.Probe;
            m_PendingPrefabEntity = prefabEntity;
            m_PendingPrefab = prefab;
            m_ProbePositions.Clear();
            m_ProbePositions.AddRange(positions);
            m_ProbeRotations.Clear();
            for (int i = 0; i < positions.Count; i++)
            {
                m_ProbeRotations.Add(rotationDegrees);
            }
            m_ProbeIndex = 0;
            m_ProbeTried = 0;
            m_ProbeRotationDegrees = rotationDegrees;
            m_ProbeLastError = "";
            m_PendingRequest = request;
            Activate();
            return true;
        }

        /// <summary>
        /// Must be called on the simulation thread. Finds the first valid,
        /// road-facing position among the candidates (each with its own
        /// rotation), then commits the placement in the same operation.
        /// </summary>
        public bool TryQueueSearchPlace(
            Entity prefabEntity,
            PrefabBase prefab,
            IReadOnlyList<float3> positions,
            IReadOnlyList<float> rotations,
            BridgeRequest request,
            Entity autoConnectPrefabEntity = default,
            PrefabBase autoConnectPrefab = null,
            float3 autoConnectStart = default,
            float3 autoConnectEnd = default)
        {
            if (m_Stage != Stage.Idle)
            {
                return false;
            }
            if (positions.Count == 0 || positions.Count != rotations.Count)
            {
                return false;
            }
            m_PendingKind = OperationKind.SearchPlace;
            m_PendingPrefabEntity = prefabEntity;
            m_PendingPrefab = prefab;
            m_ProbePositions.Clear();
            m_ProbePositions.AddRange(positions);
            m_ProbeRotations.Clear();
            m_ProbeRotations.AddRange(rotations);
            m_ProbeIndex = 0;
            m_ProbeTried = 0;
            m_ProbeLastError = "";
            m_PendingRequest = request;
            SetAutoConnect(autoConnectPrefabEntity, autoConnectPrefab, autoConnectStart, autoConnectEnd);
            Activate();
            return true;
        }

        private void SetAutoConnect(
            Entity prefabEntity,
            PrefabBase prefab,
            float3 start,
            float3 end)
        {
            m_AutoConnectQueued = prefabEntity != Entity.Null && prefab != null;
            m_AutoConnectPrefabEntity = prefabEntity;
            m_AutoConnectPrefab = prefab;
            m_AutoConnectStart = start;
            m_AutoConnectEnd = end;
        }

        /// <summary>Must be called on the simulation thread.</summary>
        public bool TryQueueRoad(Entity prefabEntity, PrefabBase prefab, float3 start, float3 end, float3 mid, bool hasMid, float2 elevations, BridgeRequest request)
        {
            if (m_Stage != Stage.Idle)
            {
                return false;
            }
            m_PendingKind = OperationKind.Net;
            m_PendingPrefabEntity = prefabEntity;
            m_PendingPrefab = prefab;
            m_PendingPosition = start;
            m_PendingEnd = end;
            m_PendingMid = mid;
            m_PendingHasMid = hasMid;
            m_PendingElevations = elevations;
            m_PendingRotation = quaternion.identity;
            m_PendingRequest = request;
            Activate();
            return true;
        }

        /// <summary>Must be called on the simulation thread.</summary>
        public bool TryQueueUpgrade(Entity target, string label, CompositionFlags upgradeFlags, BridgeRequest request)
        {
            if (m_Stage != Stage.Idle)
            {
                return false;
            }
            m_PendingKind = OperationKind.Upgrade;
            m_PendingTarget = target;
            m_PendingLabel = label;
            m_PendingUpgradeFlags = upgradeFlags;
            m_PendingRequest = request;
            Activate();
            return true;
        }

        /// <summary>Must be called on the simulation thread.</summary>
        public bool TryQueueDemolish(Entity target, string label, BridgeRequest request)
        {
            if (m_Stage != Stage.Idle)
            {
                return false;
            }
            m_PendingKind = OperationKind.Demolish;
            m_PendingTarget = target;
            m_PendingLabel = label;
            m_PendingRequest = request;
            Activate();
            return true;
        }

        /// <summary>Must be called on the simulation thread.</summary>
        public bool TryQueueArea(Entity prefabEntity, PrefabBase prefab, float3[] polygonNodes, BridgeRequest request)
        {
            if (m_Stage != Stage.Idle)
            {
                return false;
            }
            m_PendingKind = OperationKind.Area;
            m_PendingPrefabEntity = prefabEntity;
            m_PendingPrefab = prefab;
            m_PendingAreaNodes = polygonNodes;
            m_PendingRequest = request;
            Activate();
            return true;
        }

        /// <summary>
        /// Queues zone-cell painting for ToolUpdate, where ToolOutputBarrier is
        /// open and the game's zone cell-check lifecycle can observe Updated.
        /// Must be called on the simulation thread.
        /// </summary>
        public bool TryQueueZoneCircle(ZoneType zone, string label, float2 center, float radius, BridgeRequest request)
        {
            if (m_Stage != Stage.Idle)
            {
                return false;
            }
            m_PendingKind = OperationKind.Zone;
            m_PendingZone = zone;
            m_PendingLabel = label;
            m_PendingZoneCenter = center;
            m_PendingZoneRadius = radius;
            m_PendingZoneSize = default;
            m_PendingZoneRotationDegrees = 0f;
            m_PendingZoneIsRectangle = false;
            m_PendingRequest = request;
            Activate();
            return true;
        }

        /// <summary>Queues a rotated rectangular zone brush for ToolUpdate.</summary>
        public bool TryQueueZoneRectangle(
            ZoneType zone,
            string label,
            float2 center,
            float2 size,
            float rotationDegrees,
            BridgeRequest request)
        {
            if (m_Stage != Stage.Idle)
            {
                return false;
            }
            m_PendingKind = OperationKind.Zone;
            m_PendingZone = zone;
            m_PendingLabel = label;
            m_PendingZoneCenter = center;
            m_PendingZoneSize = size;
            m_PendingZoneRotationDegrees = rotationDegrees;
            m_PendingZoneRadius = math.length(size * 0.5f);
            m_PendingZoneIsRectangle = true;
            m_PendingRequest = request;
            Activate();
            return true;
        }

        private void Activate()
        {
            m_Stage = Stage.CreateDefinitions;
            m_PreviousTool = m_ToolSystem.activeTool;
            m_ToolSystem.activeTool = this;
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            try
            {
                switch (m_Stage)
                {
                    case Stage.CreateDefinitions:
                        applyMode = ApplyMode.Clear;
                        switch (m_PendingKind)
                        {
                            case OperationKind.Object:
                                CreatePlacementDefinitions();
                                break;
                            case OperationKind.Net:
                                CreateRoadDefinitions();
                                break;
                            case OperationKind.Probe:
                            case OperationKind.SearchPlace:
                                ApplyProbeCandidate();
                                // The game may switch the active tool away after a
                                // rejected preview; re-assert ownership so the probe
                                // state machine keeps advancing between candidates.
                                if (m_ToolSystem.activeTool != this)
                                {
                                    m_ToolSystem.activeTool = this;
                                }
                                CreatePlacementDefinitions();
                                break;
                            case OperationKind.Demolish:
                                CreateModifyDefinitions(CreationFlags.Delete, default);
                                break;
                            case OperationKind.Upgrade:
                                CreateModifyDefinitions(CreationFlags.Upgrade, m_PendingUpgradeFlags);
                                break;
                            case OperationKind.Area:
                                CreateAreaDefinitions();
                                break;
                            case OperationKind.Zone:
                                ApplyZoneCells();
                                break;
                        }
                        m_Stage = m_PendingKind == OperationKind.Zone
                            ? Stage.Finish
                            : m_PendingKind == OperationKind.Probe
                            || m_PendingKind == OperationKind.SearchPlace
                            ? Stage.ProbeValidate
                            : Stage.Apply;
                        break;

                    case Stage.Apply:
                        if (GetAllowApply())
                        {
                            applyMode = ApplyMode.Apply;
                            if (m_AutoConnectQueued && m_PendingKind == OperationKind.Net)
                            {
                                CompletePending(BuildAutoConnectResponse());
                                m_AutoConnectQueued = false;
                            }
                            else if (m_AutoConnectQueued)
                            {
                                // Building committed. Chain the utility connector
                                // (pipe/cable to the nearest road) in the same
                                // operation so the model does not have to place
                                // it manually.
                                m_PlacedBuildingName = m_PendingPrefab != null
                                    ? m_PendingPrefab.name
                                    : null;
                                m_PlacedBuildingPosition = m_PendingPosition;
                                m_Stage = Stage.CreateDefinitions;
                                m_PendingKind = OperationKind.Net;
                                m_PendingPrefabEntity = m_AutoConnectPrefabEntity;
                                m_PendingPrefab = m_AutoConnectPrefab;
                                m_PendingPosition = m_PlacedBuildingPosition;
                                m_PendingEnd = m_AutoConnectEnd;
                                m_PendingMid = default;
                                m_PendingHasMid = false;
                                // Pipes and ground cables belong underground;
                                // high-voltage lines run above ground.
                                string netName = m_AutoConnectPrefab != null
                                    ? m_AutoConnectPrefab.name
                                    : "";
                                m_PendingElevations =
                                    netName.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0
                                    || netName.IndexOf("Ground Cable", StringComparison.OrdinalIgnoreCase) >= 0
                                        ? new float2(-10f, -10f)
                                        : default;
                            }
                            else
                            {
                                CompletePending(BuildSuccessResponse());
                            }
                        }
                        else
                        {
                            applyMode = ApplyMode.Clear;
                            if (m_AutoConnectQueued && m_PendingKind == OperationKind.Net)
                            {
                                CompletePending(BuildAutoConnectFailedResponse());
                                m_AutoConnectQueued = false;
                            }
                            else
                            {
                                CompletePending(BridgeResponse.Error(409, DescribeValidationBlock()));
                            }
                        }
                        if (m_Stage != Stage.CreateDefinitions)
                        {
                            m_Stage = Stage.Finish;
                        }
                        break;

                    case Stage.ProbeValidate:
                        m_ProbeTried++;
                        bool allowApply = GetAllowApply();
                        string validationBlock = allowApply && RequiresRoadAccess(m_PendingPrefabEntity)
                            ? DescribeValidationBlock()
                            : null;
                        bool roadBlocked = validationBlock != null &&
                            validationBlock.IndexOf("NoRoadAccess", StringComparison.Ordinal) >= 0;
                        if (allowApply && !roadBlocked)
                        {
                            if (m_PendingKind == OperationKind.SearchPlace)
                            {
                                // Valid and road-facing: commit this candidate in
                                // the same operation (one-step find+place).
                                applyMode = ApplyMode.Apply;
                                m_Stage = Stage.Apply;
                            }
                            else
                            {
                                applyMode = ApplyMode.Clear;
                                CompletePending(BuildProbeSuccess());
                                m_Stage = Stage.Finish;
                            }
                        }
                        else
                        {
                            m_ProbeLastError = validationBlock ?? DescribeValidationBlock();
                            applyMode = ApplyMode.Clear;
                            m_ProbeIndex++;
                            if (m_ProbeIndex < m_ProbePositions.Count)
                            {
                                m_ProbeClearFrames = 4;
                                m_Stage = Stage.ProbeClear;
                            }
                            else
                            {
                                CompletePending(BridgeResponse.Error(404, DescribeProbeFailure()));
                                m_Stage = Stage.Finish;
                            }
                        }
                        break;

                    case Stage.ProbeClear:
                        // Give the game several frames to actually clear the
                        // rejected preview before probing the next candidate;
                        // switching immediately wedges the tool pipeline.
                        applyMode = ApplyMode.Clear;
                        if (m_ProbeClearFrames > 0)
                        {
                            m_ProbeClearFrames--;
                        }
                        else
                        {
                            m_Stage = Stage.ProbeCreate;
                        }
                        break;

                    case Stage.Finish:
                        applyMode = ApplyMode.None;
                        Deactivate();
                        break;

                    default:
                        applyMode = ApplyMode.None;
                        break;
                }
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"BridgeToolSystem error in stage {m_Stage}: {e}");
                CompletePending(BridgeResponse.Error(500, $"tool operation failed: {e.GetType().Name}: {e.Message}"));
                applyMode = ApplyMode.None;
                Deactivate();
            }
            return inputDeps;
        }

        private void ApplyProbeCandidate()
        {
            if (m_ProbeIndex < 0 || m_ProbeIndex >= m_ProbePositions.Count)
            {
                return;
            }
            m_PendingPosition = m_ProbePositions[m_ProbeIndex];
            float degrees = m_ProbeIndex < m_ProbeRotations.Count
                ? m_ProbeRotations[m_ProbeIndex]
                : m_ProbeRotationDegrees;
            m_PendingRotation = quaternion.RotateY(math.radians(degrees));
        }

        private void ApplyZoneCells()
        {
            const float cellSize = 8f;
            int cellsChanged = 0;
            int blocksTouched = 0;
            EntityCommandBuffer commandBuffer = m_ToolOutputBarrier.CreateCommandBuffer();
            using (EntityQuery query = EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Block>(),
                    ComponentType.ReadOnly<Cell>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Game.Tools.Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                },
            }))
            using (NativeArray<Entity> blocks = query.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity blockEntity in blocks)
                {
                    Block block = EntityManager.GetComponentData<Block>(blockEntity);
                    float blockExtent = cellSize * (math.cmax(block.m_Size) + 1) * 0.71f;
                    if (math.distance(block.m_Position.xz, m_PendingZoneCenter) > m_PendingZoneRadius + blockExtent)
                    {
                        continue;
                    }

                    DynamicBuffer<Cell> cells = EntityManager.GetBuffer<Cell>(blockEntity);
                    int blockCells = 0;
                    for (int cellZ = 0; cellZ < block.m_Size.y; cellZ++)
                    {
                        for (int cellX = 0; cellX < block.m_Size.x; cellX++)
                        {
                            int index = cellZ * block.m_Size.x + cellX;
                            if (index >= cells.Length)
                            {
                                continue;
                            }
                            Cell cell = cells[index];
                            if ((cell.m_State & CellFlags.Visible) == 0
                                || (cell.m_State & (CellFlags.Blocked | CellFlags.Overridden)) != 0
                                || cell.m_Zone.Equals(m_PendingZone))
                            {
                                continue;
                            }
                            float3 cellPosition = ZoneUtils.GetCellPosition(block, new int2(cellX, cellZ));
                            if (!PendingZoneContains(cellPosition.xz))
                            {
                                continue;
                            }
                            cell.m_Zone = m_PendingZone;
                            cells[index] = cell;
                            blockCells++;
                        }
                    }

                    if (blockCells == 0)
                    {
                        continue;
                    }
                    cellsChanged += blockCells;
                    blocksTouched++;
                    if (!EntityManager.HasComponent<Updated>(blockEntity))
                    {
                        commandBuffer.AddComponent<Updated>(blockEntity);
                    }
                }
            }

            var payload = new Dictionary<string, object>
            {
                ["zone"] = m_PendingLabel,
                ["shape"] = m_PendingZoneIsRectangle ? "rectangle" : "circle",
                ["center"] = new { x = m_PendingZoneCenter.x, z = m_PendingZoneCenter.y },
                ["cellsChanged"] = cellsChanged,
                ["blocksTouched"] = blocksTouched,
                ["note"] = cellsChanged == 0
                    ? "no zonable cells found in shape - zone cells only exist along roads and must be unoccupied"
                    : "painted zone cells during ToolUpdate; run the simulation for VacantLots/buildings",
            };
            if (m_PendingZoneIsRectangle)
            {
                payload["width"] = m_PendingZoneSize.x;
                payload["depth"] = m_PendingZoneSize.y;
                payload["rotation"] = m_PendingZoneRotationDegrees;
            }
            else
            {
                payload["radius"] = m_PendingZoneRadius;
            }
            CompletePending(BridgeResponse.Json(payload));
        }

        private bool PendingZoneContains(float2 position)
        {
            float2 delta = position - m_PendingZoneCenter;
            if (!m_PendingZoneIsRectangle)
            {
                return math.length(delta) <= m_PendingZoneRadius;
            }

            float radians = math.radians(m_PendingZoneRotationDegrees);
            float sine = math.sin(radians);
            float cosine = math.cos(radians);
            float2 local = new float2(
                cosine * delta.x + sine * delta.y,
                -sine * delta.x + cosine * delta.y);
            float2 halfSize = m_PendingZoneSize * 0.5f;
            return math.abs(local.x) <= halfSize.x && math.abs(local.y) <= halfSize.y;
        }

        private BridgeResponse BuildAutoConnectResponse()
        {
            return BridgeResponse.Json(new
            {
                placed = true,
                prefab = m_PlacedBuildingName,
                position = new
                {
                    x = m_PlacedBuildingPosition.x,
                    y = m_PlacedBuildingPosition.y,
                    z = m_PlacedBuildingPosition.z,
                },
                connected = true,
                connection = new
                {
                    prefab = m_PendingPrefab != null ? m_PendingPrefab.name : null,
                    start = new { x = m_PlacedBuildingPosition.x, z = m_PlacedBuildingPosition.z },
                    end = new { x = m_AutoConnectEnd.x, z = m_AutoConnectEnd.z },
                },
                note = "building placed; utility connector (pipe/cable) auto-built to the nearest road network",
            });
        }

        private BridgeResponse BuildAutoConnectFailedResponse()
        {
            return BridgeResponse.Json(new
            {
                placed = true,
                prefab = m_PlacedBuildingName,
                position = new
                {
                    x = m_PlacedBuildingPosition.x,
                    y = m_PlacedBuildingPosition.y,
                    z = m_PlacedBuildingPosition.z,
                },
                connected = false,
                connectionError = DescribeValidationBlock(),
                note = "building placed, but the automatic utility connector was rejected by the game; connect it manually with build_road",
            });
        }

        private BridgeResponse BuildSuccessResponse()
        {
            switch (m_PendingKind)
            {
                case OperationKind.Demolish:
                    return BridgeResponse.Json(new
                    {
                        demolished = true,
                        prefab = m_PendingLabel,
                        entity = new { index = m_PendingTarget.Index, version = m_PendingTarget.Version },
                        note = "deleted via the game's bulldoze pipeline (nodes/blocks/lanes cleaned up by the game)",
                    });
                case OperationKind.Upgrade:
                    return BridgeResponse.Json(new
                    {
                        upgraded = true,
                        prefab = m_PendingLabel,
                        entity = new { index = m_PendingTarget.Index, version = m_PendingTarget.Version },
                        note = "upgrade applied via the tool pipeline; the segment is recreated with the new composition",
                    });
                case OperationKind.Net:
                    float? widthM = null;
                    if (EntityManager.HasComponent<NetGeometryData>(m_PendingPrefabEntity))
                    {
                        widthM = (float?)Math.Round(
                            EntityManager.GetComponentData<NetGeometryData>(m_PendingPrefabEntity).m_DefaultWidth,
                            1);
                    }
                    return BridgeResponse.Json(new
                    {
                        placed = true,
                        prefab = m_PendingPrefab != null ? m_PendingPrefab.name : null,
                        start = new { x = m_PendingPosition.x, z = m_PendingPosition.z },
                        end = new { x = m_PendingEnd.x, z = m_PendingEnd.z },
                        widthM,
                        note = "committed this frame; verify via /city/roads or /screenshot",
                    });
                case OperationKind.Area:
                    return BridgeResponse.Json(new
                    {
                        created = true,
                        prefab = m_PendingPrefab != null ? m_PendingPrefab.name : null,
                        nodes = m_PendingAreaNodes != null ? m_PendingAreaNodes.Length : 0,
                        note = "area committed this frame; list districts via /districts",
                    });
                default:
                    object lotSize = null;
                    object footprintMeters = null;
                    if (EntityManager.HasComponent<BuildingData>(m_PendingPrefabEntity))
                    {
                        int2 lot = EntityManager.GetComponentData<BuildingData>(m_PendingPrefabEntity).m_LotSize;
                        lotSize = new { x = lot.x, z = lot.y };
                        footprintMeters = new
                        {
                            x = (float)Math.Round(lot.x * 8f, 1),
                            z = (float)Math.Round(lot.y * 8f, 1),
                        };
                    }
                    return BridgeResponse.Json(new
                    {
                        placed = true,
                        prefab = m_PendingPrefab != null ? m_PendingPrefab.name : null,
                        position = new
                        {
                            x = m_PendingPosition.x,
                            y = m_PendingPosition.y,
                            z = m_PendingPosition.z,
                        },
                        lotSize,
                        footprintMeters,
                        note = "committed this frame; verify via /city/buildings or /screenshot",
                    });
            }
        }

        private BridgeResponse BuildProbeSuccess()
        {
            bool isBuilding = RequiresRoadAccess(m_PendingPrefabEntity);
            var payload = new Dictionary<string, object>
            {
                ["found"] = true,
                ["prefab"] = m_PendingPrefab != null ? m_PendingPrefab.name : null,
                ["position"] = new
                {
                    x = m_PendingPosition.x,
                    y = m_PendingPosition.y,
                    z = m_PendingPosition.z,
                },
                ["rotation"] = m_ProbeRotationDegrees,
                ["attemptsTried"] = m_ProbeTried,
                ["note"] = isBuilding
                    ? "validated by the game's placement validation WITH road access; call place_building with these exact coordinates"
                    : "validated by the game's placement validation; call place_building with these exact coordinates",
            };
            if (isBuilding)
            {
                payload["roadAccess"] = true;
            }
            return BridgeResponse.Json(payload);
        }

        private bool RequiresRoadAccess(Entity prefabEntity)
        {
            return prefabEntity != Entity.Null &&
                EntityManager.HasComponent<BuildingData>(prefabEntity);
        }

        private string DescribeProbeFailure()
        {
            string lastError = string.IsNullOrWhiteSpace(m_ProbeLastError)
                ? "unknown"
                : m_ProbeLastError;
            return "no valid placement found near the requested position: tried " +
                   m_ProbeTried + " candidate(s). Last reason: " + lastError +
                   " Try a larger radius, a different rotation (90/180/270), or a center closer to a road.";
        }

        private void CompletePending(BridgeResponse response)
        {
            m_PendingRequest?.Complete(response);
            m_PendingRequest = null;
        }

        /// <summary>
        /// Pull ErrorType values off temp entities via their notification icons
        /// (IconElement → PrefabRef → ToolErrorData), so the agent sees why
        /// GetAllowApply failed instead of a generic overlap/water list.
        /// </summary>
        private string DescribeValidationBlock()
        {
            var reasons = new List<string>();
            using (NativeArray<Entity> entities = m_ErrorQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    CollectErrorReasons(entities[i], reasons);
                }
            }

            var message = new StringBuilder("operation blocked by game validation");
            if (reasons.Count > 0)
            {
                var unique = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (string reason in reasons)
                {
                    if (seen.Add(reason))
                    {
                        unique.Add(reason);
                    }
                }
                message.Append(": ").Append(string.Join("; ", unique));
            }
            else
            {
                message.Append(" (no ErrorType icons on temp entities)");
            }

            if (m_PendingKind == OperationKind.Net)
            {
                float length = math.distance(
                    new float2(m_PendingPosition.x, m_PendingPosition.z),
                    new float2(m_PendingEnd.x, m_PendingEnd.z));
                message.Append(
                    $". hint: prefer short segments on owned land near existing roads (this attempt ~{length:F0}m); " +
                    "e1/e2 are elevation meters (-30..60), not entity indexes; omit them for ground-level roads");
            }
            else
            {
                message.Append("; try a different position or target");
            }

            return message.ToString();
        }

        private void CollectErrorReasons(Entity entity, List<string> reasons)
        {
            if (EntityManager.HasComponent<PrefabRef>(entity))
            {
                TryAddToolError(EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab, reasons);
            }

            if (!EntityManager.HasBuffer<IconElement>(entity))
            {
                return;
            }

            DynamicBuffer<IconElement> icons = EntityManager.GetBuffer<IconElement>(entity, true);
            for (int i = 0; i < icons.Length; i++)
            {
                Entity icon = icons[i].m_Icon;
                if (icon == Entity.Null || !EntityManager.Exists(icon))
                {
                    continue;
                }
                if (EntityManager.HasComponent<PrefabRef>(icon))
                {
                    TryAddToolError(EntityManager.GetComponentData<PrefabRef>(icon).m_Prefab, reasons);
                }
            }
        }

        private void TryAddToolError(Entity prefab, List<string> reasons)
        {
            if (prefab == Entity.Null || !EntityManager.HasComponent<ToolErrorData>(prefab))
            {
                return;
            }

            ErrorType error = EntityManager.GetComponentData<ToolErrorData>(prefab).m_Error;
            if (error == ErrorType.None)
            {
                return;
            }

            reasons.Add(DescribeErrorType(error));
        }

        private static string DescribeErrorType(ErrorType error)
        {
            switch (error)
            {
                case ErrorType.OverlapExisting:
                    return "OverlapExisting (overlaps building/prop/network)";
                case ErrorType.InvalidShape:
                    return "InvalidShape (geometry not valid here)";
                case ErrorType.NotEnoughMoney:
                    return "NotEnoughMoney";
                case ErrorType.LongDistance:
                    return "LongDistance (too far / split into shorter segments)";
                case ErrorType.TightCurve:
                    return "TightCurve (curve too sharp)";
                case ErrorType.InWater:
                    return "InWater (cross water needs bridge elevation or another route)";
                case ErrorType.ExceedsCityLimits:
                    return "ExceedsCityLimits (outside owned map tiles)";
                case ErrorType.AlreadyExists:
                    return "AlreadyExists";
                case ErrorType.ShortDistance:
                    return "ShortDistance (endpoints too close)";
                case ErrorType.LowElevation:
                    return "LowElevation";
                case ErrorType.SteepSlope:
                    return "SteepSlope";
                case ErrorType.SmallArea:
                    return "SmallArea";
                case ErrorType.ExceedsLotLimits:
                    return "ExceedsLotLimits";
                case ErrorType.OnFire:
                    return "OnFire";
                default:
                    return error.ToString();
            }
        }

        private void Deactivate()
        {
            m_Stage = Stage.Idle;
            m_PendingRequest = null;
            m_PendingPrefab = null;
            m_PendingPrefabEntity = Entity.Null;
            m_ProbePositions.Clear();
            m_ProbeRotations.Clear();
            m_ProbeIndex = 0;
            m_ProbeTried = 0;
            m_ProbeClearFrames = 0;
            m_ProbeLastError = "";
            m_AutoConnectQueued = false;
            m_AutoConnectPrefabEntity = Entity.Null;
            m_AutoConnectPrefab = null;
            m_PlacedBuildingName = null;
            if (m_ToolSystem.activeTool == this)
            {
                m_ToolSystem.activeTool = m_PreviousTool != null ? m_PreviousTool : m_DefaultToolSystem;
            }
            m_PreviousTool = null;
        }

        /// <summary>
        /// Creates a modify definition for the pending target, faithfully
        /// mirroring BulldozeToolSystem.AddEntity (CreationDefinition with
        /// m_Original + flags plus a NetCourse/ObjectDefinition describing the
        /// original). Used for Delete (bulldoze) and Upgrade (road upgrades:
        /// grass/trees/lighting...). The game's generate/apply systems handle
        /// all related cleanup/recreation — nodes, zone blocks, lanes — which a
        /// raw component edit would skip (and skipping corrupts state).
        /// </summary>
        private void CreateModifyDefinitions(CreationFlags flags, CompositionFlags upgrades)
        {
            Entity target = m_PendingTarget;
            EntityCommandBuffer commandBuffer = m_ToolOutputBarrier.CreateCommandBuffer();
            Entity e = commandBuffer.CreateEntity();
            var definition = new CreationDefinition
            {
                m_Original = target,
                m_Flags = flags,
            };
            commandBuffer.AddComponent(e, default(Updated));
            if (upgrades != default(CompositionFlags))
            {
                commandBuffer.AddComponent(e, new Game.Net.Upgraded { m_Flags = upgrades });
            }

            if (EntityManager.HasComponent<Game.Net.Edge>(target))
            {
                Game.Net.Edge edge = EntityManager.GetComponentData<Game.Net.Edge>(target);
                NetCourse course = default;
                course.m_Curve = EntityManager.GetComponentData<Game.Net.Curve>(target).m_Bezier;
                course.m_Length = MathUtils.Length(course.m_Curve);
                course.m_FixedIndex = EntityManager.HasComponent<Game.Net.Fixed>(target)
                    ? EntityManager.GetComponentData<Game.Net.Fixed>(target).m_Index
                    : -1;
                course.m_StartPosition.m_Entity = edge.m_Start;
                course.m_StartPosition.m_Position = course.m_Curve.a;
                course.m_StartPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(course.m_Curve));
                course.m_StartPosition.m_CourseDelta = 0f;
                course.m_EndPosition.m_Entity = edge.m_End;
                course.m_EndPosition.m_Position = course.m_Curve.d;
                course.m_EndPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(course.m_Curve));
                course.m_EndPosition.m_CourseDelta = 1f;
                commandBuffer.AddComponent(e, course);
            }
            else if (EntityManager.HasComponent<Transform>(target))
            {
                Transform transform = EntityManager.GetComponentData<Transform>(target);
                var objectDefinition = new ObjectDefinition
                {
                    m_Position = transform.m_Position,
                    m_Rotation = transform.m_Rotation,
                    m_Probability = 100,
                    m_PrefabSubIndex = -1,
                    m_LocalPosition = transform.m_Position,
                    m_LocalRotation = transform.m_Rotation,
                };
                if (EntityManager.HasComponent<Game.Objects.Elevation>(target))
                {
                    Game.Objects.Elevation elevation = EntityManager.GetComponentData<Game.Objects.Elevation>(target);
                    objectDefinition.m_Elevation = elevation.m_Elevation;
                    objectDefinition.m_ParentMesh = Game.Objects.ObjectUtils.GetSubParentMesh(elevation.m_Flags);
                }
                else
                {
                    objectDefinition.m_ParentMesh = -1;
                }
                commandBuffer.AddComponent(e, objectDefinition);
            }
            else if (EntityManager.HasBuffer<Game.Areas.Node>(target))
            {
                DynamicBuffer<Game.Areas.Node> nodes = EntityManager.GetBuffer<Game.Areas.Node>(target, isReadOnly: true);
                commandBuffer.AddBuffer<Game.Areas.Node>(e).CopyFrom(nodes.AsNativeArray());
            }

            commandBuffer.AddComponent(e, definition);
        }

        /// <summary>
        /// Creates an area definition (district/surface): CreationDefinition +
        /// polygon node buffer; elevation float.MinValue snaps nodes to terrain
        /// (mirrors the game's area definition flow).
        /// </summary>
        private void CreateAreaDefinitions()
        {
            EntityCommandBuffer commandBuffer = m_ToolOutputBarrier.CreateCommandBuffer();
            Entity e = commandBuffer.CreateEntity();
            Unity.Mathematics.Random random = RandomSeed.Next().GetRandom(0);
            commandBuffer.AddComponent(e, new CreationDefinition
            {
                m_Prefab = m_PendingPrefabEntity,
                m_RandomSeed = random.NextInt(),
            });
            commandBuffer.AddComponent(e, default(Updated));
            DynamicBuffer<Game.Areas.Node> nodes = commandBuffer.AddBuffer<Game.Areas.Node>(e);
            foreach (float3 position in m_PendingAreaNodes)
            {
                nodes.Add(new Game.Areas.Node(position, float.MinValue));
            }
        }

        /// <summary>
        /// Creates a standalone straight-road course definition from the pending
        /// start to end position, terrain-following (mirrors the standalone-net
        /// branch of the game's net definition flow).
        /// </summary>
        private void CreateRoadDefinitions()
        {
            TerrainHeightData terrainHeight = m_TerrainSystem.GetHeightData();

            Curve rawCurve = default;
            if (m_PendingHasMid)
            {
                // Quadratic bezier through the mid control point, elevated to cubic.
                float3 a = m_PendingPosition;
                float3 d = m_PendingEnd;
                float3 m = m_PendingMid;
                rawCurve.m_Bezier = new Bezier4x3(a, a + (m - a) * (2f / 3f), d + (m - d) * (2f / 3f), d);
            }
            else
            {
                rawCurve.m_Bezier = NetUtils.StraightCurve(m_PendingPosition, m_PendingEnd);
            }
            Bezier4x3 adjusted = NetUtils.AdjustPosition(
                rawCurve, fixedStart: false, linearMiddle: false, fixedEnd: false, ref terrainHeight).m_Bezier;

            float e1 = m_PendingElevations.x;
            float e2 = m_PendingElevations.y;
            if (e1 != 0f || e2 != 0f)
            {
                // Lift the terrain-following curve by linearly interpolated
                // elevation; the pipeline turns nonzero course elevations into
                // bridge/elevated segments with pillars.
                adjusted.a.y += e1;
                adjusted.b.y += math.lerp(e1, e2, 1f / 3f);
                adjusted.c.y += math.lerp(e1, e2, 2f / 3f);
                adjusted.d.y += e2;
            }

            NetCourse course = default;
            course.m_Curve = adjusted;
            course.m_Elevation = new float2(math.min(e1, e2), math.max(e1, e2));
            course.m_StartPosition.m_Position = course.m_Curve.a;
            course.m_StartPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(course.m_Curve));
            course.m_StartPosition.m_CourseDelta = 0f;
            course.m_StartPosition.m_ParentMesh = -1;
            course.m_StartPosition.m_Elevation = e1;
            course.m_StartPosition.m_Flags = CoursePosFlags.IsFirst | CoursePosFlags.FreeHeight;
            course.m_EndPosition.m_Position = course.m_Curve.d;
            course.m_EndPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(course.m_Curve));
            course.m_EndPosition.m_CourseDelta = 1f;
            course.m_EndPosition.m_ParentMesh = -1;
            course.m_EndPosition.m_Elevation = e2;
            course.m_EndPosition.m_Flags = CoursePosFlags.IsLast | CoursePosFlags.FreeHeight;
            course.m_Length = MathUtils.Length(course.m_Curve);
            course.m_FixedIndex = -1;

            Unity.Mathematics.Random random = RandomSeed.Next().GetRandom(0);
            EntityCommandBuffer commandBuffer = m_ToolOutputBarrier.CreateCommandBuffer();
            Entity entity = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(entity, new CreationDefinition
            {
                m_Prefab = m_PendingPrefabEntity,
                m_RandomSeed = random.NextInt(),
            });
            commandBuffer.AddComponent(entity, default(Updated));
            commandBuffer.AddComponent(entity, course);
        }

        /// <summary>
        /// Fills and synchronously executes the ported LineTool definition job
        /// for a single object at the pending position/rotation.
        /// </summary>
        private void CreatePlacementDefinitions()
        {
            CreateDefinitions definitions = default;
            definitions.m_RandomizationEnabled = false;
            definitions.m_FixedRandomSeed = 0;
            definitions.m_EditorMode = m_ToolSystem.actionMode.IsEditor();
            definitions.m_LefthandTraffic = m_CityConfigurationSystem.leftHandTraffic;
            definitions.m_ObjectPrefab = m_PendingPrefabEntity;
            definitions.m_Theme = m_CityConfigurationSystem.defaultTheme;
            definitions.m_RandomSeed = RandomSeed.Next();
            definitions.m_AgeMask = AgeMask.Mature;
            definitions.m_ControlPoint = new ControlPoint
            {
                m_Position = m_PendingPosition,
                m_Rotation = m_PendingRotation,
            };
            definitions.m_AttachmentPrefab = default;
            definitions.m_OwnerData = GetComponentLookup<Owner>(true);
            definitions.m_TransformData = GetComponentLookup<Transform>(true);
            definitions.m_AttachedData = GetComponentLookup<Game.Objects.Attached>(true);
            definitions.m_LocalTransformCacheData = GetComponentLookup<LocalTransformCache>(true);
            definitions.m_ElevationData = GetComponentLookup<Game.Objects.Elevation>(true);
            definitions.m_BuildingData = GetComponentLookup<Game.Buildings.Building>(true);
            definitions.m_LotData = GetComponentLookup<Game.Buildings.Lot>(true);
            definitions.m_EdgeData = GetComponentLookup<Game.Net.Edge>(true);
            definitions.m_NodeData = GetComponentLookup<Game.Net.Node>(true);
            definitions.m_CurveData = GetComponentLookup<Game.Net.Curve>(true);
            definitions.m_NetElevationData = GetComponentLookup<Game.Net.Elevation>(true);
            definitions.m_OrphanData = GetComponentLookup<Game.Net.Orphan>(true);
            definitions.m_UpgradedData = GetComponentLookup<Game.Net.Upgraded>(true);
            definitions.m_CompositionData = GetComponentLookup<Game.Net.Composition>(true);
            definitions.m_AreaClearData = GetComponentLookup<Game.Areas.Clear>(true);
            definitions.m_AreaSpaceData = GetComponentLookup<Game.Areas.Space>(true);
            definitions.m_AreaLotData = GetComponentLookup<Game.Areas.Lot>(true);
            definitions.m_EditorContainerData = GetComponentLookup<Game.Tools.EditorContainer>(true);
            definitions.m_PrefabRefData = GetComponentLookup<PrefabRef>(true);
            definitions.m_PrefabNetObjectData = GetComponentLookup<NetObjectData>(true);
            definitions.m_PrefabBuildingData = GetComponentLookup<BuildingData>(true);
            definitions.m_PrefabAssetStampData = GetComponentLookup<AssetStampData>(true);
            definitions.m_PrefabBuildingExtensionData = GetComponentLookup<BuildingExtensionData>(true);
            definitions.m_PrefabSpawnableObjectData = GetComponentLookup<SpawnableObjectData>(true);
            definitions.m_PrefabObjectGeometryData = GetComponentLookup<ObjectGeometryData>(true);
            definitions.m_PrefabPlaceableObjectData = GetComponentLookup<PlaceableObjectData>(true);
            definitions.m_PrefabAreaGeometryData = GetComponentLookup<AreaGeometryData>(true);
            definitions.m_PrefabBuildingTerraformData = GetComponentLookup<BuildingTerraformData>(true);
            definitions.m_PrefabCreatureSpawnData = GetComponentLookup<CreatureSpawnData>(true);
            definitions.m_PlaceholderBuildingData = GetComponentLookup<PlaceholderBuildingData>(true);
            definitions.m_PrefabNetGeometryData = GetComponentLookup<NetGeometryData>(true);
            definitions.m_PrefabCompositionData = GetComponentLookup<NetCompositionData>(true);
            definitions.m_SubObjects = GetBufferLookup<Game.Objects.SubObject>(true);
            definitions.m_CachedNodes = GetBufferLookup<LocalNodeCache>(true);
            definitions.m_InstalledUpgrades = GetBufferLookup<Game.Buildings.InstalledUpgrade>(true);
            definitions.m_SubNets = GetBufferLookup<Game.Net.SubNet>(true);
            definitions.m_ConnectedEdges = GetBufferLookup<Game.Net.ConnectedEdge>(true);
            definitions.m_SubAreas = GetBufferLookup<Game.Areas.SubArea>(true);
            definitions.m_AreaNodes = GetBufferLookup<Game.Areas.Node>(true);
            definitions.m_AreaTriangles = GetBufferLookup<Game.Areas.Triangle>(true);
            definitions.m_PrefabSubObjects = GetBufferLookup<Game.Prefabs.SubObject>(true);
            definitions.m_PrefabSubNets = GetBufferLookup<Game.Prefabs.SubNet>(true);
            definitions.m_PrefabSubLanes = GetBufferLookup<Game.Prefabs.SubLane>(true);
            definitions.m_PrefabSubAreas = GetBufferLookup<Game.Prefabs.SubArea>(true);
            definitions.m_PrefabSubAreaNodes = GetBufferLookup<SubAreaNode>(true);
            definitions.m_PrefabPlaceholderElements = GetBufferLookup<PlaceholderObjectElement>(true);
            definitions.m_PrefabRequirementElements = GetBufferLookup<ObjectRequirementElement>(true);
            definitions.m_PrefabServiceUpgradeBuilding = GetBufferLookup<ServiceUpgradeBuilding>(true);
            definitions.m_WaterSurfaceData = m_WaterSystem.GetSurfaceData(out _);
            definitions.m_TerrainHeightData = m_TerrainSystem.GetHeightData();
            definitions.m_CommandBuffer = m_ToolOutputBarrier.CreateCommandBuffer();
            definitions.Execute();
        }
    }
}
