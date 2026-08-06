# Windows onboarding (guide)

**Date:** 2026-08-06  
**Repo:** `cities-skylines-2-agent` (GitHub：lulu0119/cities-skylines-2-agent)

## Goal

Pure in-game AI mayor mod: Gameface chat UI + API key in mod settings + C# tools on the simulation thread. Distributed via Paradox Mods; no external agent process.

## Current product tree

| Path | Role |
| --- | --- |
| [`Mod/`](../../Mod/) | Shippable mod — `ToolQueueSystem` (UIUpdate) + chat shell (`GameBottomRight` + `Portal`) |
| [`archive/`](../../archive/) | Offline POCs (`web/`, `mock/`, `cs/ModHost`) + frozen [M1 smoke](../../archive/docs/2026-08-06-m1-smoke.md) |
| [`docs/`](../README.md) | Guide / research / ops (this tree) |

## Locked / provisional decisions

- Agent loop (provisional): **C# MEAI `IChatClient` + hand-rolled ReAct / function-calling** — not Gameface apeira/xsai, not Semantic Kernel / Agent Framework first. See [csharp-agent-runtimes](../research/2026-08-06-csharp-agent-runtimes.md).
- Why not apeira in UI: Gameface has no `ReadableStream`; stock `@xsai/shared` still requires it. See [agent-runtime-gameface-requirements](../research/2026-08-06-agent-runtime-gameface-requirements.md).
- Tools always enqueue to the sim main thread (`UIUpdate` / `ToolUpdate`); pause-first.
- API keys never in the repo.
- Do not append UI to bare `"Game"` (collides with `-developerMode` F/S/H/Q strip); use `GameBottomRight` + `Portal`.

## Environment

- Windows 10/11, Steam CS2, modding toolchain / developer mode.
- .NET SDK 8+ (mod post-processor also needs **.NET 6 runtime**).
- Node 20+ for `Mod/UI`.
- Toolchain traps: [windows-toolchain-pitfalls](../ops/2026-08-06-windows-toolchain-pitfalls.md).
- Gameface capability notes: [gameface-feature-support](../research/2026-08-06-gameface-feature-support.md).

## Build / load mod

```bash
cd Mod
dotnet build
```

Deploy target: `%LocalLow%\Colossal Order\Cities Skylines II\Mods\CitiesSkylines2Agent\`  
UI-only: `cd Mod/UI && npm run build` (needs `CSII_USERDATAPATH`). Hot reload: `-uiDeveloperMode` + `npm run dev`.

Enable **CitiesSkylines2Agent** in-game, enter a save — chat shell mounts bottom-right (not the pink F/S/H/Q strip).

## Archived M1 smoke

Procedure and results are frozen in [archive/docs/2026-08-06-m1-smoke.md](../../archive/docs/2026-08-06-m1-smoke.md): **3.2 HTTPS ✅**, **3.3 UIUpdate queue ✅**, **3.1 stream ❌** (`ReadableStream` undefined). Offline POC replay: `cd archive/mock && node server.mjs`, etc. (see [archive/README](../../archive/README.md)).

## Next

Wire C# `IChatClient` + tool loop to the chat shell; grow tools on `ToolQueueSystem`.
