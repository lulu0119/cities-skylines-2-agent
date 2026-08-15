# Open work

Current inventory. Vocabulary: [CONTEXT.md](../CONTEXT.md). Decisions: [adr/](./adr/). How to update this tree: [AGENTS.md](./AGENTS.md). Frozen audits stay in dated `ops/` files and are not this list.

## Not implemented

Code still missing.

## Awaiting live acceptance (new city)

Code exists; do not treat a previous save as the final gate. New city only; close the game before DLL redeploy. Mac cannot `dotnet build` without `CSII_TOOLPATH`; Windows compile is a gate before live acceptance.

- Building placement for water pump, sewage outlet, wind turbine, water tower, landfill, and ordinary RequireRoad buildings.
- Auto-connect choosing matching low-voltage / fresh-water / sewage networks only.
- Whether the Agent actually consumes `LOCAL_MAP`.
- Ground-road water/slope rejection and explicit grade-separated mode.
- Development-node purchase and landfill expansion across save/reload.
- Specialized-industry loop from hub through extractor area to vehicles and production — not yet proven in a live city.
- Traffic governance as a product loop. The mayor skill and tools exist; the live loop is unproven.
- Agent session only in `GameMode.Game`; main menu cannot Send; dispose the session on leave Game.
- `wait_simulation` success payload carries city overview; `city_overview` / `game_state` off the model-facing surface; opening turn relies on wait.
- `list_networks` requires `kind`; list rows carry zero topology; road traffic / low-voltage electricity{flow,capacity,bottleneck} with `sort=load` / water·sewage geometry; topology QA requires `kind` and reports isolated components for water / sewage / low-voltage / road.
- Default list limit 16 / hard max 64; `list_tiles` defaults to owned with no item cap.
- Renames on the model-facing surface: `local_map`, `probe_cell_layer`, `list_zone_types`, `count_zone_cells`, `list_prefabs`.
- Notifications: citywide counts plus optional spatial filter on details.
- Dead tools gone from the model-facing surface: `ping`, `list_objects`, `find_placement`, `find_infrastructure_candidate`, `upgrade_road`, districts.
