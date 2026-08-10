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
- Off-road facilities need separate underground connections.
- Connect water/sewage plant to road network with underground pipes at one end.
- WaterPumpingStation01 draws SURFACE water: use terrain water samples to find a shoreline, keep the building on land, and put its intake side in water. Do not use groundWater data for this prefab.
- A Groundwater Pumping Station is a different prefab: use gridmap (groundWater) only for that building. A Water Tower does not need a water source.
- A utility building only functions when a ROAD reaches it: build a short road to the site FIRST, then place the pump/outlet/plant adjacent to that road (place_building auto-finds the road-facing spot). Do not place it in empty land.

## Connecting new buildings precisely
- place_building handles utility connections: a road-fronted water pump uses the pipes already carried by the road; wind turbines, sewage outlets, and power plants receive their required short cable/pipe. Normally you do NOT need to draw connectors by hand.
- If you connect manually, network connections attach at NODES: every line/pipeline should start or end at a known node. list_roads returns each segment's start/end — those coordinates ARE the nodes.
- Buildings placed via place_building report their exact x/z — reuse those coordinates as the network endpoint when connecting manually.

## Burying underground networks
- Underground pipes/cables need NEGATIVE elevation: e1=e2=-10 to -20m.
- build_road now defaults Pipes and Ground Cables to -10m (buried) automatically; only pass explicit e1/e2 when you need something different.
- Positive values = elevated/bridge segments (wrong for buried).

## Sewage is critical
- If problems[] shows sewage or notifications show "Sewage Notification": BUILD SewageOutlet01 near water immediately.
- place_building SewageOutlet01 with x/z + radius: it finds a legal water-adjacent, road-facing spot and places it in ONE step (auto-connected to the road network).
- After building, use wait_simulation() once (advances 1 in-game hour) and re-check problems[].
- Population cannot grow with sewage backing up.

## Budget changes need simulation time
- A service budget's effective capacity updates while the simulation runs. After raising an electricity, water, or sewage budget to cure a shortage, call wait_simulation(hours=1) and re-check city_services/notifications before building more capacity. A read taken immediately after changing the slider can still show the old low-efficiency output.
