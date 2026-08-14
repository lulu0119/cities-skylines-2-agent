---
name: city-building
description: How to grow a city from empty land through early, mid and late phases.
---

# City building playbook

## Priorities

- The injected problem ledger is the source of truth for city problems (deduped identity, duration, escalation, resolved). Use it instead of re-polling notifications each turn. Call notifications only when you need raw icon locations or targets. city_services.problems[] still describes sewage/water/electricity capacity gaps; those also appear in the ledger.
- Fix blocking problems (sewage, water, electricity, garbage, road access) before zoning or expanding.
- city_services.garbage.productionRate is how much garbage the city generates per day, not an unserved deficit. Never add garbage facilities merely because it is positive; act on an actual GarbagePilingUp notification.
- Before expanding, call list_tiles (filter=owned) to see which map tiles you own; buy adjacent unowned tiles with buy_tiles when you need more room. Roads and zones only work on owned tiles. Pass filter=all explicitly when you need every tile.
- When the city reaches a milestone, or an advanced service remains locked, enable the progression tool group and call get_progression. Spend legitimately earned Development Points with purchase_development_node on an eligible node that addresses the current bottleneck; do not force-place locked prefabs.
- For infrastructure and service buildings, use list_prefabs(role=...) to choose one unlocked standalone prefab, then call place_building once. For every site you choose yourself, provide x/z with a reasonable radius and omit rotation; placement resolves clearance, road frontage, shoreline orientation, and off-road water/sewage/low-voltage connections. It does not draw high-voltage lines: read utility-networks before placing a non-wind power plant. Omit radius or set rotation only for an exact pose explicitly selected by the player or a context block. If exact placement fails, retry with a larger radius and no rotation.
- Do not assume every service building needs a road. The placement tools enforce BuildingFlags.RequireRoad and PlacementFlags.Shoreline independently. Build road access only when the selected prefab declares it; for example, a sewage outlet needs a shoreline and sewage-pipe connection but no road frontage.
- Zone what demand asks for. Regular residential / commercial / industrial / office buildings grow from zone_area along roads; use place_building only for standalone buildings (service buildings, unique/landmark/signature buildings, special production or extraction facilities). Prefer the generic residential/commercial names: zone_area resolves them to the current map theme so matching growable buildings exist.
- Prefer zone_rectangle for straight road frontage: align its width/depth/rotation to the fresh blocks so it does not spill into occupied neighboring districts. zone_area remains useful for small irregular patches, but its circular brush can overwrite an occupied district and condemn buildings whose old zone no longer matches. Before painting, use count_zone_cells around the target. If an accidental rezone causes Condemned notifications, restore the original zone and simulate briefly before demolishing occupied buildings.
- The normal Residential High prefabs unlock at the Big Town milestone (46,700 XP), not at a population threshold. Before then, high-density demand can be positive while both normal high-density prefabs remain locked. Residential LowRent unlocks much earlier (Grand Village, 8,300 XP) and can satisfy that demand: when list_zone_types reports it unlocked and high-density demand is strong, zone Residential LowRent instead of endlessly expanding medium density. Use medium density as the fallback.
- Expand roads outward on owned tiles in short segments; keep utilities ahead of demand and give each new road a network role before zoning it.
- wait_simulation(hours) advances the requested 1-24 in-game hours (default 1) at the engine 8x speed cap and then restores the previous speed/pause state. The success payload includes city overview fields (population, money, xp, …) plus hours/completed/targetReached — use that as the first snapshot and after growth batches. Buildings take game hours to construct, level up and attract residents: use 1-2 hours to verify a repair or capacity change, and about 4 hours after a normal growth batch. When the ledger shows no open problems, a positive budget and ample utility headroom, use an 8-12 hour growth window before waiting again and re-reading budget.
- Zoning does NOT require clearing trees. Trees, bushes and ruins do not block zone growth: the game clears them automatically when a building constructs. If a zoned area still has no buildings after several in-game hours, do NOT waste turns demolishing trees; instead verify the zone is really registered road-adjacent (count_zone_cells / debug_zone_blocks), and that the road network reaches an outside connection.

## Road hierarchy

- For ordinary roads, omit mode and e1/e2: build_road defaults to ground and samples the route at roughly 4m or finer intervals for water and local grade, rejecting detected water crossings or grades above 10% (or a stricter selected-prefab limit). If ground placement rejects the route, move or reshape it; do not disguise the same route with arbitrary elevation.
- Use mode=grade-separated only when you intentionally need a bridge, elevated road or tunnel. Always provide both e1 and e2, with at least one nonzero. This mode expresses the intended crossing; native placement validation still decides whether the segment is legal.
- Build a connected hierarchy instead of making every zoned street carry through traffic: highway/outside connection -> arterial -> collector -> local street.
- Highways carry outside and long-distance traffic. Connect them to a small number of arterials through ramps; do not zone highway frontage.
- Arterials carry high-volume trips across districts. Use medium or large roads, keep junctions less frequent than on neighborhood streets, and avoid making them the only entrance to every building or local block.
- Collectors gather several local streets and feed arterials. Give a district more than one collector path when possible so one junction does not become the city's single choke point.
- Local streets provide direct zoning and service-building access. Use small roads for these blocks and connect them to collectors rather than sending every local street straight into an arterial.
- Give industrial, specialized-industry and garbage traffic a short collector route toward an arterial or highway; keep freight from crossing residential local streets when another connection is possible.
- When the ledger shows Traffic Bottleneck Notification or congestion, diagnose first: inspect_network_topology(kind=road) for near-miss, unnoded crossing, too-close junctions, short stubs, isolated roads; and list_networks(kind=road, sort=congestion or traffic_volume). Degree-1 dead ends are facts, not automatic errors. Isolation for pipes/cables uses inspect_network_topology(kind=water|sewage|low_voltage), not list_networks.
- Then write: build_road for a missing collector, alternate path, or noded connection; set_road_features only for composition (it does not change prefab, width, or lanes); replace_road_type only when that development tool is on the surface, and only for one simple standalone road edge. Do not add lanes to every local street.
- After the write, wait_simulation (1-2 hours for a local repair) and re-measure the same sorted list_networks plus the ledger. If congestion is unchanged, change the hierarchy rather than repeating the same local write.

## Specialized industry

- Place the hub with list_prefabs(role=specialized-industry) then place_building. Expand the extractor with expand_operational_area (find the hub via list_buildings role=specialized-industry, operational_area=extractor). expand_operational_area already ranks natural resources; do not ask for a separate overlay tool.
- Then wait_simulation and verify production with get_operational_area (extractedAmount, workAmount, resource coverage). A hub without extraction is not success. Do not combine hub and area into one tool.

## Phases (adapt to the city, not a fixed recipe)

- Empty land: connect a road from the highway, then choose unlocked power, water and sewage prefabs with list_prefabs and place them with x/z/radius. Non-wind power needs a transformer and a hand-built high-voltage cable; read utility-networks. Use local_map before choosing an expansion direction or when shoreline/slope evidence matters; its LOCAL_MAP frame, regions, sectors and road topology are compact spatial evidence, while the write tools remain responsible for native validation.
- Small city: add education and medical care, fix access and utility problems as they appear.
- Growing city: add garbage service, police/fire, medium-density housing and more utility capacity.
- Larger city: hospitals, universities, offices, cargo/industry upgrades — only as demand and problems require.

## Always

- Verify each problem is cleared before moving on.
- The simulation clock belongs to the player: use wait_simulation, never force long runs or poll.
