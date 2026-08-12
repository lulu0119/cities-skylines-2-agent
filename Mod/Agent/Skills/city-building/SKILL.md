---
name: city-building
description: How to grow a city from empty land through early, mid and late phases.
---

# City building playbook

## Priorities

- Check city_services.problems[] and notifications FIRST. Fix blocking problems (sewage, water, electricity, garbage, road access) before zoning or expanding.
- city_services.garbage.productionRate is how much garbage the city generates per day, not an unserved deficit. Never add garbage facilities merely because it is positive; act on an actual GarbagePilingUp notification.
- Before expanding, call list_tiles (filter=owned) to see which map tiles you own; buy adjacent unowned tiles with buy_tiles when you need more room. Roads and zones only work on owned tiles.
- When the city reaches a milestone, or an advanced service remains locked, enable the progression tool group and call get_progression. Spend legitimately earned Development Points with purchase_development_node on an eligible node that addresses the current bottleneck; do not force-place locked prefabs.
- For infrastructure and service buildings, use find_prefabs(role=...) to choose one unlocked standalone prefab, then call place_building once. For every site you choose yourself, provide x/z with a reasonable radius and omit rotation; placement resolves clearance, road frontage, shoreline orientation and utility connections from prefab data. Omit radius or set rotation only for an exact pose explicitly selected by the player or a context block. If exact placement fails, retry with a larger radius and no rotation.
- Do not assume every service building needs a road. The placement tools enforce BuildingFlags.RequireRoad and PlacementFlags.Shoreline independently. Build road access only when the selected prefab declares it; for example, a sewage outlet needs a shoreline and sewage-pipe connection but no road frontage.
- Zone what demand asks for. Regular residential / commercial / industrial / office buildings grow from zone_area along roads; use place_building only for standalone buildings (service buildings, unique/landmark/signature buildings, special production or extraction facilities). Prefer the generic residential/commercial names: zone_area resolves them to the current map theme so matching growable buildings exist.
- Prefer zone_rectangle for straight road frontage: align its width/depth/rotation to the fresh blocks so it does not spill into occupied neighboring districts. zone_area remains useful for small irregular patches, but its circular brush can overwrite an occupied district and condemn buildings whose old zone no longer matches. Before painting, use zoning around the target. If an accidental rezone causes Condemned notifications, restore the original zone and simulate briefly before demolishing occupied buildings.
- The normal Residential High prefabs unlock at the Big Town milestone (46,700 XP), not at a population threshold. Before then, high-density demand can be positive while both normal high-density prefabs remain locked. Residential LowRent unlocks much earlier (Grand Village, 8,300 XP) and can satisfy that demand: when list_zones reports it unlocked and high-density demand is strong, zone Residential LowRent instead of endlessly expanding medium density. Use medium density as the fallback.
- Expand roads outward on owned tiles in short segments; keep utilities ahead of demand and give each new road a network role before zoning it.
- wait_simulation(hours) advances the requested 1-24 in-game hours (default 1) at high speed and then restores the previous speed/pause state. Buildings take game hours to construct, level up and attract residents: use 1-2 hours to verify a repair or capacity change, and about 4 hours after a normal growth batch. When repeated checks show problems=[] with a positive budget and ample utility headroom, use an 8-12 hour growth window before re-reading city_overview, city_services, budget and notifications.
- Zoning does NOT require clearing trees. Trees, bushes and ruins do not block zone growth: the game clears them automatically when a building constructs. If a zoned area still has no buildings after several in-game hours, do NOT waste turns demolishing trees; instead verify the zone is really registered road-adjacent (zoning / debug_zone_blocks), and that the road network reaches an outside connection.

## Road hierarchy

- Build a connected hierarchy instead of making every zoned street carry through traffic: highway/outside connection -> arterial -> collector -> local street.
- Highways carry outside and long-distance traffic. Connect them to a small number of arterials through ramps; do not zone highway frontage.
- Arterials carry high-volume trips across districts. Use medium or large roads, keep junctions less frequent than on neighborhood streets, and avoid making them the only entrance to every building or local block.
- Collectors gather several local streets and feed arterials. Give a district more than one collector path when possible so one junction does not become the city's single choke point.
- Local streets provide direct zoning and service-building access. Use small roads for these blocks and connect them to collectors rather than sending every local street straight into an arterial.
- Give industrial, specialized-industry and garbage traffic a short collector route toward an arterial or highway; keep freight from crossing residential local streets when another connection is possible.
- When congestion appears, trace where neighborhood traffic collects, then add an alternate collector connection or upgrade the observed high-volume main road. Adding lanes to every local street does not repair a bad hierarchy.

## Phases (adapt to the city, not a fixed recipe)

- Empty land: connect a road from the highway, then choose unlocked power, water and sewage prefabs with find_prefabs and place them with x/z/radius. Use terrain and gridmap when resource or shoreline evidence is relevant.
- Small city: add education and medical care, fix access and utility problems as they appear.
- Growing city: add garbage service, police/fire, medium-density housing and more utility capacity.
- Larger city: hospitals, universities, offices, cargo/industry upgrades — only as demand and problems require.

## Always

- Verify each problem is cleared before moving on.
- The simulation clock belongs to the player: use wait_simulation, never force long runs or poll game_state.
