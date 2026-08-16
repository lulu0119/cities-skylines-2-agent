# Docs index

Vocabulary: [`CONTEXT.md`](../CONTEXT.md). Current open work: [`open-work.md`](./open-work.md). How agents write this tree: [`AGENTS.md`](./AGENTS.md). Dated notes under `guide/` / `research/` / `ops/` are evidence or history; accepted decisions live under `adr/` with sequential numbers. Frozen M1 smoke is in [`../archive/docs/`](../archive/docs/).

## ADR

| Doc | Decision |
| --- | --- |
| [0001-in-process-meai-loop.md](./adr/0001-in-process-meai-loop.md) | In-process MEAI loop; not apeira, SK, MAF, or an external process |
| [0002-native-validation.md](./adr/0002-native-validation.md) | Ordinary native validation; no Anarchy or `force` |
| [0003-one-step-building-placement.md](./adr/0003-one-step-building-placement.md) | `place_building` is the only model-facing building write |
| [0004-linear-networks.md](./adr/0004-linear-networks.md) | `build_road`; ground vs grade-separated; no silent promotion |
| [0005-player-permissions.md](./adr/0005-player-permissions.md) | Demolish / progression / visual / development tools are settings |
| [0006-budgeted-local-map.md](./adr/0006-budgeted-local-map.md) | `terrain` returns `LOCAL_MAP`, not a raw grid |
| [0007-session-lifecycle.md](./adr/0007-session-lifecycle.md) | Session follows the loaded city; data under `ModsData` |
| [0008-context-budget-auto-custom.md](./adr/0008-context-budget-auto-custom.md) | Auto from model name; Custom from the player setting |
| [0009-typed-network-graph.md](./adr/0009-typed-network-graph.md) | One typed network behind list, demolish, and topology QA |
| [0010-native-transit-lines.md](./adr/0010-native-transit-lines.md) | Transit lines via Route Tool apply; stops are not `place_building` |
| [0011-specialized-industry-hub-identity.md](./adr/0011-specialized-industry-hub-identity.md) | Specialized-industry role follows declared extractor Operational areas |

## Guide

| Doc | Topic |
| --- | --- |
| [2026-08-06-windows-onboarding.md](./guide/2026-08-06-windows-onboarding.md) | Windows setup, build/load `Mod/` |

## Research

| Doc | Topic |
| --- | --- |
| [2026-08-06-csharp-agent-runtimes.md](./research/2026-08-06-csharp-agent-runtimes.md) | C# agent stacks (superseded by [0001](./adr/0001-in-process-meai-loop.md)) |
| [2026-08-06-agent-runtime-gameface-requirements.md](./research/2026-08-06-agent-runtime-gameface-requirements.md) | apeira/xsai vs Gameface Web APIs |
| [2026-08-06-gameface-feature-support.md](./research/2026-08-06-gameface-feature-support.md) | Official Coherent support + CS2 empirical notes |
| [2026-08-06-research-in-game-ai-mods.md](./research/2026-08-06-research-in-game-ai-mods.md) | Other games’ in-game LLM mod patterns |
| [2026-08-06-agent-runtime-compression-observability.md](./research/2026-08-06-agent-runtime-compression-observability.md) | Long-run compression and tool-call observability |
| [2026-08-10-cs2-mod-hot-reload.md](./research/2026-08-10-cs2-mod-hot-reload.md) | CS2 data/UI/C# hot-reload feasibility |
| [2026-08-11-tool-deepening-next-seams.md](./research/2026-08-11-tool-deepening-next-seams.md) | Prefab roles, road features vs replacement, operational areas (partially superseded) |
| [2026-08-11-cs2-map-image-mod.md](./research/2026-08-11-cs2-map-image-mod.md) | CS2MapView / Carto map-image options |
| [2026-08-12-cs2-sewage-outlet-placement-rules.md](./research/2026-08-12-cs2-sewage-outlet-placement-rules.md) | Shoreline, road, and pipe requirements for pumps and outlets |
| [2026-08-13-cities-skylines1-agent-skill-road-planning.md](./research/2026-08-13-cities-skylines1-agent-skill-road-planning.md) | CS1 road-topology QA ideas transferable to CS2 |
| [2026-08-13-compact-local-map-and-route-anchoring.md](./research/2026-08-13-compact-local-map-and-route-anchoring.md) | `LOCAL_MAP` design; auto route anchoring out of scope ([0004](./adr/0004-linear-networks.md), [0006](./adr/0006-budgeted-local-map.md)) |
| [2026-08-14-cs2-multi-instance.md](./research/2026-08-14-cs2-multi-instance.md) | Two CS2 processes on one Windows PC (not supported) |
| [2026-08-15-ui-gameface-host.md](./research/2026-08-15-ui-gameface-host.md) | Chat UI is Gameface-hosted; the web is not a host |

## Ops

| Doc | Topic |
| --- | --- |
| [2026-08-16-20k-live-acceptance.md](./ops/2026-08-16-20k-live-acceptance.md) | In-progress 0→20k Windows live-acceptance evidence ledger |
| [2026-08-15-windows-game-dll-handoff.md](./ops/2026-08-15-windows-game-dll-handoff.md) | Frozen `Game.dll` ILSpy paste-back (LV electricity + 8-step cap) |
| [2026-08-12-tool-surface-audit-and-open-work.md](./ops/2026-08-12-tool-surface-audit-and-open-work.md) | Frozen audit (not current open work) |
| [2026-08-10-gameplay-capability-backlog.md](./ops/2026-08-10-gameplay-capability-backlog.md) | Superseded post-10k backlog |
| [2026-08-10-placement-utilities-handoff.md](./ops/2026-08-10-placement-utilities-handoff.md) | 10k handoff evidence |
| [2026-08-09-10k-loop-task.md](./ops/2026-08-09-10k-loop-task.md) | Historical 10k task brief |
| [2026-08-08-windows-mcp-game-debug-loop.md](./ops/2026-08-08-windows-mcp-game-debug-loop.md) | Hands-off Steam/Gameface control loop |
| [2026-08-07-sewage-handoff.md](./ops/2026-08-07-sewage-handoff.md) | Historical sewage-session handoff |
| [2026-08-07-chat-ui-debug-computer-use-handoff.md](./ops/2026-08-07-chat-ui-debug-computer-use-handoff.md) | Chat UI / Gameface CDP handoff |
| [scripts/2026-08-07-gameface-cdp/](./ops/scripts/2026-08-07-gameface-cdp/) | CDP probe helpers for `-uiDeveloperMode` :9444 |
| [2026-08-06-in-game-agent-fixes-handoff.md](./ops/2026-08-06-in-game-agent-fixes-handoff.md) | Early in-game agent handoff |
| [2026-08-06-windows-toolchain-pitfalls.md](./ops/2026-08-06-windows-toolchain-pitfalls.md) | Steam/Scoop/Unity/`f2c1`/UI template traps |
