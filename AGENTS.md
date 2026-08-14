# AGENTS.md

Guidance for agents working in **cities-skylines-2-agent**. Vocabulary: [CONTEXT.md](CONTEXT.md). Decisions: [docs/adr/](docs/adr/). Current open work: [docs/open-work.md](docs/open-work.md). How to write docs: [docs/AGENTS.md](docs/AGENTS.md).

## Product shape

```text
Gameface UI (chat) ↔ Cohtml bindings ↔ C# Agent + ToolQueueSystem (UIUpdate)
 ↓
 Unity ECS / native tools
```

| Path | Role |
| --- | --- |
| `Mod/` | Shippable product (C# + `Mod/UI`) |
| `archive/` | Offline POCs + frozen M1 smoke — not the product root |
| `docs/` | Numbered ADRs, current [open-work.md](docs/open-work.md), dated evidence; how to write them: [docs/AGENTS.md](docs/AGENTS.md) |

When a domain term is used, match [CONTEXT.md](CONTEXT.md). When a seam or module is designed, use the **codebase-design** skill. When a durable choice is made, add or update a numbered ADR — do not leave it only in chat or an ops audit.

## Hard constraints

- API keys stay in settings or the environment, never in the repo.
- Hang UI on `GameBottomRight` + `Portal`, not bare `"Game"`.
- Real Windows + in-game load is the authority; `archive/` is historical.
- The product has not shipped. Prefer the correct foundation over compatibility: reject unknown on-disk shapes, do not write migrations, and do not add model-facing shims or aliases.
- Use native validation. No Anarchy, `force`, or collision bypass. Construction recovery belongs inside the write tool.
- Tools enqueue onto the simulation thread. Do not apply construction from the UI or chat thread.
- Model-facing writes: `place_building` for buildings, `build_road` for linear networks. Do not add preview-then-commit, a `role` argument on place, or silent grade-separated promotion.
- The player owns the clock. `wait_simulation` advances time then restores speed and pause. Do not force pause as the product runtime.
- Design it twice and pick the cleaner design. No unsolicited docs.

## Skills

When the task matches, read the skill before acting — not a menu to skip.

- Seam or module design → `codebase-design`
- A domain term crystallizes, or a durable choice needs an ADR → `domain-modeling`
- Something is broken, throwing, or slow → `diagnosing-bugs`
- Gathering sources into a dated research/ops note → `research`

## Commits

`type(scope): English subject` — e.g. `feat(mod): …`, `docs(adr): …`.

Complete, verify, commit, and push one work item at a time. Do not accumulate unrelated completed items. Preserve unrelated user changes already in the worktree.

## Docs

Follow [docs/AGENTS.md](docs/AGENTS.md). `CONTEXT.md` is glossary only. ADRs are numbered. Current inventory is `docs/open-work.md` only.
- Completing work that changes unfinished inventory **must** edit `docs/open-work.md` in the same change (or same session before done). Do not leave it for the user to remember.

## Verification

- C#: `cd Mod && dotnet build` (close the game before redeploying the DLL).
- UI-only: `cd Mod/UI && npm run build` (needs `CSII_USERDATAPATH`).
- Prefer targeted checks over inventing large test suites unless asked.
- After logic changes: deslop; use codebase-design language if a seam moved.
- Before claiming done: if unfinished inventory changed, `docs/open-work.md` is updated (not implemented vs awaiting live acceptance; delete passed gates).
