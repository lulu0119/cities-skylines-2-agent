using System;
using System.Collections.Generic;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;

namespace CS2MCP
{
    public sealed partial class RequestHandlers
    {
        private EntityQuery m_MilestoneLevelQuery;
        private bool m_MilestoneLevelQueryCreated;
        private EntityQuery m_MilestonePrefabQuery;
        private bool m_MilestonePrefabQueryCreated;
        private EntityQuery m_DevTreeNodeQuery;
        private bool m_DevTreeNodeQueryCreated;

        private EntityQuery MilestoneLevelQuery
        {
            get
            {
                if (!m_MilestoneLevelQueryCreated)
                {
                    m_MilestoneLevelQuery = EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<MilestoneLevel>());
                    m_MilestoneLevelQueryCreated = true;
                }
                return m_MilestoneLevelQuery;
            }
        }

        private EntityQuery MilestonePrefabQuery
        {
            get
            {
                if (!m_MilestonePrefabQueryCreated)
                {
                    m_MilestonePrefabQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
                    {
                        All = new[]
                        {
                            ComponentType.ReadOnly<PrefabData>(),
                            ComponentType.ReadOnly<MilestoneData>(),
                        },
                        None = new[]
                        {
                            ComponentType.ReadOnly<Deleted>(),
                            ComponentType.ReadOnly<Game.Tools.Temp>(),
                        },
                    });
                    m_MilestonePrefabQueryCreated = true;
                }
                return m_MilestonePrefabQuery;
            }
        }

        private EntityQuery DevTreeNodeQuery
        {
            get
            {
                if (!m_DevTreeNodeQueryCreated)
                {
                    m_DevTreeNodeQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
                    {
                        All = new[]
                        {
                            ComponentType.ReadOnly<PrefabData>(),
                            ComponentType.ReadOnly<DevTreeNodeData>(),
                        },
                        None = new[]
                        {
                            ComponentType.ReadOnly<Deleted>(),
                            ComponentType.ReadOnly<Game.Tools.Temp>(),
                        },
                    });
                    m_DevTreeNodeQueryCreated = true;
                }
                return m_DevTreeNodeQuery;
            }
        }

        private sealed class DevelopmentNodeSnapshot
        {
            public Entity Entity;
            public string Name;
            public string Service;
            public DevTreeNodeData Data;
            public bool Locked;
            public bool ServiceLocked;
            public bool PrerequisitesReady;
            public List<object> Prerequisites;
            public List<string> BlockedReasons;
        }

        private BridgeResponse GetProgression(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (MilestoneLevelQuery.IsEmptyIgnoreFilter)
            {
                return BridgeResponse.Error(BridgeErrorKind.Unavailable, "milestone state is not ready yet");
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            CitySystem citySystem = World.GetOrCreateSystemManaged<CitySystem>();
            DevTreeSystem devTreeSystem = World.GetOrCreateSystemManaged<DevTreeSystem>();
            int points = devTreeSystem.points;
            int achievedIndex = MilestoneLevelQuery.GetSingleton<MilestoneLevel>().m_AchievedMilestone;

            object achievedMilestone = null;
            object nextMilestone = null;
            int achievedXp = 0;
            int nextXp = 0;
            using (NativeArray<Entity> milestones = MilestonePrefabQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in milestones)
                {
                    MilestoneData data = EntityManager.GetComponentData<MilestoneData>(entity);
                    if (data.m_Index == achievedIndex)
                    {
                        achievedXp = data.m_XpRequried;
                        achievedMilestone = BuildMilestoneView(prefabSystem, entity, data);
                    }
                    else if (data.m_Index == achievedIndex + 1)
                    {
                        nextXp = data.m_XpRequried;
                        nextMilestone = BuildMilestoneView(prefabSystem, entity, data);
                    }
                }
            }

            List<DevelopmentNodeSnapshot> allNodes = ReadDevelopmentNodes(prefabSystem, points);
            request.Query.TryGetValue("service", out string requestedService);
            requestedService = requestedService?.Trim();
            if (!TryResolveDevelopmentService(
                    allNodes,
                    requestedService,
                    out string resolvedService,
                    out error))
            {
                return error;
            }

            var purchasedNodes = new List<string>();
            var lockedServices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var selectedNodes = new List<DevelopmentNodeSnapshot>();
            foreach (DevelopmentNodeSnapshot node in allNodes)
            {
                if (!node.Locked)
                {
                    purchasedNodes.Add(node.Name);
                }
                if (node.ServiceLocked && !string.IsNullOrWhiteSpace(node.Service))
                {
                    lockedServices.Add(node.Service);
                }

                bool selected = string.IsNullOrWhiteSpace(resolvedService)
                    ? node.Locked && !node.ServiceLocked && node.PrerequisitesReady
                    : string.Equals(node.Service, resolvedService, StringComparison.OrdinalIgnoreCase);
                if (selected)
                {
                    selectedNodes.Add(node);
                }
            }

            Dictionary<Entity, List<string>> unlocks = ReadNodeUnlocks(prefabSystem, selectedNodes);
            var nodeViews = new List<object>(selectedNodes.Count);
            foreach (DevelopmentNodeSnapshot node in selectedNodes)
            {
                unlocks.TryGetValue(node.Entity, out List<string> unlockedPrefabs);
                List<string> compactUnlocks = CompactNames(unlockedPrefabs, 24);
                nodeViews.Add(new
                {
                    name = node.Name,
                    service = node.Service,
                    cost = node.Data.m_Cost,
                    locked = node.Locked,
                    eligible = node.BlockedReasons.Count == 0,
                    blockedReasons = node.BlockedReasons,
                    prerequisites = node.Prerequisites,
                    unlocks = compactUnlocks,
                    unlockCount = unlockedPrefabs?.Count ?? 0,
                    unlocksTruncated = unlockedPrefabs != null && unlockedPrefabs.Count > compactUnlocks.Count,
                });
            }

            int totalXp = citySystem.XP;
            var lockedServiceNames = new List<string>(lockedServices);
            lockedServiceNames.Sort(StringComparer.OrdinalIgnoreCase);
            return BridgeResponse.Json(new
            {
                milestone = new
                {
                    achievedIndex,
                    achieved = achievedMilestone,
                    next = nextMilestone,
                    totalXp,
                    progressXp = Math.Max(0, totalXp - achievedXp),
                    requiredProgressXp = nextXp > achievedXp ? nextXp - achievedXp : 0,
                },
                developmentPoints = points,
                scope = string.IsNullOrWhiteSpace(resolvedService) ? "frontier" : "service",
                service = resolvedService,
                purchasedNodes,
                lockedServices = lockedServiceNames,
                nodes = nodeViews,
                counts = new
                {
                    totalNodes = allNodes.Count,
                    returnedNodes = nodeViews.Count,
                    purchasedNodes = purchasedNodes.Count,
                    eligibleNodes = selectedNodes.FindAll(node => node.BlockedReasons.Count == 0).Count,
                },
                note = "nodes defaults to the current unlocked frontier; pass service to inspect one full service tree, then purchase an eligible node by name with purchase_development_node",
            });
        }

        private static bool TryResolveDevelopmentService(
            List<DevelopmentNodeSnapshot> nodes,
            string requestedService,
            out string resolvedService,
            out BridgeResponse error)
        {
            resolvedService = null;
            error = null;
            if (string.IsNullOrWhiteSpace(requestedService))
            {
                return true;
            }

            List<string> services = GetDevelopmentServices(nodes);
            var partialMatches = new List<string>();
            foreach (string service in services)
            {
                if (string.Equals(service, requestedService, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedService = service;
                    return true;
                }
                if (service.IndexOf(requestedService, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    partialMatches.Add(service);
                }
            }
            if (partialMatches.Count == 1)
            {
                resolvedService = partialMatches[0];
                return true;
            }
            if (partialMatches.Count > 1)
            {
                error = BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"development service '{requestedService}' is ambiguous: {string.Join(", ", partialMatches)}");
                return false;
            }
            error = BridgeResponse.Error(BridgeErrorKind.NotFound,
                $"unknown development service '{requestedService}'; available services: {string.Join(", ", services)}");
            return false;
        }

        private static List<string> GetDevelopmentServices(List<DevelopmentNodeSnapshot> nodes)
        {
            var services = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DevelopmentNodeSnapshot node in nodes)
            {
                if (!string.IsNullOrWhiteSpace(node.Service))
                {
                    services.Add(node.Service);
                }
            }
            var result = new List<string>(services);
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static List<string> CompactNames(List<string> names, int limit)
        {
            if (names == null || names.Count == 0)
            {
                return new List<string>();
            }
            return names.GetRange(0, Math.Min(limit, names.Count));
        }

        private BridgeResponse PurchaseDevelopmentNode(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.Query.TryGetValue("name", out string requestedName)
                || string.IsNullOrWhiteSpace(requestedName))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments, "provide ?name=<development node name from get_progression>");
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            if (!TryFindDevelopmentNode(prefabSystem, requestedName, out Entity node, out string nodeName, out error))
            {
                return error;
            }

            DevTreeSystem devTreeSystem = World.GetOrCreateSystemManaged<DevTreeSystem>();
            DevTreeNodeData data = EntityManager.GetComponentData<DevTreeNodeData>(node);
            DevelopmentNodeSnapshot snapshot = BuildDevelopmentNodeSnapshot(
                prefabSystem,
                node,
                nodeName,
                data,
                devTreeSystem.points);
            if (snapshot.BlockedReasons.Count > 0)
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"development node '{nodeName}' is not eligible: {string.Join("; ", snapshot.BlockedReasons)}");
            }

            int pointsBefore = devTreeSystem.points;
            devTreeSystem.Purchase(node);
            int pointsAfter = devTreeSystem.points;
            if (data.m_Cost > 0 && pointsAfter != pointsBefore - data.m_Cost)
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"the game rejected development node '{nodeName}'; refresh get_progression and retry only if it is still eligible");
            }
            return BridgeResponse.Json(new
            {
                purchased = true,
                node = nodeName,
                service = snapshot.Service,
                cost = data.m_Cost,
                pointsBefore,
                pointsAfter,
                note = "purchase accepted through DevTreeSystem; the unlock event applies at end of frame and persists with the save",
            });
        }

        private object BuildMilestoneView(PrefabSystem prefabSystem, Entity entity, MilestoneData data)
        {
            return new
            {
                name = GetProgressionPrefabName(prefabSystem, entity),
                index = data.m_Index,
                totalXpRequired = data.m_XpRequried,
                moneyReward = data.m_Reward,
                developmentPoints = data.m_DevTreePoints,
                mapTiles = data.m_MapTiles,
                loanLimit = data.m_LoanLimit,
                major = data.m_Major,
            };
        }

        private List<DevelopmentNodeSnapshot> ReadDevelopmentNodes(PrefabSystem prefabSystem, int points)
        {
            var nodes = new List<DevelopmentNodeSnapshot>();
            using (NativeArray<Entity> entities = DevTreeNodeQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    string name = GetProgressionPrefabName(prefabSystem, entity);
                    nodes.Add(BuildDevelopmentNodeSnapshot(
                        prefabSystem,
                        entity,
                        name,
                        EntityManager.GetComponentData<DevTreeNodeData>(entity),
                        points));
                }
            }
            nodes.Sort((left, right) =>
            {
                int serviceOrder = string.Compare(left.Service, right.Service, StringComparison.OrdinalIgnoreCase);
                return serviceOrder != 0
                    ? serviceOrder
                    : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            });
            return nodes;
        }

        private DevelopmentNodeSnapshot BuildDevelopmentNodeSnapshot(
            PrefabSystem prefabSystem,
            Entity entity,
            string name,
            DevTreeNodeData data,
            int points)
        {
            bool locked = IsLocked(entity);
            bool serviceLocked = data.m_Service != Entity.Null && IsLocked(data.m_Service);
            string serviceName = data.m_Service != Entity.Null
                ? GetProgressionPrefabName(prefabSystem, data.m_Service)
                : null;
            var prerequisites = new List<object>();
            bool hasPrerequisite = false;
            bool hasUnlockedPrerequisite = false;
            if (EntityManager.HasBuffer<DevTreeNodeRequirement>(entity))
            {
                DynamicBuffer<DevTreeNodeRequirement> requirements =
                    EntityManager.GetBuffer<DevTreeNodeRequirement>(entity, isReadOnly: true);
                foreach (DevTreeNodeRequirement requirement in requirements)
                {
                    if (requirement.m_Node == Entity.Null)
                    {
                        continue;
                    }
                    hasPrerequisite = true;
                    bool requirementLocked = IsLocked(requirement.m_Node);
                    hasUnlockedPrerequisite |= !requirementLocked;
                    prerequisites.Add(new
                    {
                        name = GetProgressionPrefabName(prefabSystem, requirement.m_Node),
                        locked = requirementLocked,
                    });
                }
            }

            var blockedReasons = new List<string>();
            if (!locked)
            {
                blockedReasons.Add("already purchased");
            }
            if (data.m_Cost > points)
            {
                blockedReasons.Add($"needs {data.m_Cost} points; {points} available");
            }
            if (serviceLocked)
            {
                blockedReasons.Add($"service '{serviceName}' is still locked");
            }
            // Match DevTreeSystem.Purchase: nodes with multiple incoming branches
            // become eligible when any listed prerequisite has been purchased.
            if (hasPrerequisite && !hasUnlockedPrerequisite)
            {
                blockedReasons.Add("requires a prerequisite node");
            }

            return new DevelopmentNodeSnapshot
            {
                Entity = entity,
                Name = name,
                Service = serviceName,
                Data = data,
                Locked = locked,
                ServiceLocked = serviceLocked,
                PrerequisitesReady = !hasPrerequisite || hasUnlockedPrerequisite,
                Prerequisites = prerequisites,
                BlockedReasons = blockedReasons,
            };
        }

        private Dictionary<Entity, List<string>> ReadNodeUnlocks(
            PrefabSystem prefabSystem,
            List<DevelopmentNodeSnapshot> nodes)
        {
            var result = new Dictionary<Entity, List<string>>();
            foreach (DevelopmentNodeSnapshot node in nodes)
            {
                result[node.Entity] = new List<string>();
            }

            using (EntityQuery query = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PrefabData>(),
                ComponentType.ReadOnly<UnlockRequirement>()))
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    DynamicBuffer<UnlockRequirement> requirements =
                        EntityManager.GetBuffer<UnlockRequirement>(entity, isReadOnly: true);
                    foreach (UnlockRequirement requirement in requirements)
                    {
                        if (entity != requirement.m_Prefab
                            && result.TryGetValue(requirement.m_Prefab, out List<string> unlocked))
                        {
                            string name = GetProgressionPrefabName(prefabSystem, entity);
                            if (!string.IsNullOrWhiteSpace(name) && !unlocked.Contains(name))
                            {
                                unlocked.Add(name);
                            }
                        }
                    }
                }
            }
            foreach (List<string> names in result.Values)
            {
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            return result;
        }

        private bool TryFindDevelopmentNode(
            PrefabSystem prefabSystem,
            string requestedName,
            out Entity node,
            out string resolvedName,
            out BridgeResponse error)
        {
            node = Entity.Null;
            resolvedName = null;
            error = null;
            var partialMatches = new List<KeyValuePair<Entity, string>>();
            using (NativeArray<Entity> entities = DevTreeNodeQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    string name = GetProgressionPrefabName(prefabSystem, entity);
                    if (string.Equals(name, requestedName, StringComparison.OrdinalIgnoreCase))
                    {
                        node = entity;
                        resolvedName = name;
                        return true;
                    }
                    if (!string.IsNullOrWhiteSpace(name)
                        && name.IndexOf(requestedName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        partialMatches.Add(new KeyValuePair<Entity, string>(entity, name));
                    }
                }
            }
            if (partialMatches.Count == 1)
            {
                node = partialMatches[0].Key;
                resolvedName = partialMatches[0].Value;
                return true;
            }
            if (partialMatches.Count > 1)
            {
                var names = new List<string>();
                foreach (KeyValuePair<Entity, string> match in partialMatches)
                {
                    names.Add(match.Value);
                }
                error = BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"development node name '{requestedName}' is ambiguous: {string.Join(", ", names)}");
                return false;
            }
            error = BridgeResponse.Error(BridgeErrorKind.NotFound,
                $"unknown development node '{requestedName}'; refresh get_progression for valid names");
            return false;
        }

        private string GetProgressionPrefabName(PrefabSystem prefabSystem, Entity entity)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
            {
                return null;
            }
            PrefabBase prefab = prefabSystem.GetPrefab<PrefabBase>(entity);
            return prefab != null ? prefab.name : entity.ToString();
        }
    }
}
