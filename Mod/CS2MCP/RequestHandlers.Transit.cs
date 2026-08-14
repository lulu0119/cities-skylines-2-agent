using System;
using System.Collections.Generic;
using System.Globalization;
using Game;
using Game.Common;
using Game.Prefabs;
using Game.Routes;
using Game.Tools;
using Game.UI.InGame;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Transform = Game.Objects.Transform;
using TransportStop = Game.Routes.TransportStop;

namespace CS2MCP
{
    /// <summary>
    /// Transit-stop listing and passenger-line create/delete. Listing reads
    /// TransportStop / TransportLine ECS. Create submits CreationDefinition plus
    /// WaypointDefinition through BridgeToolSystem so GenerateRoutesSystem and
    /// ApplyRoutesSystem own native validation. Delete adds Deleted through
    /// EndFrameBarrier — the same path TransportationOverviewUISystem uses.
    /// Stops are not a place_building role.
    /// </summary>
    public sealed partial class RequestHandlers
    {
        private const int TransitListDefaultLimit = 16;
        private const int TransitListHardMax = 64;
        private const int TransitLineMaxStops = 16;

        private EntityQuery m_TransitStopQuery;
        private bool m_TransitStopQueryCreated;
        private EntityQuery m_TransitLineQuery;
        private bool m_TransitLineQueryCreated;
        private EntityQuery m_TransportLinePrefabQuery;
        private bool m_TransportLinePrefabQueryCreated;

        private EntityQuery TransitStopQuery
        {
            get
            {
                if (!m_TransitStopQueryCreated)
                {
                    m_TransitStopQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
                    {
                        All = new[]
                        {
                            ComponentType.ReadOnly<TransportStop>(),
                            ComponentType.ReadOnly<Transform>(),
                            ComponentType.ReadOnly<PrefabRef>(),
                        },
                        None = new[]
                        {
                            ComponentType.ReadOnly<Temp>(),
                            ComponentType.ReadOnly<Deleted>(),
                        },
                    });
                    m_TransitStopQueryCreated = true;
                }
                return m_TransitStopQuery;
            }
        }

        private EntityQuery TransitLineQuery
        {
            get
            {
                if (!m_TransitLineQueryCreated)
                {
                    m_TransitLineQuery = EntityManager.CreateEntityQuery(new EntityQueryDesc
                    {
                        All = new[]
                        {
                            ComponentType.ReadOnly<Route>(),
                            ComponentType.ReadOnly<TransportLine>(),
                            ComponentType.ReadOnly<RouteWaypoint>(),
                            ComponentType.ReadOnly<PrefabRef>(),
                        },
                        None = new[]
                        {
                            ComponentType.ReadOnly<Temp>(),
                            ComponentType.ReadOnly<Deleted>(),
                        },
                    });
                    m_TransitLineQueryCreated = true;
                }
                return m_TransitLineQuery;
            }
        }

        private EntityQuery TransportLinePrefabQuery
        {
            get
            {
                if (!m_TransportLinePrefabQueryCreated)
                {
                    m_TransportLinePrefabQuery = EntityManager.CreateEntityQuery(
                        ComponentType.ReadOnly<PrefabData>(),
                        ComponentType.ReadOnly<TransportLineData>());
                    m_TransportLinePrefabQueryCreated = true;
                }
                return m_TransportLinePrefabQuery;
            }
        }

        private BridgeResponse ListTransitStops(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            if (!TryReadOptionalTransportType(request, out TransportType? typeFilter, out BridgeResponse typeError))
            {
                return typeError;
            }

            int limit = request.TryGetInt("limit", out int rawLimit)
                ? math.clamp(rawLimit, 1, TransitListHardMax)
                : TransitListDefaultLimit;
            if (!TryGetOptionalCenter(request, out bool hasCenter, out float2 center, out BridgeResponse centerError))
            {
                return centerError;
            }
            float radius = request.TryGetFloat("radius", out float rawRadius) ? math.max(rawRadius, 1f) : 250f;

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            var found = new List<(float distance, object item)>();
            int total = 0;
            using (NativeArray<Entity> entities = TransitStopQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    if (!EntityManager.HasComponent<TransportStopData>(prefabRef.m_Prefab))
                    {
                        continue;
                    }
                    TransportStopData stopData =
                        EntityManager.GetComponentData<TransportStopData>(prefabRef.m_Prefab);
                    if (typeFilter.HasValue && stopData.m_TransportType != typeFilter.Value)
                    {
                        continue;
                    }
                    Transform transform = EntityManager.GetComponentData<Transform>(entity);
                    if (hasCenter && math.distance(transform.m_Position.xz, center) > radius)
                    {
                        continue;
                    }
                    total++;
                    float distance = hasCenter ? math.distance(transform.m_Position.xz, center) : 0f;
                    int connectedLines = 0;
                    if (EntityManager.HasBuffer<ConnectedRoute>(entity))
                    {
                        connectedLines = EntityManager.GetBuffer<ConnectedRoute>(entity, isReadOnly: true).Length;
                    }
                    string ownerPrefab = null;
                    if (EntityManager.HasComponent<Owner>(entity))
                    {
                        ownerPrefab = GetEntityPrefabName(
                            prefabSystem,
                            EntityManager.GetComponentData<Owner>(entity).m_Owner);
                    }
                    var item = new
                    {
                        entity = new { index = entity.Index, version = entity.Version },
                        prefab = GetEntityPrefabName(prefabSystem, entity),
                        type = FormatTransportType(stopData.m_TransportType),
                        passenger = stopData.m_PassengerTransport,
                        cargo = stopData.m_CargoTransport,
                        connectedLines,
                        ownerPrefab,
                        position = new
                        {
                            x = transform.m_Position.x,
                            y = transform.m_Position.y,
                            z = transform.m_Position.z,
                        },
                        distanceM = hasCenter ? (double?)Math.Round(distance, 1) : null,
                    };
                    AddBoundedItem(found, item, distance, limit, hasCenter);
                }
            }

            if (hasCenter)
            {
                found.Sort((a, b) => a.distance.CompareTo(b.distance));
            }
            var stops = new List<object>(found.Count);
            foreach ((float _, object item) in found)
            {
                stops.Add(item);
            }
            return BridgeResponse.Json(new
            {
                stopCount = stops.Count,
                total,
                truncated = total > stops.Count,
                stops,
                note = "read-only snapshot of existing TransportStop entities; create_transit_line connects them. Do not use place_building to make a stop.",
            });
        }

        private BridgeResponse ListTransitLines(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            if (!TryReadOptionalTransportType(request, out TransportType? typeFilter, out BridgeResponse typeError))
            {
                return typeError;
            }

            int limit = request.TryGetInt("limit", out int rawLimit)
                ? math.clamp(rawLimit, 1, TransitListHardMax)
                : TransitListDefaultLimit;
            if (!TryGetOptionalCenter(request, out bool hasCenter, out float2 center, out BridgeResponse centerError))
            {
                return centerError;
            }
            float radius = request.TryGetFloat("radius", out float rawRadius) ? math.max(rawRadius, 1f) : 250f;
            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            NativeArray<UITransportLineData> sorted =
                TransportUIUtils.GetSortedLines(TransitLineQuery, EntityManager, prefabSystem);
            try
            {
                var lines = new List<object>();
                int total = 0;
                for (int i = 0; i < sorted.Length; i++)
                {
                    UITransportLineData line = sorted[i];
                    if (typeFilter.HasValue && line.type != typeFilter.Value)
                    {
                        continue;
                    }
                    if (hasCenter && !TransitLineIntersectsRadius(line.entity, center, radius))
                    {
                        continue;
                    }
                    total++;
                    if (lines.Count >= limit)
                    {
                        continue;
                    }
                    lines.Add(new
                    {
                        entity = new { index = line.entity.Index, version = line.entity.Version },
                        prefab = GetEntityPrefabName(prefabSystem, line.entity),
                        type = FormatTransportType(line.type),
                        active = line.active,
                        isCargo = line.isCargo,
                        lengthM = Math.Round(line.length, 1),
                        stops = line.stops,
                        vehicles = line.vehicles,
                        usage = Math.Round(line.usage, 3),
                    });
                }
                return BridgeResponse.Json(new
                {
                    lineCount = lines.Count,
                    total,
                    truncated = total > lines.Count,
                    lines,
                    note = "read-only snapshot; optional x/z/radius keeps lines with a stop inside the range; vehicles and production stay native",
                });
            }
            finally
            {
                if (sorted.IsCreated)
                {
                    sorted.Dispose();
                }
            }
        }

        private BridgeResponse CreateTransitLine(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }

            string typeRaw = "bus";
            if (request.Query.TryGetValue("type", out string requestedType)
                && !string.IsNullOrWhiteSpace(requestedType))
            {
                typeRaw = requestedType;
            }
            if (!TryParsePassengerTransportType(typeRaw, out TransportType transportType, out BridgeResponse typeError))
            {
                return typeError;
            }
            if (!request.Query.TryGetValue("stops", out string stopsRaw)
                || string.IsNullOrWhiteSpace(stopsRaw))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "provide ?stops=index:version,index:version from list_transit_stops (at least two)");
            }
            if (!TryParseStopRefs(stopsRaw, out List<Entity> stops, out BridgeResponse parseError))
            {
                return parseError;
            }
            if (stops.Count < 2)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "a passenger line needs at least two distinct stops");
            }
            if (stops.Count > TransitLineMaxStops)
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"at most {TransitLineMaxStops} stops per create_transit_line call");
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            if (!TryFindPassengerLinePrefab(
                    transportType,
                    out Entity linePrefabEntity,
                    out TransportLinePrefab linePrefab,
                    out BridgeResponse prefabError))
            {
                return prefabError;
            }

            for (int i = 0; i < stops.Count; i++)
            {
                if (!TryValidatePassengerStop(
                        stops[i],
                        transportType,
                        prefabSystem,
                        out BridgeResponse stopError))
                {
                    return stopError;
                }
            }

            RouteData routeData = EntityManager.GetComponentData<RouteData>(linePrefabEntity);
            float minDistance = RouteUtils.GetMinWaypointDistance(routeData);
            for (int i = 1; i < stops.Count; i++)
            {
                float3 previous = EntityManager.GetComponentData<Transform>(stops[i - 1]).m_Position;
                float3 current = EntityManager.GetComponentData<Transform>(stops[i]).m_Position;
                if (math.distance(previous, current) < minDistance)
                {
                    return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                        $"stops {i} and {i + 1} are closer than the native waypoint snap ({minDistance:F1}m); pick farther stops");
                }
            }

            BridgeToolSystem tool = World.GetOrCreateSystemManaged<BridgeToolSystem>();
            if (!tool.TryQueueTransitLine(linePrefabEntity, linePrefab, stops.ToArray(), request))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    "another build operation is in progress, retry shortly");
            }
            return null;
        }

        private BridgeResponse DeleteTransitLine(BridgeRequest request)
        {
            if (!TryGetCity(out _, out BridgeResponse error))
            {
                return error;
            }
            if (!request.TryGetInt("index", out int index)
                || !request.TryGetInt("version", out int version))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "provide ?index=&version= of a line from list_transit_lines");
            }
            if (!TryResolveExistingEntity(index, version, out Entity entity))
            {
                return BridgeResponse.Error(BridgeErrorKind.NotFound,
                    $"entity {index}:{version} does not exist (stale id?)");
            }
            if (!EntityManager.HasComponent<TransportLine>(entity)
                || !EntityManager.HasComponent<Route>(entity))
            {
                return BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "entity is not a transit line; use list_transit_lines, not list_buildings or list_transit_stops");
            }
            if (EntityManager.HasComponent<Temp>(entity)
                || EntityManager.HasComponent<Deleted>(entity))
            {
                return BridgeResponse.Error(BridgeErrorKind.Conflict,
                    "line is already being deleted");
            }

            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            string prefabName = GetEntityPrefabName(prefabSystem, entity);
            World.GetOrCreateSystemManaged<EndFrameBarrier>()
                .CreateCommandBuffer()
                .AddComponent(entity, default(Deleted));
            return BridgeResponse.Json(new
            {
                deleted = true,
                prefab = prefabName,
                entity = new { index = entity.Index, version = entity.Version },
                note = "deleted through EndFrameBarrier.Deleted, the same path as the transportation overview",
            });
        }

        private bool TryFindPassengerLinePrefab(
            TransportType transportType,
            out Entity prefabEntity,
            out TransportLinePrefab prefab,
            out BridgeResponse error)
        {
            prefabEntity = Entity.Null;
            prefab = null;
            error = null;
            PrefabSystem prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            Entity lockedMatch = Entity.Null;
            TransportLinePrefab lockedPrefab = null;
            using (NativeArray<Entity> entities = TransportLinePrefabQuery.ToEntityArray(Allocator.Temp))
            {
                foreach (Entity entity in entities)
                {
                    TransportLineData data = EntityManager.GetComponentData<TransportLineData>(entity);
                    if (data.m_TransportType != transportType
                        || !data.m_PassengerTransport
                        || data.m_CargoTransport)
                    {
                        continue;
                    }
                    PrefabBase prefabBase = prefabSystem.GetPrefab<PrefabBase>(entity);
                    var linePrefab = prefabBase as TransportLinePrefab;
                    if (linePrefab == null)
                    {
                        continue;
                    }
                    if (IsLocked(entity))
                    {
                        if (lockedMatch == Entity.Null)
                        {
                            lockedMatch = entity;
                            lockedPrefab = linePrefab;
                        }
                        continue;
                    }
                    prefabEntity = entity;
                    prefab = linePrefab;
                    return true;
                }
            }
            if (lockedMatch != Entity.Null)
            {
                error = BridgeResponse.Error(BridgeErrorKind.Conflict,
                    $"passenger {FormatTransportType(transportType)} line prefab '{lockedPrefab.name}' is locked (milestone not reached)");
                return false;
            }
            error = BridgeResponse.Error(BridgeErrorKind.NotFound,
                $"no passenger {FormatTransportType(transportType)} line prefab is available");
            return false;
        }

        private bool TryValidatePassengerStop(
            Entity stop,
            TransportType transportType,
            PrefabSystem prefabSystem,
            out BridgeResponse error)
        {
            error = null;
            if (!EntityManager.HasComponent<TransportStop>(stop)
                || EntityManager.HasComponent<Temp>(stop)
                || EntityManager.HasComponent<Deleted>(stop))
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"entity {stop.Index}:{stop.Version} is not an existing transit stop; use list_transit_stops");
                return false;
            }
            if (EntityManager.HasComponent<TaxiStand>(stop))
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "taxi stands cannot join a passenger line");
                return false;
            }
            PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(stop);
            if (!EntityManager.HasComponent<TransportStopData>(prefabRef.m_Prefab))
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"stop {GetEntityPrefabName(prefabSystem, stop)} has no TransportStopData");
                return false;
            }
            TransportStopData stopData =
                EntityManager.GetComponentData<TransportStopData>(prefabRef.m_Prefab);
            if (stopData.m_TransportType != transportType)
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"stop {GetEntityPrefabName(prefabSystem, stop)} is {FormatTransportType(stopData.m_TransportType)}, not {FormatTransportType(transportType)}");
                return false;
            }
            if (!stopData.m_PassengerTransport)
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"stop {GetEntityPrefabName(prefabSystem, stop)} is not a passenger stop");
                return false;
            }
            if (!EntityManager.HasBuffer<ConnectedRoute>(stop))
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"stop {GetEntityPrefabName(prefabSystem, stop)} has no ConnectedRoute buffer, so the native route tool cannot attach a waypoint");
                return false;
            }
            return true;
        }

        private bool TryParseStopRefs(string raw, out List<Entity> stops, out BridgeResponse error)
        {
            stops = new List<Entity>();
            error = null;
            string trimmed = raw.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[')
            {
                trimmed = trimmed.Trim('[', ']');
            }
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "stops must be index:version pairs from list_transit_stops");
                return false;
            }

            var seen = new HashSet<Entity>();
            foreach (string token in trimmed.Split(','))
            {
                string piece = token.Trim().Trim('"');
                int colon = piece.IndexOf(':');
                if (colon <= 0
                    || !int.TryParse(
                        piece.Substring(0, colon),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int index)
                    || !int.TryParse(
                        piece.Substring(colon + 1),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int version))
                {
                    error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                        $"could not parse stop '{piece.Trim()}'; use index:version from list_transit_stops");
                    return false;
                }
                if (!TryResolveExistingEntity(index, version, out Entity entity))
                {
                    error = BridgeResponse.Error(BridgeErrorKind.NotFound,
                        $"stop {index}:{version} does not exist (stale id?)");
                    return false;
                }
                if (!seen.Add(entity))
                {
                    error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                        $"stop {index}:{version} is listed twice; a simple passenger line uses each stop once");
                    return false;
                }
                stops.Add(entity);
            }
            return true;
        }

        private bool TryReadOptionalTransportType(
            BridgeRequest request,
            out TransportType? typeFilter,
            out BridgeResponse error)
        {
            typeFilter = null;
            error = null;
            if (!request.Query.TryGetValue("type", out string typeRaw)
                || string.IsNullOrWhiteSpace(typeRaw))
            {
                return true;
            }
            if (!TryParsePassengerTransportType(typeRaw, out TransportType parsedType, out error))
            {
                return false;
            }
            typeFilter = parsedType;
            return true;
        }

        private bool TryParsePassengerTransportType(
            string raw,
            out TransportType transportType,
            out BridgeResponse error)
        {
            transportType = TransportType.None;
            error = null;
            if (!Enum.TryParse(raw.Trim(), true, out transportType)
                || transportType == TransportType.None
                || transportType == TransportType.Count)
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    "type must be a passenger transport kind such as bus, tram, train, subway or ferry");
                return false;
            }
            if (transportType == TransportType.Taxi
                || transportType == TransportType.Work
                || transportType == TransportType.Post
                || transportType == TransportType.Bicycle
                || transportType == TransportType.Car)
            {
                error = BridgeResponse.Error(BridgeErrorKind.InvalidArguments,
                    $"{FormatTransportType(transportType)} is not a simple passenger line; use bus (default) or another passenger type with matching stops");
                return false;
            }
            return true;
        }

        private static string FormatTransportType(TransportType type)
        {
            return Enum.GetName(typeof(TransportType), type)?.ToLowerInvariant() ?? type.ToString();
        }

        private bool TransitLineIntersectsRadius(Entity line, float2 center, float radius)
        {
            if (!EntityManager.HasBuffer<RouteWaypoint>(line))
            {
                return false;
            }
            DynamicBuffer<RouteWaypoint> waypoints =
                EntityManager.GetBuffer<RouteWaypoint>(line, isReadOnly: true);
            float radiusSq = radius * radius;
            foreach (RouteWaypoint waypoint in waypoints)
            {
                if (!EntityManager.Exists(waypoint.m_Waypoint)
                    || !EntityManager.HasComponent<Position>(waypoint.m_Waypoint))
                {
                    continue;
                }
                float3 position = EntityManager.GetComponentData<Position>(waypoint.m_Waypoint).m_Position;
                if (math.distancesq(position.xz, center) <= radiusSq)
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddBoundedItem(
            List<(float distance, object item)> found,
            object item,
            float distance,
            int limit,
            bool hasCenter)
        {
            if (found.Count < limit)
            {
                found.Add((distance, item));
                return;
            }
            if (!hasCenter)
            {
                return;
            }
            int worst = 0;
            for (int j = 1; j < found.Count; j++)
            {
                if (found[j].distance > found[worst].distance)
                {
                    worst = j;
                }
            }
            if (distance < found[worst].distance)
            {
                found[worst] = (distance, item);
            }
        }
    }
}
