# Archive — offline POCs (not the shippable mod)

Moved out of the main tree after M1 Windows smoke (2026-08-06).  
Active product code lives in [`../Mod/`](../Mod/).

| Path | What it was |
| --- | --- |
| [`web/`](./web/) | Browser React + `@apeira/core` agent POC + Playwright e2e |
| [`mock/`](./mock/) | Zero-dep OpenAI-compatible mock LLM (`:8787`) |
| [`cs/ModHost/`](./cs/ModHost/) | C# OpenAI.NET tool-loop POC (`net10.0` / `net472` compile check) |
| [`docs/`](./docs/) | Archived M1 smoke procedure & results |

Run POCs from this folder if needed, e.g. `cd archive/mock && node server.mjs`.
