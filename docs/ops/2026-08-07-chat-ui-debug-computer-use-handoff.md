# Handoff: chat UI debug + computer-use / Gameface CDP (2026-08-07)

**Audience:** next agent / human continuing `Mod/` UI reliability and in-game verification.

**Prior handoff:** [2026-08-06-in-game-agent-fixes-handoff.md](./2026-08-06-in-game-agent-fixes-handoff.md) (Portal width, compact, sim wait, perception caps, road `ErrorType`).

**Cursor debug session:** `548a1a`  
**Transcript:** [Chat UI issues](548a1a29-f4f7-458d-a3e2-397269de4b81) (local agent-transcripts)  
**NDJSON log (workspace, not committed):** `debug-548a1a.log` at repo root  
**Hard rule for next agent:** **do not remove** `#region agent log` instrumentation / `Debug548a1a` / `debugLog` trigger until post-fix is explicitly signed off.

---

## Product shape (unchanged)

```text
Gameface UI (chat) ↔ Cohtml bindings ↔ C# AgentLoop + ToolQueueSystem (UIUpdate)
 ↓
 Unity ECS / CS2MCP-style bridge tools
```

Do **not** append UI to bare `"Game"`. Mount stays `GameBottomRight` + `Portal`.

---

## User-reported symptoms (this session, chronological)

From screenshots / chat (order approximate):

1. **Interrupt** not on the same row as **Send**.
2. Empty “boxes” / tofu glyphs instead of tool / status text (⚙ emoji; later Consolas Chinese tofu).
3. No auto-scroll / no useful scrollbar / stuck at top.
4. No resize (later: drag/resize UX iterations).
5. Tools not showing usefully in chat (then product choice: chat-only, ignore tool events for display).
6. TUI-ish shell requested, then “more native” again.
7. **Duplicate user messages** (“You” twice).
8. Intermittent **black screen / hitch** (GIF from earlier day; still investigated).
9. Weird **line breaks** (`You` / `:` / body stacked vertically).
10. Chat window **vanished** after some deploys.
11. Ask to drive Steam/CS2 via computer-use; install desktop MCP; operate hands-off.

---

## Solving approach (how we worked)

1. **Runtime evidence first** (debug mode): hypotheses → instrument → reproduce → classify CONFIRMED/REJECTED → smallest fix → keep logs for verification.
2. Prefer **Gameface `UI.log`** + workspace **`debug-548a1a.log`** + agent timeline JSONL over guessing from code alone.
3. UI crashes that kill the whole Cohtml tree must be fixed before any layout debate (`ScrollController`, `fetch`).
4. Avoid reintroducing known killers (listed below).
5. When desktop control was needed: Steam CLI launch → **Windows-MCP** (not low-star DCU; not AUV-as-computer-use) → Gameface **CDP :9444** for DOM truth.

---

## Hypotheses and verdicts

### Duplicate messages

| ID | Hypothesis | Verdict | Evidence |
| --- | --- | --- | --- |
| **H-DUP-A** | `send()` local `pushMessage` **and** C# `Emit(user)` each add a You line | **CONFIRMED → fixed** | `debug-548a1a.log`: `ui_push` id=0 then `applyEvent user` then `ui_push` id=1 |
| **H-DUP-B** | form `onSubmit` + Button `onSelect` double-fire | **REJECTED** | One `ui_send` per send |
| **H-DUP-C** | remount / hydrate thrash: `stateJson` updates with `store.session === ""` → full replace | **CONFIRMED (partially mitigated)** | Repeated `hydrate_replace` / `session_change` with `prevSession:""`; later mitigated with `window.__cs2AgentChat` + same-session skip wipe. Still see remounts (`mounts` resets / `session_change` on media reload) |

**Fix kept:** UI does **not** local-push on send; waits for C# `user` event. Module store on `window.__cs2AgentChat` survives remounts; hydrate only when `store.messages.length === 0` for a new session.

### Black screen / hitch

| ID | Hypothesis | Verdict | Evidence |
| --- | --- | --- | --- |
| **H-BLK-A** | `run_simulation` / `advance_time` blocks main thread | **REJECTED** (that session) | No matching slow tools in log |
| **H-BLK-B** | `screenshot` stalls render | **REJECTED** (that session) | No screenshot in timeline for black-screen window |
| **H-BLK-C** | dense tool storm | **INCONCLUSIVE** | Perception tools were fast; not proven as sole cause |
| **H-BLK-D** | high-frequency `delta`/`progress` into Cohtml | **PLAUSIBLE** | Huge `delta` volume; coincides with remounts |
| **H-BLK-E** | agent opens native tool panels → bad frame | **INCONCLUSIVE** | Not proven |
| **H-BLK-K** | **every delta** → `trigger(debugLog)` → C# `File.AppendAllText` on UI/main path | **HIGH confidence → mitigated** | After H-DUP fix, verify logs still showed per-delta IO; throttled tool logs to ≥100ms only; removed per-delta UI file logging |

**Still open:** intermittent black/hitch without hard post-fix proof that H-BLK-K was the only cause. Latest hands-off session showed world + HUD + agent panel OK while agent worked (paused city「佩恩顿」).

### UI total crash (black/missing HUD)

| Cause | Verdict | Evidence |
| --- | --- | --- |
| `new ScrollController()` | **CONFIRMED crash** | Runtime: not a constructor; kills Gameface UI tree. Use `Scrollable` only. |
| `fetch(...)` debug ingest in TS | **CONFIRMED crash** | `UI.log`: `ReferenceError: fetch is not defined`. Route UI logs via C# `debugLog` trigger. |
| Two `cs2/ui` `Button`s in one form (Interrupt + Send) | **CONFIRMED focus error** | `UI.log`: `Cannot register second focus key 'Button'`. Interrupt = plain `div`. |

### Empty tool boxes / tofu

| Cause | Verdict | Fix / note |
| --- | --- | --- |
| ⚙ emoji missing in Gameface fonts | **CONFIRMED** | Stop using emoji in chat chrome |
| `RenderChatStateJson` did not serialize tool results / names from `FunctionResultContent` + CallId map | **CONFIRMED** | Fixed in `AgentLoop.RenderChatStateJson` (CallId→name); skip empty assistant tool-call-only turns |
| Consolas TUI theme | **CONFIRMED Chinese tofu** | Reverted; native Panel + default fonts |
| Product: chat-only display | **Intentional** | UI ignores `tool` events for message list; status line shows Thinking/working |

### Line breaks (`You` / `:` / body stacked)

| Cause | Verdict | Fix |
| --- | --- | --- |
| Gameface default `display:flex` on containers stacking child nodes | **CONFIRMED** | Message as **single string** `` `${role}: ${text}` `` inside one `div`; avoid multi-node lines. Avoid invalid `display` values that spam `UI.log` (`Trying to set display property to invalid value!`). |

### Panel vanished / hard to find

| Cause | Verdict | Notes |
| --- | --- | --- |
| Custom Drag bar / `Panel draggable` / layout experiments | **SUSPECTED** | Simplified to fixed `Portal` + native `Panel`, `right:24px; bottom:200px; width:480px`. Later verified visible via screenshot + CDP. |
| Gameface media reload / remount | **OBSERVED** | `UI.log` “Reloading media 0” + font unload; debug mount logs fire again |

### Wrong tooling for “computer use”

| Tool | Verdict |
| --- | --- |
| **AUV** (`moeru-ai/auv`) | **Wrong for CS2 desktop.** README: not computer-use; Application Use Via… No CS2 driver. |
| **DCU** (`dev-willbird1936/Desktop-Computer-Use`) | Rejected by user (≈6 stars). Download also blocked by Cursor auto-review. |
| **Windows-MCP** (`Computer-Agent/Windows-MCP`, ~6.6k★) | **Installed and used.** Global `~\.cursor\mcp.json` → `uvx windows-mcp serve`. |
| **Gameface CDP :9444** | Correct for Cohtml DOM with `-uiDeveloperMode`. Prefer over AUV for UI inspect. |
| `@csmodding/gameface-devtools-mcp` | Mentioned as ideal MCP packaging; session used raw CDP + `ws` instead. |

---

## What the code looks like now (keep)

### Chat UI — `Mod/UI/src/mods/chat-panel.tsx`

- `Portal` + fixed placement; native `Panel` header/footer.
- Chat-only (tool events set busy only).
- Interrupt = `div` (when `busy`); Send = single `Button`.
- Send does **not** local-push user text.
- `window.__cs2AgentChat` store; hydrate on session change only if messages empty.
- `Scrollable` for list; **no** autoScroll; **no** `ScrollController`.
- Message lines = single template string.
- Debug: `debugLog(...)` → `trigger(mod.id, "debugLog", json)` (folded `#region agent log`).

### C# debug sink — **KEEP**

| File | Role |
| --- | --- |
| `Mod/Agent/Debug548a1a.cs` | Append NDJSON to hardcoded `...\cities-skylines-2-agent\debug-548a1a.log` |
| `Mod/Agent/AgentUISystem.cs` | `TriggerBinding` `debugLog` → `Debug548a1a.LogUiPayload` |
| `Mod/Agent/AgentLoop.cs` | `H-DUP-A` on Send; **slow tools only** (`ms >= 100`) as `tool_end_slow` with H-BLK-A/B/C tags |
| `chat-panel.tsx` | H-DUP-A/B/C logs via trigger |

### Tool message serialization — `AgentLoop.RenderChatStateJson`

Resolves tool names via CallId map from `FunctionCallContent` → `FunctionResultContent`; still relevant even if UI hides tools.

---

## Current open problems

1. **Black screen / hitch** — mitigated (no per-delta file IO); **not closed** with before/after log proof for every repro.
2. **H-DUP-C remounts** — store helps; Gameface still reloads media / remounts panel (`UI.log` Reloading media; mount logs). Understand *why* remounts (invalid display? focus? Panel close?).
3. **`UI.log` spam:** unsupported `gap` / `word-wrap`; `Trying to set display property to invalid value!` while executing `assetdb://gameui/index.js` — may be vanilla + our styles; worth grepping our UI for invalid `display`.
4. **No auto-scroll** — intentional after ScrollController crash; `Scrollable` only. Product may still want stick-to-bottom without `ScrollController`.
5. **No resize/drag** — removed after vanish suspicion; fixed geometry only.
6. **Chat composer automation** — CDP `dispatchEvent` / value setter unreliable for React controlled input; Windows-MCP Type+Enter better. Interrupt div not always found by exact text match in CDP.
7. **Hardcoded debug path** in `Debug548a1a.cs` is machine-specific (`C:\Users\super\Documents\GitHub\...`) — fine for this box; next machine must edit or generalize **without deleting** the sink until session closed.
8. Earlier ops issues still open from 2026-08-06 handoff: `list_objects` radius suspect; no footprint in place/inspect; place-before-road agent behavior.

---

## Possible causes still worth testing

- Remount driven by Cohtml focus-key / Panel state / invalid CSS `display`.
- Remaining hitch from **tool result size** or **state JSON** size even without debug IO.
- Camera/screenshot in *other* sessions (timeline for one black-screen window had neither).
- Maximized vs windowed client rect when mapping CDP coords for desktop clicks.

---

## Tools used (inventory)

### Cursor / MCP

| Tool | Purpose |
| --- | --- |
| Shell / PowerShell | Build, Steam launch, log greps, window geometry |
| Read / Grep / Write / StrReplace | Code changes |
| Delete | Clear `debug-548a1a.log` between runs |
| `cursor-ide-browser` | **Not** used for CS2 (browser only) |
| **user-windows-mcp** | Desktop: Screenshot, Snapshot, Click, Type, Wait, App, Process, PowerShell |
| Global MCP config | `C:\Users\super\.cursor\mcp.json` → Windows-MCP via `C:\Users\super\scoop\shims\uvx.exe windows-mcp serve` |

### Game / logs

| Path | Purpose |
| --- | --- |
| `%LocalLow%\Colossal Order\Cities Skylines II\Logs\UI.log` | Cohtml errors (`fetch`, ScrollController, focus key, invalid display) |
| Same tree `Player.log`, `CitiesSkylines2Agent.Mod.log` | Boot / mod load |
| `...\Mods\CitiesSkylines2Agent\logs\agent-timeline-*.jsonl` | Tool timeline |
| Repo `debug-548a1a.log` | Session NDJSON |

### Launch

- Steam app id **949230** (`Cities: Skylines II`).
- Steam.exe (Scoop): `C:\Users\super\scoop\apps\steam\current\steam.exe`
- Launch: `steam://rungameid/949230`
- Required for CDP: Steam launch option **`-uiDeveloperMode`** → inspector `http://127.0.0.1:9444/json/list` (Cohtml 1.64.x).

### Build / deploy

```text
cd Mod && dotnet build          # close game before redeploy DLL
cd Mod/UI && npm run build      # needs CSII_USERDATAPATH
```

### Node CDP helpers (archived in repo)

See [scripts/2026-08-07-gameface-cdp/](./scripts/2026-08-07-gameface-cdp/).

| Script | Role |
| --- | --- |
| `cdp-probe.mjs` | List agent panel text / inputs / buttons via `Runtime.evaluate` |
| `cdp-send.mjs` | Attempt Interrupt + set input + fire Send via MouseEvents (flaky with React) |
| `cdp-check.mjs` | Check `innerText` for probe string / recent You/Agent lines |

Deps: install `ws` once under `%TEMP%\cdp-ws` and set `NODE_PATH`.

Root copies may still exist as `.tmp-cdp-*.mjs` from the live session; **canonical** copies are under `docs/ops/scripts/...`.

### Desktop control lessons (runtime)

- Windows UIA Snapshot does **not** expose Gameface React controls inside the CS2 window (only Minimize/Close). Click chat by **screen coordinates** from CDP `getBoundingClientRect` + `ClientToScreen`.
- `App` mode `switch` with name `"Cities"` fuzzy-matched **Cursor** (“cities-skylines-2-agent”). Prefer taskbar button `Cities2.exe` or exact title `Cities: Skylines II`, or `SetForegroundWindow` on process HWND.
- `SetForegroundWindow` sometimes returns false (Windows focus rules); taskbar click worked.
- Background **city** control = in-mod tools (C# bridge), not Windows-MCP.
- Background **UI inspect** = CDP :9444 while process lives.
- Foreground **composer typing** = Windows-MCP.

Observed CDP quirks:

- `button.click` is **not a function** in Gameface.
- `querySelector` **rejects `:not()`**.
- Synthetic events often do not update React `draft` → Send no-ops if draft empty.

---

## Verified good state (2026-08-07 ~16:21 local)

Hands-off operation evidence:

- Process `Cities2.exe` running; window title `Cities: Skylines II`.
- Screenshot: paused city, HUD visible, panel **Cities Skylines 2 Agent** bottom-right; Interrupt + Send on one row; Chinese text legible; agent diagnosing utilities for「佩恩顿」.
- CDP: `textHits` include Agent messages, Interrupt, Send; `button` with text `Send`; viewport `2560×1417`.
- `debug-548a1a.log`: `mounted` + `session_change` to session `899f517c` with dozens of state messages.
- `UI.log`: Inspector on port **9444**; repeated media reloads; invalid display warnings continue.

---

## Do not reintroduce

1. `new ScrollController()`
2. Browser `fetch` (or any undefined Gameface Web API) in UI bundle
3. Two `cs2/ui` `Button`s in the same composer
4. Consolas / emoji chrome for Chinese text
5. Per-`delta` `File.AppendAllText` / debug triggers
6. Multi-node message lines without explicit block layout
7. AUV as CS2 computer-use driver
8. Low-star DCU as default Windows computer-use (prefer Windows-MCP)

---

## Suggested next steps

1. Keep instrumentation; clear `debug-548a1a.log` only via Delete tool before a focused repro.
2. If black screen returns: correlate timestamps with `tool_end_slow`, remount logs, and timeline JSONL (camera/screenshot).
3. Investigate remount root (`Reloading media`, focus key, Panel).
4. Optional: stick-to-bottom via Scrollable API **without** ScrollController — verify against Gameface types.
5. Optional: package Gameface CDP as MCP (`@csmodding/gameface-devtools-mcp`) for less ad-hoc Node.
6. Generalize `Debug548a1a` path off hardcoded username **after** session sign-off.
7. When removing debug (only after explicit OK): delete `Debug548a1a.cs`, `debugLog` binding, `#region agent log` blocks, and this session’s “KEEP” note.

---

## Commit / docs grouping suggestion

Not committed in this write unless asked:

1. Chat UI + AgentLoop serialization + throttled debug (product)
2. `Debug548a1a` + UI trigger (temporary — keep until signed off)
3. This handoff + CDP scripts under `docs/ops/`
