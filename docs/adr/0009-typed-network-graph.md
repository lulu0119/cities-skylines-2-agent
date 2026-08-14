# One typed network graph behind list, demolish, and topology QA

Status: accepted

This ADR records the typed-network graph seam. It is not a second inventory taxonomy. Roads, water pipes, sewage pipes, and low-voltage cables are one typed Net (`Game.Net` edges). Auto-connect, `list_networks`, demolish, and topology QA all use `TypedNetworkKinds` from native prefab layers.

## Decision

Model-facing Net inventory is one tool: `list_networks`. It lists `Game.Net` edges — roads, water, sewage, low-voltage, including isolated components. It does not list buildings, transit routes, areas, notifications, or topology QA results. There is no `list_roads`, `list_pipes`, or `list_cables`. Traffic volume and congestion are fields and `sort=distance|traffic_volume|congestion` on road-kind rows, not a second inventory tool.

`inspect_network_topology` stays QA (what is wrong), distinct from `list_networks` (what is there). Isolated pipes and cables are listable and demolishable through native bulldoze; raw `Deleted` is not a cleanup path. `demolish` stays one write and accepts listed typed-network edges via native bulldoze. `build_road` stays the linear write.

## Considered Options

- **Separate `list_pipes` / `list_cables` with their own classifiers.** Rejected: the failure mode was duplicated classification, not missing tool names.
- **Keep `list_roads` as HTTP diagnostic like `find_placement`.** Rejected: unused tools are deleted; a leftover `/city/roads` is a second Net inventory.
- **Raw `Deleted` for utility cleanup.** Rejected: it skips node and lane cleanup and corrupts the graph ([ADR-0002](0002-native-validation.md)).
- **Merge topology QA into `list_networks`.** Rejected: `inspect_network_topology` is QA, not a second inventory; merging it would fatten the list interface.

## Consequences

`list_roads` is gone from the catalog, the construction group, and HTTP. Callers rank roads with `list_networks` (`kind=road`, `sort=congestion|traffic_volume`). Do not keep a second utility-vs-road classification.
