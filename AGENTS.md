# AGENTS.md

Guidance for agents working in **cities-skylines-2-agent**: an in-game AI mayor mod for Cities: Skylines II (Gameface React UI + C# tools). Players install via Paradox Mods and paste an API key — no external agent process.

## Product shape

```text
Gameface UI (chat)  ↔  Cohtml bindings  ↔  C# agent loop + ToolQueueSystem (UIUpdate)
                                              ↓
                                    Unity ECS / native tools
```

| Path | Role |
| --- | --- |
| `Mod/` | Shippable product (C# + `Mod/UI`) |
| `archive/` | Offline POCs + frozen M1 smoke — do not treat as the product root |
| `docs/` | Dated notes under `guide/` / `research/` / `ops/` — see `docs/README.md` |

**Provisional agent loop:** C# `IChatClient` (MEAI) + hand-rolled function-calling / ReAct. **Not** Gameface apeira/xsai, Semantic Kernel, or Agent Framework first. Tools always enqueue to the simulation main thread; pause-first. Details: `docs/research/2026-08-06-csharp-agent-runtimes.md`.

**Hard constraints**

- API keys never enter the repo (settings / env only).
- Do not append UI to bare `"Game"` (collides with `-developerMode` F/S/H/Q); use `GameBottomRight` + `Portal`.
- Real Windows + in-game load is the authority; Mac/browser POCs in `archive/` are historical.
- Prefer smallest correct diff; no drive-by refactors or unsolicited docs.

## Design philosophy

Work from [A Philosophy of Software Design](https://github.com/alysivji/notes/blob/main/software-engineering/philosophy_of_software_design.md) (Ousterhout). Reduce complexity; working code is not enough.

| Principle | Practice here |
| --- | --- |
| **Deep modules** | Small interface, lots of behavior behind it. Prefer one `Enqueue(Action)` / one chat→agent entry over many smoke-era special APIs. |
| **Information hiding** | Hide Mono/Unity/UIUpdate/tool-pipeline details inside tool & queue modules; callers see intents (“build road”), not frame state machines. |
| **Different layer, different abstraction** | UI = messages & bindings; agent = turns & tools; queue = “run on sim thread”; game tools = ECS/native apply. No pass-through wrappers that only rename. |
| **Pull complexity downward** | Absorb TLS, retries, paused-queue, and tool errors inside C# modules so the chat UI stays thin. |
| **Define errors out of existence** | Prefer APIs that cannot be misused (e.g. all tool work must go through the queue) over scattering try/catch and special cases. |
| **Design it twice** | For new seams (agent loop, tool surface, settings), sketch at least two shapes before coding. |
| **Obvious + consistent** | Match existing `Mod/` naming and Gameface patterns; comments for *why* and non-obvious invariants, not narration. |
| **Strategic change** | When touching code, leave the module deeper or clearer — not a tactical patch that leaks another special case. |

Use the **codebase-design** skill vocabulary when designing seams: *module*, *interface*, *depth*, *seam*, *adapter*, *leverage*, *locality*. One adapter = hypothetical seam; two adapters = real seam.

## Recommended skills

Use these eagerly when the task matches:

| Skill | When |
| --- | --- |
| **research** | CS2 / Paradox / Coherent / NuGet / reference-repo facts; write dated notes under `docs/research/` (or `guide/` / `ops/`) with primary-source citations. |
| **diagnosing-bugs** | In-game failures, UI bind issues, queue/pause bugs, HTTPS/TLS, toolchain — build a tight feedback loop first (`Player.log`, `Logs/`, deploy + reload). |
| **codebase-design** | New modules or reshaping agent / tools / UI↔C# seams; deepen before adding surface area. |

## Reference projects

Study these; **copy patterns, not whole stacks**. Cite them in design notes when you adopt an idea.

| Project | Steal |
| --- | --- |
| [shinohara-rin/airicraft](https://github.com/shinohara-rin/airicraft) | Pure in-game agent architecture (mod process owns the loop + tools); packaging and “agent in the game” product shape. |
| [mayor-modder/Cities2-MCP](https://github.com/mayor-modder/Cities2-MCP) | CS2 bridge: `UIUpdate` / tool queue, paused simulation, native tool apply pipeline (Apache-2.0 — reuse carefully with attribution). |
| [shinohara-rin/action-plan-advisor](https://github.com/shinohara-rin/action-plan-advisor) | Planning / action-advice structure for multi-step goals (how to decompose mayor intents without bloating the UI). |
| [moeru-ai/apeira](https://github.com/moeru-ai/apeira) | Turn / tool / event lifecycle for a small agent runtime (shape to mirror in C#, not a dependency). |

In-repo POC analogues: `archive/cs/ModHost` (C# tool loop), `archive/web` + `archive/mock` (browser-only).

## Commits

`type(scope): English subject` — e.g. `feat(mod): …`, `docs(guide): …`.

## Docs

- New research/ops/guide notes: `docs/<bucket>/YYYY-MM-DD-<slug>.md`, then link from `docs/README.md`.
- Do not resurrect live M1 click-checklists; historical procedure stays in `archive/docs/`.
- Prefer updating the relevant dated note or README over scattering duplicate status.

## Verification

- C#: `cd Mod && dotnet build` (close the game before redeploying the DLL).
- UI-only: `cd Mod/UI && npm run build` (needs `CSII_USERDATAPATH`).
- Prefer targeted checks over inventing large test suites unless asked.
- After logic changes: deslop; use codebase-design language if a seam moved.
