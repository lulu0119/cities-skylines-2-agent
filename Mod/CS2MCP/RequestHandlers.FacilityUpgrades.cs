using System;
using System.Collections.Generic;
using Game.Buildings;
using Game.Common;
using Game.Objects;
using Game.Prefabs;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;

namespace CS2MCP
{
    /// <summary>
    /// Facility-upgrade endpoints. Listing reads BuildingUpgradeElement and
    /// InstalledUpgrade. Writes install through ObjectToolBaseSystem.CreateDefinitions
    /// (owner = parent building, CreationFlags.Upgrade) and toggle Out of Service
    /// through PoliciesUISystem — the same paths the info panel uses.
    /// </summary>
    public sealed partial class RequestHandlers
    {
        private const string OutOfServicePolicyName = "Out of Service";

        private EntityQuery m_ServiceUpgradePrefabQuery;
        private bool m_ServiceUpgradePrefabQueryCreated;
        private EntityQuery m_BuildingOptionPolicyQuery;
        private bool m_BuildingOptionPolicyQueryCreated;

        private EntityQuery ServiceUpgradePrefabQuery
        {
            get
            {
                if (!m_ServiceUpgradePrefabQueryCreated)
                {
                    m_ServiceUpgradePrefabQuery = EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<PrefabData>(),
                        ComponentType.ReadOnly<ServiceUpgradeData>());
                    m_ServiceUpgradePrefabQueryCreated = true;
                }
                return m_ServiceUpgradePrefabQuery;
            }
        }

        private EntityQuery BuildingOptionPolicyQuery
        {
            get
            {
                if (!m_BuildingOptionPolicyQueryCreated)
                {
                    m_BuildingOptionPolicyQuery = EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<PrefabData>(),
                        ComponentType.ReadOnly<PolicyData>(),
                        ComponentType.ReadOnly<BuildingOptionData>());
                    m_BuildingOptionPolicyQueryCreated = true;
                }
                return m_BuildingOptionPolicyQuery;
            }
        }

        private BridgeResponse ListFacilityUpgrades(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!TryResolveUpgradeTarget(request, out Entity building, out BridgeResponse targetError))
            {
                return targetError;
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            Entity buildingPrefab = EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
            List<Entity> available = CollectAvailableUpgradePrefabs(buildingPrefab);
            Dictionary<Entity, InstalledUpgradeState> installed = ReadInstalledUpgrades(building);

            var upgrades = new List<object>(available.Count);
            var seen = new HashSet<Entity>();
            foreach (Entity upgradePrefab in available)
            {
                seen.Add(upgradePrefab);
                upgrades.Add(BuildUpgradeView(prefabSystem, upgradePrefab, installed));
            }
            foreach (KeyValuePair<Entity, InstalledUpgradeState> pair in installed)
            {
                if (seen.Add(pair.Value.Prefab))
                {
                    upgrades.Add(BuildUpgradeView(prefabSystem, pair.Value.Prefab, installed));
                }
            }

            return BridgeResponse.Json(new
            {
                building = GetEntityPrefabName(prefabSystem, building),
                entity = new { index = building.Index, version = building.Version },
                upgradeCount = upgrades.Count,
                upgrades,
                note = upgrades.Count == 0
                    ? "this building has no facility upgrades"
                    : "read-only snapshot; set_facility_upgrade installs a missing upgrade or toggles Out of Service on an installed one",
            });
        }

        private BridgeResponse SetFacilityUpgrade(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!TryResolveUpgradeTarget(request, out Entity building, out BridgeResponse targetError))
            {
                return targetError;
            }
            if (!request.Query.TryGetValue("upgrade", out string upgradeName)
                || string.IsNullOrWhiteSpace(upgradeName))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "provide ?upgrade=<prefab name from list_facility_upgrades>");
            }
            if (!request.TryGetBool("enabled", out bool enabled))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "provide ?enabled=true|false");
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            Entity buildingPrefab = EntityManager.GetComponentData<PrefabRef>(building).m_Prefab;
            if (!TryFindPrefabByName(
                    ServiceUpgradePrefabQuery,
                    upgradeName,
                    out Entity upgradePrefab,
                    out PrefabBase upgradeBase))
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound,
                    $"unknown facility upgrade '{upgradeName}'; list names via list_facility_upgrades");
            }
            if (!IsFacilityObjectUpgrade(upgradePrefab))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"prefab '{upgradeBase.name}' is not a placeable facility upgrade; network replacements cannot use this tool");
            }
            if (!UpgradeBelongsToBuilding(buildingPrefab, upgradePrefab))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"upgrade '{upgradeBase.name}' does not belong to this building");
            }

            Dictionary<Entity, InstalledUpgradeState> installed = ReadInstalledUpgrades(building);
            if (installed.TryGetValue(upgradePrefab, out InstalledUpgradeState current))
            {
                if (current.Enabled == enabled)
                {
                    return BridgeResponse.Json(new
                    {
                        applied = true,
                        upgrade = upgradeBase.name,
                        enabled,
                        installed = true,
                        building = GetEntityPrefabName(prefabSystem, building),
                        entity = new { index = building.Index, version = building.Version },
                        note = enabled
                            ? "upgrade is already installed and in service"
                            : "upgrade is already out of service",
                    });
                }
                if (!TrySetUpgradeOutOfService(current.Entity, outOfService: !enabled, out BridgeResponse policyError))
                {
                    return policyError;
                }
                return BridgeResponse.Json(new
                {
                    applied = true,
                    upgrade = upgradeBase.name,
                    enabled,
                    installed = true,
                    building = GetEntityPrefabName(prefabSystem, building),
                    entity = new { index = building.Index, version = building.Version },
                    note = "toggled Out of Service through PoliciesUISystem, the same path as the building info panel",
                });
            }

            if (!enabled)
            {
                return BridgeResponse.Json(new
                {
                    applied = true,
                    upgrade = upgradeBase.name,
                    enabled = false,
                    installed = false,
                    building = GetEntityPrefabName(prefabSystem, building),
                    entity = new { index = building.Index, version = building.Version },
                    note = "upgrade is not installed",
                });
            }
            if (!EntityManager.HasBuffer<InstalledUpgrade>(building))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    "this building has no InstalledUpgrade buffer, so the native object-upgrade path cannot attach an upgrade");
            }
            if (IsLocked(upgradePrefab))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"upgrade '{upgradeBase.name}' is locked (milestone not reached)");
            }
            ResolveUpgradePose(building, upgradePrefab, out float3 position, out quaternion rotation);

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueFacilityUpgrade(
                    building,
                    upgradePrefab,
                    upgradeBase,
                    position,
                    rotation,
                    request))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private bool TryResolveUpgradeTarget(
            BridgeRequest request,
            out Entity building,
            out BridgeResponse error)
        {
            building = Entity.Null;
            error = null;
            if (!request.TryGetInt("index", out int index)
                || !request.TryGetInt("version", out int version))
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "provide ?index=&version= of a building from list_buildings");
                return false;
            }
            if (!TryResolveExistingEntity(index, version, out Entity entity))
            {
                error = BridgeResponse.Error(BridgeErrorKind.NotFound,
                    $"entity {index}:{version} does not exist (stale id?)");
                return false;
            }

            building = GetUpgradableBuilding(entity);
            if (building == Entity.Null
                || !EntityManager.Exists(building)
                || !EntityManager.HasComponent<Building>(building)
                || !EntityManager.HasComponent<PrefabRef>(building)
                || !EntityManager.HasComponent<Transform>(building))
            {
                error = BridgeResponse.Error(BridgeErrorKind.NotFound,
                    $"entity {index}:{version} is not an existing building");
                return false;
            }
            return true;
        }

        private Entity GetUpgradableBuilding(Entity entity)
        {
            if (EntityManager.HasComponent<Game.Objects.Attached>(entity))
            {
                return EntityManager.GetComponentData<Game.Objects.Attached>(entity).m_Parent;
            }
            if (EntityManager.HasComponent<Game.Buildings.ServiceUpgrade>(entity)
                && EntityManager.HasComponent<Owner>(entity))
            {
                return EntityManager.GetComponentData<Owner>(entity).m_Owner;
            }
            return entity;
        }

        private List<Entity> CollectAvailableUpgradePrefabs(Entity buildingPrefab)
        {
            var upgrades = new List<Entity>();
            if (EntityManager.HasBuffer<BuildingUpgradeElement>(buildingPrefab))
            {
                DynamicBuffer<BuildingUpgradeElement> elements =
                    EntityManager.GetBuffer<BuildingUpgradeElement>(buildingPrefab, isReadOnly: true);
                for (int i = 0; i < elements.Length; i++)
                {
                    Entity upgrade = elements[i].m_Upgrade;
                    if (IsListedFacilityUpgrade(upgrade))
                    {
                        upgrades.Add(upgrade);
                    }
                }
                return upgrades;
            }

            using (NativeArray<Entity> entities = ServiceUpgradePrefabQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity upgrade in entities)
                {
                    if (IsListedFacilityUpgrade(upgrade)
                        && UpgradeBelongsToBuilding(buildingPrefab, upgrade))
                    {
                        upgrades.Add(upgrade);
                    }
                }
            }
            return upgrades;
        }

        private bool IsListedFacilityUpgrade(Entity upgradePrefab)
        {
            return EntityManager.Exists(upgradePrefab)
                && EntityManager.HasComponent<ServiceUpgradeData>(upgradePrefab)
                && EntityManager.HasComponent<UIObjectData>(upgradePrefab)
                && IsFacilityObjectUpgrade(upgradePrefab);
        }

        private bool IsFacilityObjectUpgrade(Entity upgradePrefab)
        {
            if (!EntityManager.HasComponent<ServiceUpgradeData>(upgradePrefab)
                || !EntityManager.HasComponent<PlaceableObjectData>(upgradePrefab))
            {
                return false;
            }
            if (EntityManager.HasComponent<NetGeometryData>(upgradePrefab)
                && !EntityManager.HasComponent<BuildingData>(upgradePrefab)
                && !EntityManager.HasComponent<BuildingExtensionData>(upgradePrefab))
            {
                return false;
            }
            return EntityManager.HasComponent<BuildingData>(upgradePrefab)
                || EntityManager.HasComponent<BuildingExtensionData>(upgradePrefab);
        }

        private bool UpgradeBelongsToBuilding(Entity buildingPrefab, Entity upgradePrefab)
        {
            if (EntityManager.HasBuffer<BuildingUpgradeElement>(buildingPrefab))
            {
                DynamicBuffer<BuildingUpgradeElement> elements =
                    EntityManager.GetBuffer<BuildingUpgradeElement>(buildingPrefab, isReadOnly: true);
                for (int i = 0; i < elements.Length; i++)
                {
                    if (elements[i].m_Upgrade == upgradePrefab)
                    {
                        return true;
                    }
                }
            }
            if (!EntityManager.HasBuffer<ServiceUpgradeBuilding>(upgradePrefab))
            {
                return false;
            }
            DynamicBuffer<ServiceUpgradeBuilding> parents =
                EntityManager.GetBuffer<ServiceUpgradeBuilding>(upgradePrefab, isReadOnly: true);
            for (int i = 0; i < parents.Length; i++)
            {
                if (parents[i].m_Building == buildingPrefab)
                {
                    return true;
                }
            }
            return false;
        }

        private Dictionary<Entity, InstalledUpgradeState> ReadInstalledUpgrades(Entity building)
        {
            var installed = new Dictionary<Entity, InstalledUpgradeState>();
            if (!EntityManager.HasBuffer<InstalledUpgrade>(building))
            {
                return installed;
            }
            DynamicBuffer<InstalledUpgrade> buffer =
                EntityManager.GetBuffer<InstalledUpgrade>(building, isReadOnly: true);
            for (int i = 0; i < buffer.Length; i++)
            {
                InstalledUpgrade entry = buffer[i];
                Entity upgrade = entry.m_Upgrade;
                if (upgrade == Entity.Null
                    || !EntityManager.Exists(upgrade)
                    || !EntityManager.HasComponent<PrefabRef>(upgrade))
                {
                    continue;
                }
                Entity prefab = EntityManager.GetComponentData<PrefabRef>(upgrade).m_Prefab;
                installed[prefab] = new InstalledUpgradeState
                {
                    Entity = upgrade,
                    Prefab = prefab,
                    Enabled = !IsUpgradeDisabled(upgrade, entry),
                };
            }
            return installed;
        }

        private bool IsUpgradeDisabled(Entity upgrade, InstalledUpgrade entry)
        {
            if (BuildingUtils.CheckOption(entry, BuildingOption.Inactive))
            {
                return true;
            }
            if (EntityManager.HasComponent<Extension>(upgrade))
            {
                Extension extension = EntityManager.GetComponentData<Extension>(upgrade);
                if ((extension.m_Flags & ExtensionFlags.Disabled) != 0)
                {
                    return true;
                }
            }
            if (EntityManager.HasComponent<Building>(upgrade))
            {
                Building building = EntityManager.GetComponentData<Building>(upgrade);
                if (BuildingUtils.CheckOption(building, BuildingOption.Inactive))
                {
                    return true;
                }
            }
            return false;
        }

        private object BuildUpgradeView(
            PrefabSystem prefabSystem,
            Entity upgradePrefab,
            Dictionary<Entity, InstalledUpgradeState> installed)
        {
            PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(upgradePrefab);
            string name = prefab != null ? prefab.name : null;
            ServiceUpgradeData data = EntityManager.HasComponent<ServiceUpgradeData>(upgradePrefab)
                ? EntityManager.GetComponentData<ServiceUpgradeData>(upgradePrefab)
                : default;
            bool isInstalled = installed.TryGetValue(upgradePrefab, out InstalledUpgradeState state);
            object installedEntity = null;
            if (isInstalled)
            {
                installedEntity = new { index = state.Entity.Index, version = state.Entity.Version };
            }
            return new
            {
                name,
                installed = isInstalled,
                enabled = isInstalled && state.Enabled,
                locked = IsLocked(upgradePrefab),
                cost = data.m_UpgradeCost,
                forbidMultiple = data.m_ForbidMultiple
                    || EntityManager.HasComponent<BuildingExtensionData>(upgradePrefab),
                kind = EntityManager.HasComponent<BuildingExtensionData>(upgradePrefab)
                    ? "extension"
                    : "sub-building",
                installedEntity,
            };
        }

        private bool TrySetUpgradeOutOfService(Entity upgrade, bool outOfService, out BridgeResponse error)
        {
            error = null;
            if (!TryFindOutOfServicePolicy(out Entity policy, out string failure))
            {
                error = BridgeResponse.Error(BridgeErrorKind.Unavailable, failure);
                return false;
            }
            World.GetOrCreateSystemManaged<PoliciesUISystem>()
                .SetSelectedInfoPolicy(upgrade, policy, outOfService);
            return true;
        }

        private bool TryFindOutOfServicePolicy(out Entity policy, out string failure)
        {
            policy = Entity.Null;
            failure = null;
            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            using (NativeArray<Entity> entities = BuildingOptionPolicyQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PolicyPrefab prefab = prefabSystem.GetPrefab<PolicyPrefab>(entity);
                    if (prefab == null
                        || !string.Equals(prefab.name, OutOfServicePolicyName, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    BuildingOptionData options = EntityManager.GetComponentData<BuildingOptionData>(entity);
                    if (!BuildingUtils.HasOption(options, BuildingOption.Inactive))
                    {
                        continue;
                    }
                    policy = entity;
                    return true;
                }
            }
            failure = "native Out of Service policy was not found; refusing to fake an upgrade toggle";
            return false;
        }

        private void ResolveUpgradePose(
            Entity building,
            Entity upgradePrefab,
            out float3 position,
            out quaternion rotation)
        {
            Transform parent = EntityManager.GetComponentData<Transform>(building);
            rotation = parent.m_Rotation;
            if (EntityManager.HasComponent<BuildingExtensionData>(upgradePrefab))
            {
                BuildingExtensionData extension =
                    EntityManager.GetComponentData<BuildingExtensionData>(upgradePrefab);
                position = ObjectUtils.LocalToWorld(parent, extension.m_Position);
                return;
            }
            if (EntityManager.HasComponent<BuildingData>(upgradePrefab)
                && EntityManager.HasComponent<PrefabRef>(building)
                && EntityManager.HasComponent<BuildingData>(
                    EntityManager.GetComponentData<PrefabRef>(building).m_Prefab))
            {
                BuildingData parentLot = EntityManager.GetComponentData<BuildingData>(
                    EntityManager.GetComponentData<PrefabRef>(building).m_Prefab);
                BuildingData upgradeLot = EntityManager.GetComponentData<BuildingData>(upgradePrefab);
                position = BuildingUtils.CalculateFrontPosition(
                    parent,
                    parentLot.m_LotSize.y + upgradeLot.m_LotSize.y);
                return;
            }
            position = parent.m_Position;
        }

        private struct InstalledUpgradeState
        {
            public Entity Entity;
            public Entity Prefab;
            public bool Enabled;
        }
    }
}
