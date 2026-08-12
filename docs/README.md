# Docs index

Dated notes under `guide/` / `research/` / `ops/`. Frozen M1 smoke checklist lives in [`../archive/docs/`](../archive/docs/).

## Guide

| Doc | Topic |
| --- | --- |
| [2026-08-06-windows-onboarding.md](./guide/2026-08-06-windows-onboarding.md) | Windows setup, build/load `Mod/`, decisions, link to archived M1 |

## Research

| Doc | Topic |
| --- | --- |
| [2026-08-06-csharp-agent-runtimes.md](./research/2026-08-06-csharp-agent-runtimes.md) | C# agent stacks; provisional MEAI + hand loop |
| [2026-08-06-agent-runtime-gameface-requirements.md](./research/2026-08-06-agent-runtime-gameface-requirements.md) | apeira/xsai vs Gameface Web APIs |
| [2026-08-06-gameface-feature-support.md](./research/2026-08-06-gameface-feature-support.md) | Official Coherent support + CS2 empirical notes |
| [2026-08-06-research-in-game-ai-mods.md](./research/2026-08-06-research-in-game-ai-mods.md) | Other games’ in-game LLM mod patterns |
| [2026-08-06-agent-runtime-compression-observability.md](./research/2026-08-06-agent-runtime-compression-observability.md) | Long-run compression, tool-call observability, interleaved input (Apeira/Agents SDK/LangChain/SK refs) |
| [2026-08-10-cs2-mod-hot-reload.md](./research/2026-08-10-cs2-mod-hot-reload.md) | CS2 data/UI/C# hot-reload feasibility; stable host + replaceable policy architecture |
| [2026-08-11-tool-deepening-next-seams.md](./research/2026-08-11-tool-deepening-next-seams.md) | ECS prefab roles, road features vs replacement, and owner-linked operational-area seams |
| [2026-08-11-cs2-map-image-mod.md](./research/2026-08-11-cs2-map-image-mod.md) | CS2MapView / Carto map-image mods; open-source status and agent integration options |

## Ops

| Doc | Topic |
| --- | --- |
| [2026-08-06-windows-toolchain-pitfalls.md](./ops/2026-08-06-windows-toolchain-pitfalls.md) | Steam/Scoop/Unity/`f2c1`/UI template traps |
| [2026-08-06-in-game-agent-fixes-handoff.md](./ops/2026-08-06-in-game-agent-fixes-handoff.md) | Session handoff: UI Portal, compact, sim wait, perception caps, road ErrorType |
| [2026-08-07-chat-ui-debug-computer-use-handoff.md](./ops/2026-08-07-chat-ui-debug-computer-use-handoff.md) | Chat UI crashes/dupes/black-screen hypotheses, Windows-MCP + Gameface CDP, keep 548a1a instrumentation |
| [scripts/2026-08-07-gameface-cdp/](./ops/scripts/2026-08-07-gameface-cdp/) | CDP probe/send/check helpers for `-uiDeveloperMode` :9444 |
| [2026-08-07-sewage-handoff.md](./ops/2026-08-07-sewage-handoff.md) | Session handoff: agent sees sewage problem but never fixes it; `agent_advance_time` dispatch bug; proposed fixes |
| [2026-08-08-windows-mcp-game-debug-loop.md](./ops/2026-08-08-windows-mcp-game-debug-loop.md) | Repeatable hands-off Steam/launcher/Gameface control, black-screen diagnosis, and real-machine acceptance loop |
| [2026-08-09-10k-loop-task.md](./ops/2026-08-09-10k-loop-task.md) | Codex task brief: iterate launch/observe/diagnose/build until the in-game agent reaches 10k population |
| [2026-08-10-placement-utilities-handoff.md](./ops/2026-08-10-placement-utilities-handoff.md) | Completed 10k handoff: one-step place, auto-connect, zoning pipeline, list/buy tiles and cold acceptance evidence |
| [2026-08-10-gameplay-capability-backlog.md](./ops/2026-08-10-gameplay-capability-backlog.md) | Post-10k backlog: rectangle zoning, progression/dev tree, road hierarchy, specialized industry and editable landfill areas |
| [2026-08-12-tool-surface-audit-and-open-work.md](./ops/2026-08-12-tool-surface-audit-and-open-work.md) | Read-only audit: place/find tool surface, "candidate returns one site" design issue, and the authoritative open-work inventory |
