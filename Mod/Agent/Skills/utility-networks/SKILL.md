---
name: utility-networks
description: How electricity, water and sewage networks work in Cities: Skylines II, and how to place underground utilities correctly.
---

# Utility networks & roads

## Roads carry utilities

- Most roads in Cities: Skylines II automatically carry electricity, water and sewage lines. Buildings placed along those roads connect to all three automatically.
- Highways and a few special network types carry nothing by default. Before planning a remote facility, check the prefab (find_prefabs) or upgrade the road; do not assume a freeway distributes power or water.
- Because roads already distribute utilities, do NOT draw parallel underground water/sewage pipes or power cables next to every road. That is wasted money and clutter.

## When separate underground networks are needed

- Buildings or facilities without road access (parks, farms, power plants in open land, water towers, etc.) need a separate underground connection.
- Connect a water/sewage plant to the city network with underground pipes; a pipe only needs to touch the road network at one end.
- Use gridmap (layer=groundWater / groundWaterPollution) to find water for pumps and to avoid polluted sources.

## Burying underground networks deep enough

- Underground pipes/cables must use explicit NEGATIVE elevation on both endpoints. Omit e1/e2 only for ground-level roads.
- Recommended depth: e1 = e2 = -10 to -20 meters for underground water, sewage and power connections. Values near 0 place the network just under the surface and can look or behave wrong (too shallow).
- Never pass large positive e1/e2 for "underground" utilities: positive values mean elevated/bridge segments, which is the opposite of buried.

## Gameplay loop reminder

You can act while the simulation runs; the game validates construction. Use agent_advance_time (0.5-2 in-game hours) to observe results with game_state / notifications / screenshot.
