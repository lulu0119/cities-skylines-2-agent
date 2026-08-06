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

Follow [A Philosophy of Software Design](https://github.com/alysivji/notes/blob/main/software-engineering/philosophy_of_software_design.md) (Ousterhout). Goal: reduce complexity.

- **Complexity** — anything in the structure that makes the system hard to understand or modify (change amplification, cognitive load, unknown unknowns). It accumulates in small chunks; don’t shrug off “a little” complexity per change.
- **Working code isn’t enough** — prefer a strategic mindset (invest in clean design) over a purely tactical “get it working fast” mindset.

### Deep modules

- A module = **interface** (everything a caller must know to use it correctly — formal *and* informal) + **implementation** (what fulfills that promise).
- Depth = benefit / cost: lots of functionality behind a **simple** interface. Best modules: interface much simpler than implementation.
- Abstraction can fail two ways: expose unimportant details (interface too fat), or omit important details (obscurity).
- Avoid **classitis**: many tiny classes that are each “simple” but whose *accumulated* interfaces explode complexity.

### Information hiding

- Hide design decisions in the implementation so they do not appear in the interface; that lowers cognitive load and localizes change.
- **`private` is not information hiding** by itself — hiding means callers (and other modules) do not need the knowledge at all.
- **Leakage** = the same knowledge reflected in multiple modules (even when not in a public signature, e.g. two places assuming the same format). Merge tightly coupled leakers, or pull the shared knowledge into one module.
- Prefer modules organized around *what knowledge they own*, not around the time-order of steps (**temporal decomposition**).
- Make the common case simple; do **not** hide information that callers truly need — expose what must be known, hide the rest.

### Define errors out of existence

- Exceptions (uncommon conditions that divert normal control flow) are a major source of complexity; handling them is harder than normal-case code and often breeds secondary exceptions.
- Too many exceptions = over-defensive style that **punts** to every caller; exceptions are part of the interface — keep them few. Reduce how many *places* must handle them.
- Prefer designing the API so the error case **does not exist** for callers (or is rare).
- **Mask** at a lower level when higher layers need not know (pulls complexity down; deepens the lower module).
- **Aggregate**: one handler for many exceptions (e.g. abort current request, clean up, continue) rather than distinct handlers everywhere.
- Some errors are not worth handling — fail fast with diagnostics rather than elaborate recovery.
- Design **special cases out of existence** so the normal path covers them without extra `if`s.
- Do not take this too far: hide what is unimportant; **expose** what the caller must know.

### Comments

- If callers must read the method body to use it, there is **no abstraction** — comments carry design knowledge the code cannot.
- Comment what is **not obvious** from the code (don’t parrot names). Separate **interface** comments from **implementation** comments.
- Lower-level comments add **precision**; higher-level comments add **intuition** (what/why). Implementation comments: what and why, not line-by-line how.

### Other principles

- **General-purpose deeper than special-purpose** — simplest interface that covers current needs beats a pile of narrow methods.
- **Different layer, different abstraction** — adjacent layers should not repeat the same abstraction; avoid pass-through methods/variables that add no value.
- **Pull complexity downward** — put complexity in lower-level modules when that simplifies higher-level code; don’t shove hard choices onto every caller via config knobs.
- **Together vs apart** — merge when it simplifies the interface or removes duplication; separate general-purpose from special-purpose code.
- **Design it twice** — try more than one design and pick the cleaner one; don’t ship the first idea by default.
- **Names** — precise, consistent, create a clear image; inconsistency and vagueness add obscurity.
- **Obviousness** — readers should quickly see how the code works and what a change requires.
- **Consistency** — same meaning, same style, same patterns throughout.
- **Modifying existing code** — stay strategic: improve design while changing behavior; keep comments near the code they describe (not only in the commit log).

For seam/module design vocabulary in this repo, use the **codebase-design** skill.

## Recommended skills

- `research`
- `diagnosing-bugs`
- `codebase-design`

## Reference projects

- https://github.com/shinohara-rin/airicraft
- https://github.com/mayor-modder/Cities2-MCP
- https://github.com/shinohara-rin/action-plan-advisor
- https://github.com/moeru-ai/apeira

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
