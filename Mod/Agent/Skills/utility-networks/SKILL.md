---
name: utility-networks
description: How electricity, water and sewage networks work in CS2.
---

# Utility networks

## Roads carry utilities
- Most roads carry low-voltage electricity, water and sewage. Buildings on those roads connect to those three.
- Do NOT draw parallel pipes or low-voltage cables next to roads.

## Electricity has TWO voltage networks
- LOW voltage: the city grid, ordinary buildings, and WIND TURBINES.
- HIGH voltage: coal, gas, solar farm, geothermal, nuclear and other non-wind plants, plus long-distance lines.
- Low-voltage and high-voltage cannot join. Bridge them with TransformerStation01: one side high voltage, the other side a road or Low-voltage Ground Cable.
- Wind turbines -> low voltage. Every other power producer -> high voltage. Consumers -> low voltage.

## What place_building actually connects
- Off-road water, sewage, or low-voltage nodes: a short matching pipe or cable to a network within 150m. Wind turbines use this path.
- Road-fronted buildings use the utilities on that road. That is low voltage, water and sewage only.
- place_building never draws High-voltage Line or High-voltage Ground Cable. A placed coal plant is not on the grid until you wire high voltage yourself.

## Power plant loop (non-wind)
1. Place the plant with radius on a road. RequireRoad plants with a no-road warning produce 0 kW even if cables exist. Do not aim 100m+ away from the road you intend to use.
2. Place TransformerStation01 on a road next to the plant, not a long walk away.
3. build_road High-voltage Ground Cable between them. High-voltage Line is 30m wide: a lot-center to lot-center segment will OverlapExisting. Keep endpoints outside both footprints, on short segments.
4. Confirm the transformer touches a powered road or add a short Low-voltage Ground Cable to a road node.
5. wait_simulation(hours=1). If Electricity Notification or Powerline Not Connected remains in notificationCounts, the high-voltage side is still open — do not start water or zoning yet.

## When separate underground networks are needed
- Off-road facilities need a pipe or cable only when the prefab declares a water, sewage or low-voltage node.
- WaterPumpingStation01 draws SURFACE water: search near a shoreline. Do not use groundWater for this prefab.
- Groundwater Pumping Station is a different prefab: use probe_cell_layer (groundWater) only for that building. A Water Tower does not need a water source.
- Do not infer road access from a name. place_building reads BuildingFlags.RequireRoad; shoreline is PlacementFlags.Shoreline.

## Connecting manually
- For a site you choose, pass a radius and omit rotation. Omit radius or force rotation only for an exact player pose; after an exact failure, enlarge radius and drop rotation.
- Network connections attach at NODES. list_networks (kind required) start/end coordinates are nodes; list rows are inventory only (road traffic / low-voltage electricity{flow,capacity,bottleneck} / water-sewage geometry). Isolation QA is inspect_network_topology(kind=...). place_building x/z is the lot center, not a guaranteed utility node — offset the cable endpoint outside the footprint toward the other building.
- After a high-voltage OverlapExisting, switch to High-voltage Ground Cable and shorten the segment; do not retry the same 30m-wide Line through the buildings.

## Burying underground networks
- Underground pipes/cables need NEGATIVE elevation: e1=e2=-10 to -20m.
- build_road mode is only for road prefabs. Never pass mode for pipes, cables, High-voltage Lines or other utility-network prefabs.
- build_road defaults Pipes and Ground Cables to -10m (buried); omit e1/e2 unless you need a different elevation.
- Positive values = elevated/bridge segments (wrong for buried).

## Sewage is critical
- If serviceGaps show sewage or notificationCounts include Sewage Notification: BUILD SewageOutlet01 near water immediately.
- place_building SewageOutlet01 with x/z + radius snaps to a legal shoreline. The outlet does not need road frontage; place_building connects its sewage node to a nearby sewage network.
- After building, wait_simulation() once and re-check serviceGaps.
- Population cannot grow with sewage backing up.

## Budget changes need simulation time
- After raising an electricity, water, or sewage budget, wait_simulation(hours=1) and re-check overview/problems. A read taken immediately after the slider can still show the old output.
