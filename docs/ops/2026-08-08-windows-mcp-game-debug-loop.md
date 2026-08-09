# Ops: hands-off Cities: Skylines II debug loop (2026-08-08)

**Audience:** the next agent or developer who must reproduce, debug, and verify the mod on a real Windows machine.

**Purpose:** document the complete control loop that fixed the intermittent black screen without asking the user to click through the launcher or the game. The loop is deliberately split into desktop control, Gameface UI control, in-game simulation control, and log-based verification.

**Validated outcome (2026-08-07):** Steam launch, Paradox Launcher **Play**, main-menu **Continue Game**, city loading, agent auto-start, prompt delivery, and reply inspection all completed hands-off. The final three-minute run had no periodic `Reloading media 0`; the one reload during the main-menu-to-city transition was normal.

---

## 1. Control model

"Complete control" means that each surface is controlled by the layer that actually owns it. Do not make one tool guess at another layer's state.

| Layer | Authority | Use it for |
| --- | --- | --- |
| PowerShell | Windows process and filesystem | Build, launch, process checks, log collection, and timestamping |
| Windows-MCP | Desktop/UI Automation | Steam, Paradox Launcher, taskbar/window focus, typing, waits, and screenshots |
| Gameface CDP `:9444` | CS2 Cohtml page | Inspect React/Gameface DOM, find localized controls, and dispatch a main-menu click |
| `AgentLoop` + bridge tools | CS2 simulation | City observation and construction actions; never use desktop automation for city state |
| Game and mod logs | Runtime evidence | Prove scene transitions, UI reloads, remounts, tool latency, and agent state |

The Windows-MCP server controls the desktop, not the city. The Gameface inspector controls the page, not Unity ECS. The in-mod tool queue controls the simulation on the simulation thread.

### Agent runtime seams

Keep the external `AgentLoop` facade small. Its internal responsibilities are now deliberately concentrated in a few deep modules:

| Module | Owns |
| --- | --- |
| `AgentClientFactory` | OpenAI-compatible `IChatClient` cache and model capability profile |
| `AgentPromptAssembler` | One stable system prompt plus marked skill/context prefix messages |
| `AgentContextBudget` | Conservative token estimate and tool-call/result-safe compaction boundary |
| `AgentToolSurface` | Core/meta tools and per-turn domain exposure |
| `AgentToolExecutor` | Meta tools, bridge invocation, retry guard, progress, observability, and PNG attachment |

DeepSeek V4 Flash and OpenAI use the same MEAI path. Keep `ChatToolMode.Auto`; this is the compatibility path that produces `tool_choice=auto` without a provider-specific adapter. DeepSeek V4 Flash is treated as text-only, so visual tools remain hidden; use a model profile that advertises vision for the screenshot path.

### Hard boundaries

- Real Windows plus an in-game load is the authority; browser or `archive/` POCs are not acceptance evidence.
- API keys stay in game settings or environment variables. Never put one in this document, a prompt marker, a repo file, or a log.
- Keep the product UI mounted at `GameBottomRight` through `Portal`; never append to bare `Game`.
- Close CS2 before rebuilding or redeploying the DLL.
- Keep runtime logs, screenshots, and state outside `Mods\CitiesSkylines2Agent`; the game watches that directory as mod content.
- Preserve `#region agent log`, `Debug548a1a`, and `debugLog` instrumentation until the session is explicitly signed off.

---

## 2. Preconditions and bootstrap

### Build before launching

Use the smallest relevant build first. Do not redeploy a DLL while `Cities2.exe` is using it.

```powershell
cd Mod
dotnet build

cd UI
npm run build
```

The UI build requires `CSII_USERDATAPATH`. A successful baseline from this session was `dotnet build`: 0 errors and 15 existing IL merge warnings.

### Windows-MCP endpoint

The working desktop server was Windows-MCP at:

```text
http://127.0.0.1:8000/mcp
```

The observed server identified itself as `windows-mcp` `3.4.6`. Do not assume that the host has a usable tool session just because the endpoint is listening. Initialize the MCP session, retain the returned `mcp-session-id`, and list tools before acting.

If the MCP host does not start the service for you, launch the installed server once and then verify the endpoint. Do not start a second copy when port `8000` is already serving MCP:

```powershell
uvx windows-mcp serve
```

The minimum JSON-RPC sequence is:

```http
POST /mcp
Accept: application/json, text/event-stream
Content-Type: application/json

{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"cs2-debug-runner","version":"1.0"}}}
```

Read the `mcp-session-id` response header. Reuse it on the next request:

```http
POST /mcp
Accept: application/json, text/event-stream
Content-Type: application/json
mcp-session-id: <session id from initialize>

{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
```

A representative tool call is:

```http
POST /mcp
Accept: application/json, text/event-stream
Content-Type: application/json
mcp-session-id: <session id>

{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"Snapshot","arguments":{"use_vision":true,"use_annotation":false,"use_ui_tree":true}}}
```

The tools used in this run were:

- `Snapshot`: first action for desktop state, UI tree, and interactive element ids.
- `Wait`: allow Steam, launcher, scene loading, or UI transitions to settle.
- `Click`: click a Snapshot element id or an explicit screen coordinate.
- `Type`: type into a focused or identified field; `press_enter` can submit.
- `App`: launch or focus a desktop window when the process/window is known.
- `Process`: confirm `Cities2.exe` exists and is responsive; do not kill by guesswork.
- `PowerShell`: collect logs and run non-UI checks.

`Snapshot` is intentionally first. It prevents blind clicks against the wrong window and gives a timestamped visual/UI baseline.

### Gameface CDP dependency

Launch CS2 with `-uiDeveloperMode`. Cohtml then exposes:

```text
http://127.0.0.1:9444/json/list
```

The canonical probes are in [`scripts/2026-08-07-gameface-cdp/`](./scripts/2026-08-07-gameface-cdp/). Install the temporary WebSocket dependency once:

```powershell
mkdir $env:TEMP\cdp-ws -Force | Out-Null
Push-Location $env:TEMP\cdp-ws
npm init -y | Out-Null
npm i ws --silent
Pop-Location
$env:NODE_PATH = "$env:TEMP\cdp-ws\node_modules"
```

---

## 3. Hands-off launch and city entry

### Start Steam and the real game path

Use Steam's app launch path so the Paradox Launcher, launch arguments, and mod environment are preserved. Do not start `Cities2.exe` directly as a shortcut for the normal acceptance path.

```powershell
Start-Process -FilePath 'C:\Users\super\scoop\apps\steam\current\steam.exe' `
  -ArgumentList '-applaunch 949230 -uiDeveloperMode'
```

The sequence is:

1. Call Windows-MCP `Wait` until Steam/Paradox is visible.
2. Call `Snapshot` and use the returned UI tree to click the Paradox Launcher **Play** control.
3. Call `Wait` again; use `Snapshot` to confirm that the game, not the launcher, is foreground.
4. Check `Process` for `Cities2.exe` and wait for the main menu.
5. Query Gameface CDP `:9444` and inspect the actual localized page text.
6. Locate **Continue Game** (this machine displayed `继续游戏`) through the DOM, calculate its `getBoundingClientRect()`, and dispatch `mousedown`, `mouseup`, and `click` events in that order.
7. Call `Wait` for the city scene to finish loading, then verify logs and the agent panel.

The launcher click is Windows-MCP work. The main-menu click is Gameface CDP work because CS2's React/Gameface controls are not exposed by Windows UIA.

### Why the two click paths are required

Windows-MCP `Snapshot` exposes the native launcher and window controls, but the CS2 React tree usually exposes only the outer window controls. CDP sees the Gameface DOM, including the localized menu text and the agent panel. Conversely, CDP is a poor substitute for keyboard input into a React-controlled composer; use Windows-MCP `Type`/`Click` for that.

### CDP main-menu click pattern

Use a simple selector and text walk. Gameface rejects some browser APIs that work in Chromium.

```javascript
const target = Array.from(document.querySelectorAll("*")).find((el) => {
  const text = (el.textContent || "").trim()
  return text === "继续游戏" || text === "Continue Game"
})

if (!target) throw new Error("Continue Game control not found")

for (const type of ["mousedown", "mouseup", "click"]) {
  target.dispatchEvent(new MouseEvent(type, {
    bubbles: true,
    cancelable: true,
    view: window,
  }))
}
```

If the target is not found, wait and probe again; do not immediately fall back to a blind screen coordinate. If the target is inside a custom control, inspect its ancestors and dispatch on the element that owns the visible label.

### Composer input pattern

For an agent prompt after entering the city:

1. Use CDP probe to find the panel, input, and Send bounds.
2. Convert the Gameface client rectangle to screen coordinates using the current window/client origin.
3. Focus the game window with Windows-MCP if necessary.
4. Use Windows-MCP `Type` with `clear=true`, then `Click` Send or `press_enter=true`.
5. Use CDP and logs to verify that exactly one user event and one agent turn were produced.

Do not treat raw DOM `input.value = ...` plus synthetic `input` as authoritative. The controlled React input can keep its old `draft`, causing Send to no-op. `cdp-send.mjs` is a diagnostic probe; Windows-MCP typing is the reliable composer path.

---

## 4. Observe before changing code

Start every repro with a unique, harmless marker and a timestamp. Keep the marker free of secrets; for example, `debug probe 2026-08-08T16:00:00Z: reply with only OK`.

Record the following evidence around the same run window:

| Evidence | What it proves |
| --- | --- |
| Windows-MCP `Snapshot` / `Screenshot` | Foreground window, black screen, launcher state, panel visibility, and screen geometry |
| `Process` | `Cities2.exe` existence and responsiveness |
| `Logs\UI.log` | Cohtml exceptions, focus-key errors, `Reloading media 0`, and invalid CSS/runtime messages |
| `Logs\Player.log` / mod log | Boot, scene flow, mod load, and fatal errors |
| `CitiesSkylines2Agent\logs\agent-timeline-*.jsonl` | Agent rounds, tool start/end, deltas, duration, and state transitions |
| workspace `debug-548a1a.log` | UI mount, hydrate, duplicate-message, remount, and debug-trigger chronology |
| Gameface CDP evaluation | Panel text, Send control, status, and latest `Agent:` reply |

The runtime root is selected by `CSII_USERDATAPATH` when set; otherwise it is:

```text
%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II
```

The safe runtime locations are siblings of the watched mod directory:

```text
<runtime root>\CitiesSkylines2Agent\logs
<runtime root>\CitiesSkylines2Agent\logs\screenshots
<runtime root>\CitiesSkylines2Agent\state
```

The unsafe location for runtime writes is:

```text
<runtime root>\Mods\CitiesSkylines2Agent
```

Use PowerShell to align log evidence without dumping entire files into the conversation:

```powershell
$root = if ($env:CSII_USERDATAPATH) {
  $env:CSII_USERDATAPATH
} else {
  Join-Path $env:USERPROFILE 'AppData\LocalLow\Colossal Order\Cities Skylines II'
}

Get-ChildItem "$root\Logs" -Filter '*.log' |
  Select-Object Name,Length,LastWriteTime

Get-ChildItem "$root\CitiesSkylines2Agent\logs" -Filter '*.jsonl' |
  Select-Object Name,Length,LastWriteTime

Select-String -Path "$root\Logs\UI.log" `
  -Pattern 'Reloading media 0|fetch|ScrollController|focus key|black' `
  -CaseSensitive:$false
```

The exact path of `FileSystem.log` can vary with the installation. When black screen returns, search the game log tree for watcher entries mentioning `Mods\CitiesSkylines2Agent` and align their timestamps with the agent timeline.

---

## 5. The black-screen feedback loop

The effective loop is **symptom -> evidence -> one hypothesis -> smallest change -> same repro -> acceptance gate**. Never change several unrelated UI or runtime behaviors between observations.

### Reproduce and classify

1. Capture a clean `Snapshot` and note the exact local start time.
2. Enter the same city through the same Steam/launcher/CDP path.
3. Let auto-start send its configured prompt; do not manually add a second prompt.
4. If the screen hitches or goes black, capture a `Screenshot`, check `Process`, and collect `UI.log`, scene-flow lines, timeline JSONL, and the debug NDJSON for the same time window.
5. Classify the hypothesis as **CONFIRMED**, **REJECTED**, or **INCONCLUSIVE** with one observable that would distinguish it.

### Findings from the successful session

| Hypothesis | Verdict in this session | Diagnostic lesson |
| --- | --- | --- |
| `run_simulation` / `advance_time` blocked the main thread | Rejected | No matching slow tools at the black-screen time |
| `screenshot` stalled rendering | Rejected | No screenshot in the affected timeline window |
| Dense tool storm was the sole cause | Inconclusive | Fast perception tools did not prove this |
| Every streaming delta caused Cohtml pressure and file I/O | High-confidence contributor; mitigated | Aggregate deltas by model round and remove per-delta file writes |
| Timeline/state writes under `Mods\CitiesSkylines2Agent` triggered the asset watcher | Confirmed; root cause fixed | Move runtime data outside the watched mod directory |

The decisive observation was the correlation between agent runtime writes, the unfiltered mod-directory watcher, and repeated `Reloading media 0`. Moving logs, screenshots, and context state out of `Mods` restored the full event/state/vision path without disabling the UI. A single reload during the normal menu-to-city transition remains expected; repeated reloads after agent events are not.

### Fix and rerun

The minimal fix has two parts:

1. Keep runtime data under `<runtime root>\CitiesSkylines2Agent\...`, not under `<runtime root>\Mods\CitiesSkylines2Agent`.
2. Merge streaming deltas per model round and avoid per-delta `File.AppendAllText` or UI debug triggers.

After the change, close CS2, rebuild/redeploy, and rerun the identical launch and city-entry sequence. Do not "validate" the fix by permanently disabling event/state UI, vision, or the agent loop; that only hides the failure mode.

---

## 6. In-game auto-start and verification

The product now starts one configured turn after the game reaches `Game` mode and finishes loading. The default setting is:

```text
AutoStart = true
StartupPrompt = "Observe the current city, identify the highest-priority problem, and report one next step. Do not modify the city."
```

The startup guard is armed again after leaving the city. Therefore the acceptance path is:

1. Enter a city.
2. Wait for scene loading to finish.
3. Observe one automatic user event and one agent turn.
4. Confirm the reply appears in the Gameface panel.
5. Leave/re-enter only if testing re-arming; do not send a duplicate manual prompt.

Verify all of the following, not just a screenshot:

- Scene-flow logs contain `Loading mode Game` and `Loading completed`.
- `Cities2.exe` is still responding.
- CDP text contains `Cities Skylines 2 Agent`, `Send`, and the expected agent reply/status.
- `debug-548a1a.log` shows mount/session/state events without a duplicate user push.
- The timeline shows the startup turn and its completion.
- `UI.log` has no new periodic `Reloading media 0` during agent operation.
- Runtime files are being written only under `<runtime root>\CitiesSkylines2Agent`.

The three-minute hands-off run is the minimum useful smoke test for this class of regression. A longer run is preferable when changing streaming, state serialization, or screenshot behavior.

---

## 7. Failure modes and recovery
### Visual chain acceptance

For a vision-capable model, verify the complete path rather than only the tool result text:

1. The model first enables the `visual` tool group and calls `screenshot`.
2. The bridge saves PNG outside the watched mod directory.
3. `AgentToolExecutor` attaches the PNG as `DataContent(image/png)` to the next model request.
4. The chat state omits that internal image message, so the UI does not duplicate it as a user message.
5. The next generation contains visual verification or a clear image-attachment error.

A text-only DeepSeek V4 Flash profile must not expose `screenshot`, `get_camera`, or `set_camera`; this is expected capability gating, not a broken screenshot path.

| Symptom | First action | Do not do |
| --- | --- | --- |
| MCP GET returns `406 Not Acceptable` | POST an MCP `initialize` request with `Accept: application/json, text/event-stream` | Treat the endpoint as a normal REST GET API |
| MCP tools are unavailable | Reinitialize, retain `mcp-session-id`, then call `tools/list` | Issue blind tool calls from a stale session |
| Stuck at Paradox Launcher | `Snapshot`, click the exact **Play** element, `Wait`, then snapshot again | Start `Cities2.exe` directly for the normal acceptance path |
| UIA sees only Minimize/Close in CS2 | Use CDP `:9444` for Gameface DOM and localization | Assume the React controls are missing from the product |
| CDP cannot find **Continue Game** | Wait, probe body text, and check the current locale/page | Blind-click a remembered coordinate |
| CDP Send changes no state | Use Windows-MCP `Type`/`Click` for the controlled input | Trust `HTMLElement.click()` or raw `input.value` assignment |
| `App switch "Cities"` focuses Cursor | Switch by exact title, taskbar entry, or `Cities2.exe` process | Use fuzzy window names containing "cities" |
| Black screen returns after agent events | Compare `UI.log`, `FileSystem.log`, timeline, and runtime paths first | Disable the entire UI or delete instrumentation before evidence is captured |
| DLL cannot be replaced | Close the game, then build/deploy | Rebuild over a live loaded assembly |
| One `Reloading media 0` appears at city entry | Correlate it with the scene transition | Treat every transition reload as the agent bug |

Known Gameface limitations from this run:

- `fetch` is not available in the Gameface bundle.
- `new ScrollController()` is not a valid runtime constructor here.
- A second `cs2/ui` `Button` can collide on focus key `Button`; Interrupt remains a plain `div` and Send remains the single native Button.
- `querySelector(":not(...)")` is rejected; use simple selectors and explicit filtering.
- `button.click()` is not reliable/available; dispatch mouse events explicitly.
- Consolas and emoji chrome produce tofu for Chinese text; use native panel/default fonts.

---

## 8. Final acceptance checklist

Run this checklist after a black-screen or computer-use change:

- [ ] `dotnet build` passes with no new errors.
- [ ] Steam starts app `949230` with `-uiDeveloperMode`.
- [ ] Windows-MCP initializes and `tools/list` succeeds.
- [ ] Paradox Launcher **Play** is selected by Snapshot/UI tree, not by user click.
- [ ] Gameface CDP `:9444` sees the main menu and selects localized **Continue Game**.
- [ ] City loading completes and `AutoStart` produces exactly one startup turn.
- [ ] CDP sees the panel, Send, and a real agent reply.
- [ ] `Cities2.exe` remains responsive.
- [ ] No repeated `Reloading media 0` occurs during the observation window.
- [ ] Runtime logs/state/screenshots remain outside `Mods\CitiesSkylines2Agent`.
- [ ] No API key appears in the repo, prompt marker, or captured evidence.
- [ ] Instrumentation remains available until the run is explicitly signed off.

This is the reusable debug closure: the agent can start the real game, cross the launcher boundary, enter a city, operate the Gameface UI, observe the simulation, collect synchronized evidence, make a minimal fix, and prove the fix on the same real-machine path without handing the critical click back to the user.
