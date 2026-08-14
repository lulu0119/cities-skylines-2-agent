# Documentation

How agents maintain this tree. Formats for glossary and ADRs come from the **domain-modeling** skill; this file only says which home each kind of fact uses.

## Homes

| Fact | Home | Not here |
| --- | --- | --- |
| What a domain word means | [CONTEXT.md](../CONTEXT.md) | ADRs, open-work, README, system prompt copies |
| Why we chose a hard-to-reverse trade-off | [adr/NNNN-slug.md](./adr/) | Chat, ops audits, dated research |
| What is unfinished or awaiting a new-city gate | [open-work.md](./open-work.md) | ADR bodies, README, appending to frozen ops files |
| Evidence, surveys, session freezes | dated `research/` / `ops/` / `guide/` | Current authority after an ADR supersedes them |
| Player/dev entry | [README.md](../README.md) | Second copy of the product contract |

`docs/adr/` holds only sequential `NNNN-slug.md` files. Do not add dated combined specs there.

## When you learn something

1. A term crystallizes → update `CONTEXT.md` in the same change. Glossary only: what it **is**, plus `_Avoid_`. No algorithms.
2. A choice is hard to reverse, surprising without context, and beat a real alternative → next ADR number. One decision per file. A paragraph is enough. Link it from [README.md](./README.md) in this folder.
3. Work is not done, or code needs a new-city gate → edit `open-work.md` only. No session ids, token dumps, or recycle-bin paths.
4. A long-run audit or investigation → new dated `ops/` or `research/` note, linked from the index, with `Status: frozen` or `superseded`. Do not grow `open-work.md` into that note.

When an ADR ships, banner the dated research/ops it replaces. Do not rewrite the historical body.

## Pointers in always-on files

[AGENTS.md](../AGENTS.md) stays short: constraints and links. If a rule is needed only while writing docs, it belongs in this file, not in the root.
