using System;
using System.Collections.Generic;
using Game.Areas;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Game.UI.Menu;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace CS2MCP
{
    /// <summary>
    /// Meta / time endpoints: short real-time simulation waits with state
    /// restore, triggering saves (AI safety net), and map tile info.
    /// </summary>
    public sealed partial class RequestHandlers
    {
        // SimulationSystem.frameIndex ticks 262144 times per 24 in-game hours,
        // so one in-game hour is exactly 262144 / 24 frames.
        private const float kFramesPerGameHour = 262144f / 24f;

        private EntityQuery m_MapTileQuery;
        private bool m_MapTileQueryCreated;

        private EntityQuery MapTileQuery
        {
            get
            {
                if (!m_MapTileQueryCreated)
                {
                    m_MapTileQuery = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<MapTile>());
                    m_MapTileQueryCreated = true;
                }
                return m_MapTileQuery;
            }
        }

        private BridgeResponse SimWait(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (m_System.AutoPauseTargetFrame != 0)
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "a timed simulation wait is already active; wait for it to finish first");
            }
            if (!request.TryGetFloat("hours", out float hours))
            {
                hours = 1f;
            }
            hours = math.clamp(hours, 1f, 24f);

            SimulationSystem sim = World.GetOrCreateSystemManaged<SimulationSystem>();
            float restoreSpeed = sim.selectedSpeed;
            // One wait advances exactly the requested number of in-game hours.
            // The run speed only controls how long that takes in real time;
            // it does not change how much game time passes.
            const float speed = 8f;
            uint targetFrame = sim.frameIndex +
                (uint)Math.Ceiling(hours * kFramesPerGameHour);
            sim.selectedSpeed = speed;
            m_System.StartTimedRun(targetFrame, restoreSpeed, speed);

            return BridgeResponse.Json(new
            {
                running = true,
                hours,
                speed,
                restoreSpeed,
                startFrame = sim.frameIndex,
                targetFrame,
                note = "simulation runs until exactly the requested in-game hours have passed (default 1 game hour), then the previous speed/pause state is restored",
            });
        }

        private BridgeResponse SaveGame(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            string name = request.Query.TryGetValue("name", out string rawName) && !string.IsNullOrEmpty(rawName)
                ? rawName
                : $"CS2MCP {DateTime.Now:yyyy-MM-dd HH-mm-ss}";

            MenuUISystem menu = World.GetExistingSystemManaged<MenuUISystem>();
            if (menu == null)
            {
                return BridgeResponse.Error(BridgeErrorKind.Unavailable, "menu system unavailable");
            }
            var saveInfo = menu.GetSaveInfo(autoSave: false);
            // The save pipeline requires a preview texture (null crashes it) —
            // capture one exactly like AutoSaveSystem does.
            UnityEngine.RenderTexture preview = Game.UI.ScreenCaptureHelper.CreateRenderTarget("PreviewSaveGame-CS2MCP", 680, 383);
            Game.UI.ScreenCaptureHelper.CaptureScreenshot(UnityEngine.Camera.main, preview, new MenuHelpers.SaveGamePreviewSettings());
            _ = GameManager.instance.Save(name, saveInfo, Colossal.IO.AssetDatabase.AssetDatabase.user, preview);

            return BridgeResponse.Json(new
            {
                saving = true,
                name,
                note = "save runs asynchronously; it appears in the game's load menu when finished",
            });
        }

        private BridgeResponse GetTilesInfo(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            request.Query.TryGetValue("filter", out string filterRaw);
            string filter = string.IsNullOrWhiteSpace(filterRaw)
                ? "owned"
                : filterRaw.Trim().ToLowerInvariant();
            if (filter != "all" && filter != "owned" && filter != "unowned" && filter != "available")
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "filter must be 'owned' (default), 'all', 'unowned' or 'available'");
            }
            MapTilePurchaseSystem tiles = World.GetOrCreateSystemManaged<MapTilePurchaseSystem>();
            int availableTileSlots = tiles.GetAvailableTiles();
            int total = MapTileQuery.CalculateEntityCount();
            int owned = 0;
            const float kMapHalfSize = 7168f;
            const float kMapTileSize = 623.304347826f;
            var tileList = new List<object>();
            using (NativeArray<Entity> entities = MapTileQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    bool isOwned = !EntityManager.HasComponent<Game.Common.Native>(entity);
                    if (isOwned)
                    {
                        owned++;
                    }
                    bool canAttemptPurchase = !isOwned && availableTileSlots > 0;
                    if ((filter == "owned" && !isOwned) ||
                        (filter == "unowned" && isOwned) ||
                        (filter == "available" && !canAttemptPurchase))
                    {
                        continue;
                    }
                    Geometry geometry = EntityManager.GetComponentData<Geometry>(entity);
                    float x = geometry.m_CenterPosition.x;
                    float z = geometry.m_CenterPosition.z;
                    int gridX = (int)Math.Floor((x + kMapHalfSize) / kMapTileSize);
                    int gridZ = (int)Math.Floor((z + kMapHalfSize) / kMapTileSize);
                    tileList.Add(new
                    {
                        entity = new { index = entity.Index, version = entity.Version },
                        grid = new { x = gridX, z = gridZ },
                        center = new { x = (float)Math.Round(x, 1), z = (float)Math.Round(z, 1) },
                        owned = isOwned,
                        available = canAttemptPurchase,
                    });
                }
            }
            return BridgeResponse.Json(new
            {
                filter,
                totalTiles = total,
                ownedTiles = owned,
                availableToPurchase = availableTileSlots,
                upkeepEnabled = tiles.GetMapTileUpkeepEnabled(),
                upkeepCostMultiplier = tiles.GetMapTileUpkeepCostMultiplier(owned),
                note = "tiles are 623m squares on a 23x23 grid; default filter=owned (no item cap); pass filter=all explicitly for every tile; available means a purchase permit exists, while the game still validates tile eligibility and funds",
                tiles = tileList,
            });
        }

        private BridgeResponse BuyTiles(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            Entity tile = Entity.Null;
            if (request.TryGetInt("index", out int index))
            {
                using (NativeArray<Entity> entities = MapTileQuery.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity entity in entities)
                    {
                        if (entity.Index == index)
                        {
                            tile = entity;
                            break;
                        }
                    }
                }
                if (tile == Entity.Null)
                {
                    return BridgeResponse.Error(BridgeErrorKind.NotFound, $"map tile entity {index} not found; list via /city/tiles");
                }
            }
            else if (request.TryGetInt("gridX", out int gridX) && request.TryGetInt("gridZ", out int gridZ))
            {
                const float kMapHalfSize = 7168f;
                const float kMapTileSize = 623.304347826f;
                using (NativeArray<Entity> entities = MapTileQuery.ToEntityArray(Allocator.Temp))
                {
                    foreach (Entity entity in entities)
                    {
                        Geometry geometry = EntityManager.GetComponentData<Geometry>(entity);
                        int x = (int)Math.Floor((geometry.m_CenterPosition.x + kMapHalfSize) / kMapTileSize);
                        int z = (int)Math.Floor((geometry.m_CenterPosition.z + kMapHalfSize) / kMapTileSize);
                        if (x == gridX && z == gridZ)
                        {
                            tile = entity;
                            break;
                        }
                    }
                }
                if (tile == Entity.Null)
                {
                    return BridgeResponse.Error(BridgeErrorKind.NotFound, $"map tile at grid ({gridX},{gridZ}) not found");
                }
            }
            else
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?index=<tile entity index> (from /city/tiles) or ?gridX=&gridZ=");
            }

            if (!EntityManager.HasComponent<Game.Common.Native>(tile))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict, "this map tile is already owned");
            }

            MapTilePurchaseSystem purchase = World.GetOrCreateSystemManaged<MapTilePurchaseSystem>();

            // Feed the game's purchase pipeline the same way the map-tiles UI
            // does: a selection entity holding the tile, then PurchaseSelection
            // validates permits/funds, charges the price and unlocks the tile.
            Entity selectionEntity = Entity.Null;
            bool wasSelecting = purchase.selecting;
            TilePurchaseErrorFlags status;
            int cost;
            try
            {
                selectionEntity = EntityManager.CreateEntity(
                    ComponentType.ReadWrite<SelectionInfo>(),
                    ComponentType.ReadWrite<SelectionElement>());
                DynamicBuffer<SelectionElement> selection =
                    EntityManager.GetBuffer<SelectionElement>(selectionEntity);
                selection.Add(new SelectionElement { m_Entity = tile });

                purchase.selecting = true;
                purchase.PurchaseSelection();
                status = purchase.status;
                cost = purchase.cost;
            }
            finally
            {
                purchase.selecting = wasSelecting;
                if (selectionEntity != Entity.Null && EntityManager.Exists(selectionEntity))
                {
                    EntityManager.DestroyEntity(selectionEntity);
                }
            }

            if (status != TilePurchaseErrorFlags.None)
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"purchase blocked by the game: {status}; estimated cost {cost} (check money/permits)");
            }

            return BridgeResponse.Json(new
            {
                purchased = true,
                cost,
                tile = new { index = tile.Index, version = tile.Version },
                note = "map tile unlocked; build roads/zone on it now",
            });
        }
    }
}
