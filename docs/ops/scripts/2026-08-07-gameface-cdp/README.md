# Gameface CDP helpers (session 548a1a)

Require Cities: Skylines II running with Steam launch option `-uiDeveloperMode` so Cohtml listens on `http://127.0.0.1:9444`.

```powershell
mkdir $env:TEMP\cdp-ws -Force | Out-Null
Push-Location $env:TEMP\cdp-ws
npm init -y | Out-Null
npm i ws --silent
Pop-Location
$env:NODE_PATH = "$env:TEMP\cdp-ws\node_modules"

node docs/ops/scripts/2026-08-07-gameface-cdp/cdp-probe.mjs
node docs/ops/scripts/2026-08-07-gameface-cdp/cdp-send.mjs
node docs/ops/scripts/2026-08-07-gameface-cdp/cdp-check.mjs
```

`cdp-probe.mjs` also accepts an optional `CDP_EXPRESSION` environment variable for
one-off Gameface evaluation while retaining the same connection/error handling:

```powershell
$env:CDP_EXPRESSION = '(() => ({ title: document.title }))()'
node docs/ops/scripts/2026-08-07-gameface-cdp/cdp-probe.mjs
Remove-Item Env:CDP_EXPRESSION
```

**Gotchas observed**

- Gameface has no `fetch`, incomplete `HTMLElement.click`, and rejects CSS `:not()` in `querySelector`.
- React controlled `<input value={draft}>` ignores raw DOM `value=` unless the native value setter + `input` event runs. Windows-MCP `Type` can also lose focus or fail on long text. For either path, read the input value back before clicking Send and confirm the exact `You:` line afterward.
- Coordinate map (maximized, this machine): Gameface viewport `2560×1417`, client origin `(0,23)`, scale `1.0`. Send button center ≈ screen `(2485,1222)`; chat input ≈ `(2239,1222)`.
