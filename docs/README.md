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

## Ops

| Doc | Topic |
| --- | --- |
| [2026-08-06-windows-toolchain-pitfalls.md](./ops/2026-08-06-windows-toolchain-pitfalls.md) | Steam/Scoop/Unity/`f2c1`/UI template traps |
| [2026-08-06-in-game-agent-fixes-handoff.md](./ops/2026-08-06-in-game-agent-fixes-handoff.md) | Session handoff: UI Portal, compact, sim wait, perception caps, road ErrorType |
| [2026-08-07-chat-ui-debug-computer-use-handoff.md](./ops/2026-08-07-chat-ui-debug-computer-use-handoff.md) | Chat UI crashes/dupes/black-screen hypotheses, Windows-MCP + Gameface CDP, keep 548a1a instrumentation |
| [scripts/2026-08-07-gameface-cdp/](./ops/scripts/2026-08-07-gameface-cdp/) | CDP probe/send/check helpers for `-uiDeveloperMode` :9444 |
| [2026-08-07-sewage-handoff.md](./ops/2026-08-07-sewage-handoff.md) | Session handoff: agent sees sewage problem but never fixes it; `agent_advance_time` dispatch bug; proposed fixes |
