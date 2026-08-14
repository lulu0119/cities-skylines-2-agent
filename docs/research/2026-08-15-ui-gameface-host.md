# Chat UI host: Gameface, not the web

- **Date:** 2026-08-15
- **Status:** frozen
- **Question:** Must `Mod/UI` run in Cities: Skylines II Gameface? Is there a lighter Gameface host than launching the game?

## Short answer

The shippable UI is a CS2 Gameface module. **The web is not a host:** Chrome, Safari, Electron, and Storybook are Chromium, not Gameface, and they do not provide `cs2/*`.

| Question | Answer |
| --- | --- |
| Product UI | Gameface inside CS2. Webpack emits a `coui://` module with `cs2/*` and React as window externals. |
| Web / Electron / Storybook | **No.** |
| Lighter than the game, still Gameface | Coherent **Player** (`Player.exe`), from the Gameface SDK, not from CS2. Still no Colossal `cs2/ui`. |
| Ctrl+S inside CS2 | `webpack --watch` (production) writes the bundle; the game calls `View.Reload()` and remounts React. Not module HMR. |

## Why the web is out

`ChatPanel` imports `cs2/api` and `cs2/ui`. The stock template marks those, plus React and `cohtml/cohtml`, as window externals and sets `publicPath` to `coui://ui-mods/` ([`Mod/UI/webpack.config.js`](../../Mod/UI/webpack.config.js), [stock template](https://github.com/CitiesSkylinesModding/StockModTemplatesDiffer/blob/main/ui/webpack.config.js)). A browser has none of that. Gameface talks to native code through `engine`, not a browser chrome API ([javascript_native](https://docs.coherent-labs.com/cpp-gameface/integration/ui_scripting/javascript_native/)).

Gameface is an HTML/CSS/JS *subset*. The same page can look different in a traditional browser: default `display: flex` and `box-sizing: border-box`, `flex-shrink` default 0 ([htmlfeaturesupport](https://docs.coherent-labs.com/cpp-gameface/what_is_gfp/htmlfeaturesupport/)). This repo already hit Gameface-only failures (`ScrollController`, invalid `display`, missing emoji, `fetch`) that Chromium will not reproduce ([ops 2026-08-07](../ops/2026-08-07-chat-ui-debug-computer-use-handoff.md), [feature-support](./2026-08-06-gameface-feature-support.md)).

## CS2 watch vs Player

`npm run dev` is `webpack --watch` in production mode ([`Mod/UI/package.json`](../../Mod/UI/package.json), [`webpack.config.js`](../../Mod/UI/webpack.config.js)). `Colossal.UI.UILiveReload` then calls `View.Reload()` — “Reloads the current page in the view” ([hot-reload research](./2026-08-10-cs2-mod-hot-reload.md), [`cohtml::View`](https://docs.coherent-labs.com/cpp-gameface/api_reference/classes/classcohtml_1_1_view/)). Observed: `UI.log` “Reloading media 0” and React remount; chat state lives on `window.__cs2AgentChat` to survive. That skips a game restart. It is not in-place HMR.

Coherent’s light host is **Player**: a standalone Gameface window, HTTP + WebSockets, DevTools on 9444, no game engine ([Player](https://docs.coherent-labs.com/cpp-gameface/quick_start/player/player/), [Quick start](https://docs.coherent-labs.com/cpp-gameface/quick_start/quickstartguide_native/)). Their React template opens Player via `PLAYER_PATH` ([React support](https://docs.coherent-labs.com/cpp-gameface/content_development/reactsupport/)). CS2 does not ship Player. This repo’s Cohtml was 1.64.x; a current SDK Player can be newer. Player still lacks `cs2/ui` / `cs2/api`.

In-game inspector remains `-uiDeveloperMode` on `127.0.0.1:9444` ([CDP helpers](../ops/scripts/2026-08-07-gameface-cdp/README.md)).

## Source index

| Claim | Source |
| --- | --- |
| Mount is `GameBottomRight` + `Portal` | [`Mod/UI/src/index.tsx`](../../Mod/UI/src/index.tsx), [`chat-panel.tsx`](../../Mod/UI/src/mods/chat-panel.tsx) |
| Externals and `coui://` | [`Mod/UI/webpack.config.js`](../../Mod/UI/webpack.config.js) |
| Stock template matches | [StockModTemplatesDiffer UI webpack](https://github.com/CitiesSkylinesModding/StockModTemplatesDiffer/blob/main/ui/webpack.config.js) |
| Gameface ≠ browser | [htmlfeaturesupport](https://docs.coherent-labs.com/cpp-gameface/what_is_gfp/htmlfeaturesupport/) |
| Native bridge is `engine` | [javascript_native](https://docs.coherent-labs.com/cpp-gameface/integration/ui_scripting/javascript_native/) |
| Player is standalone Gameface | [Player](https://docs.coherent-labs.com/cpp-gameface/quick_start/player/player/) |
| `View.Reload` reloads the page | [cohtml::View](https://docs.coherent-labs.com/cpp-gameface/api_reference/classes/classcohtml_1_1_view/) |
| CS2 `dev` is production `webpack --watch` | [`Mod/UI/package.json`](../../Mod/UI/package.json) |
| Empirical Gameface traps | [ops 2026-08-07](../ops/2026-08-07-chat-ui-debug-computer-use-handoff.md) |
| In-game UI watch | [hot-reload research](./2026-08-10-cs2-mod-hot-reload.md) |
