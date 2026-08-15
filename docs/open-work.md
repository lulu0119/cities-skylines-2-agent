# Open work

Current inventory. Vocabulary: [CONTEXT.md](../CONTEXT.md). Decisions: [adr/](./adr/). How to update this tree: [AGENTS.md](./AGENTS.md). Frozen audits stay in dated `ops/` files and are not this list.

## Not implemented

Code still missing.

- Growable auto-demolish on build_road/place_building — decompile-verify native behavior; implement corridor clearance only if native rejects.

## Awaiting live acceptance

Code exists; a previous save is not the final gate. Prefer a new city unless marked existing-city (this playthrough empty→12.8k is enough — do not demand a third empty city). Close the game before DLL redeploy. Mac cannot `dotnet build` without `CSII_TOOLPATH`; Windows compile is a gate before live acceptance.

- (existing-city) `wait_simulation` nested overview/problems digest; ledger injection gone; KV overall/median ≥ 90%.
- (existing-city) Tool surface persistent from turn 1 (no groups / `enable_group`); schema stable.
- (existing-city) Road topology QA: 32 m `too_close_junctions` + `near_miss`; no `short_stub` length findings.
- (existing-city) Auto-connect: road-carried water/sewage/LV attach as short perpendicular on matched lane; utilities work (Windows in-game).
- Development-node purchase and landfill expansion across save/reload.
- Specialized-industry loop from hub through extractor area to vehicles and production — not yet proven in a live city.
- Traffic governance as a product loop. The mayor skill and tools exist; the live loop is unproven.
- Agent session only in `GameMode.Game`; main menu cannot Send; dispose the session on leave Game.
- Notifications: optional spatial filter on details.
