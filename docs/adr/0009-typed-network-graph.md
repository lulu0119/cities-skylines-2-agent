# One typed network graph behind list, demolish, and topology QA

Status: accepted

This ADR records the typed-network graph seam. It is not a second inventory taxonomy. Roads, water pipes, sewage pipes, and low-voltage cables are one typed Net (`Game.Net` edges). Auto-connect, `list_networks`, demolish, and topology QA all use `TypedNetworkKinds` from native prefab layers.

## Decision

Model-facing Net inventory is one tool: `list_networks`. It requires `kind=road|water|sewage|low_voltage` (no `all`). Rows are inventory only — shared fields are entity, prefab, start, end, length; `distanceM` when a center is given; roads also expose `widthM` and `traffic{volumeIndex, congestionIndex, activeBottlenecks}`. Water and sewage stay geometry only. Low-voltage rows add `electricity{flow, capacity, bottleneck}` from the native flow graph: the net edge's `ElectricityNodeConnection` points at a middle `ElectricityFlowNode`; incident `ConnectedFlowEdge` entries hold `ElectricityFlowEdge` (`m_Flow`, `m_Capacity`, `isBottleneck`). The row reports the worst-loaded incident edge (`|flow|/capacity`) and `bottleneck` if any incident edge is a bottleneck. Signatures confirmed on Windows `Game.dll` ([ops handoff](../ops/2026-08-15-windows-game-dll-handoff.md)). List rows carry zero topology: no `isolated`, `kinds[]`, or `componentSize`. Sort rules: `distance` when x/z are present; `traffic_volume` / `congestion` only for `kind=road`; `load` ranks low-voltage by that electricity ratio. Default limit 16, hard max 64.

`inspect_network_topology` stays QA (what is wrong), distinct from `list_networks` (what is there). It also requires `kind`. For roads it reports geometry findings (near-miss, unnoded crossing, too-close junctions, short stubs, isolated roads) plus optional dead ends. For water, sewage, and low-voltage it reports only isolated components that do not share a node with any road edge (`isolated_water` / `isolated_sewage` / `isolated_low_voltage`). Isolation for every kind is only via this QA tool, not the list. Isolated pipes and cables remain demolishable through native bulldoze; raw `Deleted` is not a cleanup path. `demolish` stays one write and accepts listed typed-network edges via native bulldoze. `build_road` stays the linear write.

## Considered Options

- **Separate `list_pipes` / `list_cables` with their own classifiers.** Rejected: the failure mode was duplicated classification, not missing tool names.
- **Keep `list_roads` as HTTP diagnostic like `find_placement`.** Rejected: unused tools are deleted; a leftover `/city/roads` is a second Net inventory.
- **Raw `Deleted` for utility cleanup.** Rejected: it skips node and lane cleanup and corrupts the graph ([ADR-0002](0002-native-validation.md)).
- **Merge topology QA into `list_networks`.** Rejected: `inspect_network_topology` is QA, not a second inventory; merging it would fatten the list interface. Isolation stayed on list rows once; that leaked QA into inventory and is removed.

## Consequences

`list_roads` is gone from the catalog, the construction group, and HTTP. Callers rank roads with `list_networks` (`kind=road`, `sort=congestion|traffic_volume`) and low-voltage cables with `sort=load`. Do not keep a second utility-vs-road classification.
