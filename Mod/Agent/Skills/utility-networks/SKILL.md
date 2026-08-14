---
name: utility-networks
description: How electricity, water and sewage networks work in CS2.
---

# Utility networks

## Roads carry utilities
- Most roads carry electricity, water and sewage automatically. Buildings on roads connect to all three.
- Do NOT draw parallel pipes/cables next to roads. Wasted money.

## Electricity has TWO voltage networks
- LOW voltage: normal city grid, buildings, and WIND TURBINES (connect with Low-voltage Ground Cable).
- HIGH voltage: power plants (coal, gas, solar farm, geothermal, nuclear, etc.) and long-distance lines (connect with High-voltage Line).
- Low-voltage and high-voltage networks CANNOT connect directly. To bridge them, place a transformer station (Transformer01) and connect one side to low voltage and the other side to high voltage.
- Rule of thumb: wind turbines -> low voltage; every other power producer -> high voltage; buildings/consumers -> low voltage.

## When separate underground networks are needed
- Off-road utility facilities need separate underground connections when their prefab declares a water, sewage or low-voltage node.
- Connect an off-road water/sewage facility to the road-carried network with the corresponding underground pipe.
- WaterPumpingStation01 draws SURFACE water: search near a shoreline and let place_building/native validation resolve the exact wet/dry pose. Do not use groundWater data for this prefab.
- A Groundwater Pumping Station is a different prefab: use gridmap (groundWater) only for that building. A Water Tower does not need a water source.
- Do not infer road access from a facility's name or category. place_building reads BuildingFlags.RequireRoad: only those prefabs need road frontage. A shoreline requirement is independent and comes from PlacementFlags.Shoreline.

## Connecting new buildings precisely
- place_building handles utility connections from prefab node flags: road-fronted nodes use the utilities carried by the road, while off-road nodes receive the corresponding short pipe/cable. Normally you do NOT need to draw connectors by hand.
- For a site you choose, pass a radius and omit rotation so the tool can resolve road/shoreline orientation. Omit radius or force a rotation only when the player explicitly selected an exact pose; after an exact failure, add or enlarge radius and remove rotation.
- If you connect manually, network connections attach at NODES: every line/pipeline should start or end at a known node. list_roads returns each segment's start/end — those coordinates ARE the nodes.
- Buildings placed via place_building report their exact x/z — reuse those coordinates as the network endpoint when connecting manually.

## Burying underground networks
- Underground pipes/cables need NEGATIVE elevation: e1=e2=-10 to -20m.
- build_road mode is only for road prefabs. Never pass mode for pipes, cables, High-voltage Lines or other utility-network prefabs.
- build_road defaults Pipes and Ground Cables to -10m (buried) automatically; omit e1/e2 to accept that burial, and only pass explicit e1/e2 when you need a different utility elevation.
- Positive values = elevated/bridge segments (wrong for buried).

## Sewage is critical
- If problems[] shows sewage or notifications show "Sewage Notification": BUILD SewageOutlet01 near water immediately.
- place_building SewageOutlet01 with x/z + radius: it snaps to a legal shoreline pose and places it in ONE step. The outlet does not need road frontage; place_building connects its sewage node to the nearby road-carried network with an underground sewage pipe.
- After building, use wait_simulation() once (advances 1 in-game hour) and re-check problems[].
- Population cannot grow with sewage backing up.

## Budget changes need simulation time
- A service budget's effective capacity updates while the simulation runs. After raising an electricity, water, or sewage budget to cure a shortage, call wait_simulation(hours=1) and re-check city_services/notifications before building more capacity. A read taken immediately after changing the slider can still show the old low-efficiency output.
