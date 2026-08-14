# Open work

Current inventory. Vocabulary: [CONTEXT.md](../CONTEXT.md). Decisions: [adr/](./adr/). How to update this tree: [AGENTS.md](./AGENTS.md). Frozen audits stay in dated `ops/` files and are not this list.

## Build

- Context budget settings: Auto / Custom on the settings page. Auto infers the window from the model name; Custom uses the player setting and must win over the profile. Today named-model profiles already behave like Auto; hidden `WindowTokens=200000` is only the unknown-model fallback, so Custom is not implemented. [ADR-0008](./adr/0008-context-budget-auto-custom.md)
- KV cache observability: record cached-input, reasoning, and additional counts, plus per-turn coverage. Missing fields stay unknown, never zero.
- Problem ledger: dedupe, duration, escalation, and resolved lifecycle. Today there is only the system prompt and the Agent polling notifications.
- Unified network query and cleanup: roads, pipes, cables, isolated components. Isolated utilities cannot be listed or demolished.
- Road topology QA: near-miss, unnoded crossing, too-close junctions, surprise short stubs, isolated roads.
- Landfill expansion toward a near-circle minus obstacles. Current planner is still an ~110° sector.
- Facility upgrades: `list_facility_upgrades` / `set_facility_upgrade`.
- Transit stops and lines.
- Specialized-industry loop from hub through extractor area to vehicles and production — not yet proven in a live city.
- Traffic governance as a product loop. The mayor skill already asks for a road hierarchy; without a problem ledger and topology QA this is not done.

## Live acceptance (new city)

Code exists; do not treat a previous save as the final gate.

- Building placement for water pump, sewage outlet, wind turbine, water tower, landfill, and ordinary RequireRoad buildings.
- Auto-connect choosing matching low-voltage / fresh-water / sewage networks only.
- Whether the Agent actually consumes `LOCAL_MAP`.
- Ground-road water/slope rejection and explicit grade-separated mode (`6d8d099`).
- Development-node purchase and landfill expansion across save/reload.
