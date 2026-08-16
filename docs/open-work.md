# Open work

Current inventory. Vocabulary: [CONTEXT.md](../CONTEXT.md). Decisions: [adr/](./adr/). How to update this tree: [AGENTS.md](./AGENTS.md). Frozen audits stay in dated `ops/` files and are not this list.

## Not implemented

Code still missing.

None.

## Awaiting live acceptance

Code exists; a previous save is not the final gate. Close the game before DLL redeploy. Mac cannot `dotnet build` without `CSII_TOOLPATH`; Windows compile is a gate before live acceptance.

- `wait_simulation` nested overview/problems digest; ledger injection gone; KV overall/median ≥ 90%.
- Auto-connect: road-carried water/sewage/LV attach as short perpendicular on matched lane; utilities work (Windows in-game).
- Specialized-industry loop from hub through extractor area to vehicles and production — not yet proven in a live city.
- Traffic governance as a product loop. The accepted-run intervention persisted, but traffic notifications stayed at 2 and the same-road congestion/volume aggregates worsened after one simulated hour.
