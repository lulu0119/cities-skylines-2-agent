---
name: city-building
description: How to grow a city from empty land through early, mid and late phases.
---

# City building playbook

## Priorities

- Check city_services.problems[] and notifications FIRST. Fix blocking problems (sewage, water, electricity, garbage, road access) before zoning or expanding.
- city_services.garbage.productionRate is how much garbage the city generates per day, not an unserved deficit. Never add garbage facilities merely because it is positive; act on an actual GarbagePilingUp notification.
- Before expanding, call list_tiles (filter=owned) to see which map tiles you own; buy adjacent unowned tiles with buy_tiles when you need more room. Roads and zones only work on owned tiles.
- Standalone buildings (service buildings, unique/signature buildings) MUST be adjacent to a road. place_building with prefab + x/z + radius finds the first legal, road-facing position inside the radius and places it in ONE step (the building is auto-aligned to the road). Utility placement handles the required connection automatically: road-fronted water pumps use the pipes carried by the road, while sewage outlets and power producers receive the appropriate short pipe/cable. find_placement is only a preview: never end a turn with just a found position.
- Service buildings need a ROAD AT THE SITE FIRST. If there is no road near the intended location (river/lake shore, resource field, empty plain), build a short road to that spot FIRST, then place_building next to it. Never place a water pump, sewage outlet, power plant or clinic in empty land and hope it connects later.
- Zone what demand asks for. Regular residential / commercial / industrial / office buildings grow from zone_area along roads; use place_building only for standalone buildings (service buildings, unique/landmark/signature buildings, special production or extraction facilities). Prefer the generic residential/commercial names: zone_area resolves them to the current map theme so matching growable buildings exist.
- zone_area paints every zonable cell in its radius and can overwrite an occupied district, condemning buildings whose old zone no longer matches. Before painting, use zoning around the target and prefer fresh road frontage or a small radius that does not overlap occupied cells. If an accidental rezone causes Condemned notifications, restore the original zone and simulate briefly before demolishing occupied buildings.
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

- Empty land: connect a road from the highway; make sure the city has power, water and sewage handling (choose prefabs by the map with terrain / gridmap / find_prefabs).
- Small city: add education and medical care, fix access and utility problems as they appear.
- Growing city: add garbage service, police/fire, medium-density housing and more utility capacity.
- Larger city: hospitals, universities, offices, cargo/industry upgrades — only as demand and problems require.

## Always

- Verify each problem is cleared before moving on.
- The simulation clock belongs to the player: use wait_simulation, never force long runs or poll game_state.
