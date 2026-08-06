# Gameface / CS2 UI feature support map

**Date:** 2026-08-06  
**Audience:** Gameface TS agent loop + React UI work in this repo.

## Important caveat

There is **no separate official “Gameface TypeScript feature list.”**  
TypeScript only compiles to JavaScript. Support is:

1. **Coherent Gameface** HTML / CSS / JS subset (version shipped inside *Cities: Skylines II*)
2. **CS2 game UI bindings** (`cs2/*`, `cohtml/cohtml`) on top of that runtime

CS2’s Gameface build may lag or diverge from the latest public Coherent docs. Treat Coherent tables as the **vendor baseline**, then **probe in-game** for anything networking / Streams / optional.

---

## Where the full official lists live (authoritative)

Coherent publishes support **tables** here (open these in a browser; they are the complete matrices):

| Table | URL |
| --- | --- |
| **Supported features index** | https://docs.coherent-labs.com/cpp-gameface/content_development/supported_features_tables/ |
| HTML elements | https://docs.coherent-labs.com/cpp-gameface/content_development/supported_features_tables/htmlelements/ |
| CSS properties | https://docs.coherent-labs.com/cpp-gameface/content_development/supported_features_tables/cssproperties/ |
| CSS selectors | https://docs.coherent-labs.com/cpp-gameface/content_development/supported_features_tables/cssselectors/ |
| JS DOM events | https://docs.coherent-labs.com/cpp-gameface/content_development/supported_features_tables/jsevents/ |
| SVG | https://docs.coherent-labs.com/cpp-gameface/content_development/supported_features_tables/svgsupport/ |
| Canvas | https://docs.coherent-labs.com/cpp-gameface/content_development/supported_features_tables/canvassupport/ |
| Browser differences overview | https://docs.coherent-labs.com/cpp-gameface/what_is_gfp/htmlfeaturesupport/ |
| JS DOM API (interfaces) | https://docs.coherent-labs.com/cpp-gameface/api_reference/modules/group___d_o_m___a_p_i/ |
| Native ↔ JS (`engine`) | https://docs.coherent-labs.com/cpp-gameface/integration/ui_scripting/javascript_native/ |
| React support | https://docs.coherent-labs.com/cpp-gameface/content_development/reactsupport/ |
| WebSockets (optional native hook) | https://docs.coherent-labs.com/cpp-gameface/integration/optional_features/websockets/ |
| XMLHttpRequest | https://docs.coherent-labs.com/cpp-gameface/api_reference/classes/interface_x_m_l_http_request/ |

Those CSS/HTML/SVG/events tables are hundreds of rows — **do not duplicate them in-repo**; link and filter for what you need.

---

## Runtime model (from Coherent docs)

| Topic | Vendor claim | Source |
| --- | --- | --- |
| JS VM | **V8** on all platforms | [javascript_native](https://docs.coherent-labs.com/cpp-gameface/integration/ui_scripting/javascript_native/) |
| Promises | **ECMAScript 6 Promises** supported | same |
| Layout | **Flexbox** is the real layout engine; default `display: flex` + `box-sizing: border-box` on elements | [htmlfeaturesupport](https://docs.coherent-labs.com/cpp-gameface/what_is_gfp/htmlfeaturesupport/) |
| Media | Always “screen”; write `@media (min-width: …)` not `@media screen and …` | same |
| Game ↔ JS | Through `engine` (`call` / events / bindings), not inventing a browser `chrome` API | [javascript_native](https://docs.coherent-labs.com/cpp-gameface/integration/ui_scripting/javascript_native/) |
| Networking (documented) | **XMLHttpRequest** is first-class; News Feed sample uses XHR | [News Feed](https://docs.coherent-labs.com/cpp-gameface/content_development/pages_guides/newsfeed_native/), XHR API page |
| WebSocket | Supported **if** host implements `OnCreateWebSocket` | [Web Sockets](https://docs.coherent-labs.com/cpp-gameface/integration/optional_features/websockets/) |
| React | Supported with Coherent CRA / webpack samples; official sample uses **`whatwg-fetch` polyfill** | [React Support](https://docs.coherent-labs.com/cpp-gameface/content_development/reactsupport/) |

### CSS general (from CSS properties table preface)

- `!important` — yes  
- CSS variables — yes, **not** in `@keyframes`; **no** fallback values  
- `calc()` — yes, **not** in `@keyframes`; **no** mixing `%` with other units (e.g. `50% - 20px`)  

Full property YES/PARTIAL/NO matrix: CSS properties URL above.

### Selectors (high level)

- Type / class / id / universal / attribute — yes  
- Combinators (`>`, `+`, `~`, descendant) — **conditional** (`EnableComplexCSSSelectorsStyling`)  
- Common pseudos (`:hover`, `:focus`, `:active`, `:root`, `::before`/`::after`) — largely yes with notes  

Full matrix: CSS selectors URL above.

---

## JavaScript DOM API surface (vendor interface list)

Published under [JavaScript DOM API](https://docs.coherent-labs.com/cpp-gameface/api_reference/modules/group___d_o_m___a_p_i/). Presence of an **interface** means Gameface documents that binding; it is **not** a guarantee of full WHATWG parity.

Includes (non-exhaustive of every method):  
`Window`, `Document`, `Element`, `Node`, `HTMLElement` (+ common HTML* elements), `XMLHttpRequest`, `CanvasRenderingContext2D`, `HTMLCanvasElement`, SVG* types, `MutationObserver*`, `Animation` / `CSSAnimation`, `Touch*` / `MouseEvent` / `KeyboardEvent`, `Storage`, `History`, `Navigator`, `Console`, `CustomElementRegistry`, `PromiseRejectionEvent`, etc.

**Notable for agent work:** documented network path is **XHR**. Streams / Fetch are **not** listed as first-class DOM API interfaces on that module index. CS2 may still expose a partial `fetch` (see empirical section).

---

## CS2-specific layer (this repo’s typings)

Under `Mod/UI/types/` (official `create-csii-ui-mod` stubs). These are **Colossal UI modules**, not vanilla Gameface:

| Module | Role |
| --- | --- |
| `cs2/modding` | `ModRegistrar`, `moduleRegistry.append/extend/override`, append targets (`Menu`, `Game`, `GameTopLeft`, `GameTopRight`, `GameBottomRight`, `UniversalModMenu`, `Editor`) |
| `cs2/api` | `bindValue` / `bindTrigger` / `call` / `useValue` — C# ↔ UI |
| `cs2/ui` | `Button`, `Panel`, `Portal`, `FloatingButton`, dialogs, tooltips, scroll helpers, … |
| `cs2/bindings` | Extra binding helpers |
| `cs2/l10n` | Localization |
| `cs2/input` | Input actions |
| `cs2/utils` | Utilities |
| `cohtml/cohtml` | `engine` (whenReady, on, call, trigger, …) |

Webpack externals treat these as provided by the game UI host (`Mod/UI/webpack.config.js`).

---

## Empirical CS2 Gameface (this project, 2026-08-06)

| Probe | Result | Notes |
| --- | --- | --- |
| `typeof ReadableStream` | **`"undefined"`** | Matches Coherent DOM API omission; see agent-runtime matrix |
| Non-stream `fetch` → mock `:8787` | **Not finally pinned** | Smoke panel archived; see archive M1 doc |
| C# `HttpClient` HTTPS | **OK** (401 with fake key) | TLS outside Gameface |
| UIUpdate queue while paused | **OK** | Tool execution path |

**Agent / apeira / xsai dependency matrix (audited against package source + Gameface docs):**  
[agent-runtime-gameface-requirements](./2026-08-06-agent-runtime-gameface-requirements.md)  
Important: stock `@xsai/generate-text` still hard-requires `Response.body instanceof ReadableStream` via `@xsai/shared` `responseCatch` — not only apeira streaming.

Project CSS note: avoid `gap` in Gameface layouts (use margins) — observed/community + prior smoke UI work.

---

## Practical checklist for Gameface TS agent loop

| Need | Assume | Action |
| --- | --- | --- |
| React 18 UI | Yes (CS2 ships React) | Use `cs2/ui` + Portal for floating panels |
| C# tools | Bindings, not HTTP | `bindTrigger` / `call` → UIUpdate queue |
| LLM HTTP non-stream | Probe | Prefer `fetch`+`text()` **or** XHR; confirm 3.1 fetch |
| LLM SSE via `getReader()` | **No** (today) | Use `@xsai/generate-text` or XHR `onprogress` experiments |
| apeira stream-first | Avoid in-game | Keep for browser POC only |
| Full CSS/HTML matrix | Use Coherent tables | Don’t invent |

### Runtime probe snippet (paste in smoke / DevTools)

With `-uiDeveloperMode`, evaluate:

```js
[
  typeof fetch,
  typeof ReadableStream,
  typeof XMLHttpRequest,
  typeof WebSocket,
  typeof EventSource,
  typeof localStorage,
  typeof Worker,
].join(" | ");
```

Record results back into this file when you have a CS2 build string.

---

## Source index

| Claim | Source |
| --- | --- |
| Official support tables hub | https://docs.coherent-labs.com/cpp-gameface/content_development/supported_features_tables/ |
| Browser differences | https://docs.coherent-labs.com/cpp-gameface/what_is_gfp/htmlfeaturesupport/ |
| DOM API module | https://docs.coherent-labs.com/cpp-gameface/api_reference/modules/group___d_o_m___a_p_i/ |
| V8 + ES6 Promises + engine | https://docs.coherent-labs.com/cpp-gameface/integration/ui_scripting/javascript_native/ |
| React + whatwg-fetch sample | https://docs.coherent-labs.com/cpp-gameface/content_development/reactsupport/ |
| XHR / News Feed | https://docs.coherent-labs.com/cpp-gameface/content_development/pages_guides/newsfeed_native/ |
| WebSocket optional | https://docs.coherent-labs.com/cpp-gameface/integration/optional_features/websockets/ |
| CS2 modules | `Mod/UI/types/*.d.ts` |
| CS2 ReadableStream | In-game smoke panel / `Logs/UI.log` 2026-08-06 |
