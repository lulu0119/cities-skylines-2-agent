# Open work

Current inventory. Vocabulary: [CONTEXT.md](../CONTEXT.md). Decisions: [adr/](./adr/). How to update this tree: [AGENTS.md](./AGENTS.md). Frozen audits stay in dated `ops/` files and are not this list.

## Awaiting live acceptance (new city)

Code exists; do not treat a previous save as the final gate. New city only; close the game before DLL redeploy.

- Building placement for water pump, sewage outlet, wind turbine, water tower, landfill, and ordinary RequireRoad buildings.
- Auto-connect choosing matching low-voltage / fresh-water / sewage networks only.
- Whether the Agent actually consumes `LOCAL_MAP`.
- Ground-road water/slope rejection and explicit grade-separated mode.
- Development-node purchase and landfill expansion across save/reload.
- Specialized-industry loop from hub through extractor area to vehicles and production — not yet proven in a live city.
- Traffic governance as a product loop. The mayor skill and tools exist; the live loop is unproven.
