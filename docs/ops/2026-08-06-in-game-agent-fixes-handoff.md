# Handoff: in-game agent session fixes (2026-08-06)

**Audience:** next agent / human picking up `Mod/` after the first in-process mayor loop landed.

**Product path:** Gameface chat ↔ Cohtml ↔ C# `AgentLoop` (MEAI) + CS2MCP-style bridge tools on the simulation thread. No external agent process.

Related research: [agent-runtime-compression-observability](../research/2026-08-06-agent-runtime-compression-observability.md).

**Follow-on (2026-08-07):** chat UI debug, duplicate/black-screen hypotheses, Windows-MCP + Gameface CDP — [2026-08-07-chat-ui-debug-computer-use-handoff.md](./2026-08-07-chat-ui-debug-computer-use-handoff.md).

---

## What landed (code)

| Area | Paths |
| --- | --- |
| Bridge tools (CS2MCP-derived) | `Mod/CS2MCP/` |
| Agent loop, catalog, UI bindings, observability | `Mod/Agent/` |
| Settings / API key / window tokens | `Mod/Setting.cs`, `Mod/Mod.cs` |
| Deps merge for Paradox deploy | `Mod/merge-deps.ps1`, `Mod/CitiesSkylines2Agent.csproj` |
| Chat UI | `Mod/UI/src/mods/chat-panel.tsx` (mount: `GameBottomRight` in `index.tsx`) |

Logs (runtime, not in git):  
`%LocalLow%/Colossal Order/Cities Skylines II/Mods/CitiesSkylines2Agent/logs/agent-timeline-*.jsonl`

---

## Fixes in this session (why / what)

### 1. Chat panel crushed into a vertical strip

**Symptom:** Title like `CITI SKYL…`, not “wrong corner”.

**Cause:** `GameBottomRight` is a narrow HUD icon column; percentage width inherits the parent.

**Fix:** `Portal` + fixed pixel width (`~480px`), panel raised (`bottom` offset), composer in `Panel` `footer`. Never append UI to bare `"Game"` (collides with `-developerMode`).

### 2. Missing `/ping`

Catalog listed `cs2_ping` but handler was absent → tool errors.

**Fix:** `/ping` case in `RequestHandlers.cs`.

### 3. `cs2_run_simulation` + `cs2_game_state` poll storm

**Symptom:** Black screen / hitch feel; dozens of `cs2_game_state` after a timed sim run.

**Fix:** `cs2_run_simulation` blocks until auto-pause and returns final `/state`; catalog + system prompt say not to poll; `/sim/run` removed from pause-before-write set where appropriate (`AgentToolBridge.cs`).

### 4. Context compaction broke (pairing + DSML junk)

**Symptoms:** Compact HTTP 400 (tool_calls without tool result); “success” summaries that were DeepSeek DSML markup; system prompt lost after compact.

**Fix (`AgentLoop.cs`):** Flatten messages for summarizer; reject DSML / `tool_calls` summaries; cut only at safe tool-call boundaries; restore `SystemPrompt` after compact.

### 5. Full system prompt dumped into chat UI

**Cause:** `RenderChatStateJson` synced entire `m_History` including system.

**Fix:** Omit `system` from chat state JSON; UI also filters `system` roles.

### 6. Identical tool spam

**Fix:** Refuse after 3 identical tool+args signatures in one turn.

### 7. Context explosion from perception tools

**Cause:** `cs2_terrain` / `cs2_gridmap` / `cs2_list_roads` returned huge arrays → 100k–400k input tokens.

**Fix:**

- `cs2_terrain`: require range; fixed **8×8** samples; height min/max/mean + water flags; no full grids.
- `cs2_gridmap`: same range + 8×8; if native cells in bounds **>128** → `truncated` + warning.
- `cs2_list_roads`: hard max **128**; `truncated` + warning.

### 8. Opaque `cs2_build_road` failures + bad `e1`/`e2`

**Symptom:** Agent treated “stuck” as hang; actually ~100–270ms validation rejects. Models used `e1=531` like entity IDs; silent clamp hid the mistake. Error was only “overlap, water, steep…”.

**Fix:**

- On `GetAllowApply` failure, read `ErrorType` via temp `IconElement` → prefab `ToolErrorData` (`BridgeToolSystem.DescribeValidationBlock`).
- Reject `e1`/`e2` outside `-30..60` with an explicit message (elevation meters, not entity indexes).
- Richer `ToolCatalog.json` + one system-prompt line: short segments, owned land, near existing nodes.

**Verified in session `d85a3110`:** failures returned `OverlapExisting (…)`.

---

## Observed agent behavior (still open)

Not all “stuck” feelings are bugs in the tool host:

1. **Place building before access road** → horizontal connect through footprint → repeated `OverlapExisting` → micro-nudge endpoints → eventually demolish plant and `turn.finish`.
2. **`cs2_list_objects` spatial filter** looks wrong (radius query returned far-away trees) — treat as suspect; do not trust for clear-obstacle workflows until fixed.
3. **No building footprint in place/inspect responses** — model guesses lot size; high change amplification for service buildings.
4. Ambitious long spans / unowned tiles still fail (`ExceedsCityLimits` / water); prefer short owned-land segments.

Suggested follow-ups (not done unless asked): footprint in place/inspect; fix `list_objects` radius; optional prompt/tool note “road then building”; clearer net-vs-power connection guidance.

---

## Verify

```text
cd Mod && dotnet build   # close the game before redeploying the DLL
cd Mod/UI && npm run build   # needs CSII_USERDATAPATH
```

In-game: one chat turn that builds a short road; confirm timeline JSONL under Mods logs; on failure, result text should name an `ErrorType` when icons are present.

---

## Commit grouping (this handoff)

1. CS2MCP bridge handlers  
2. Agent loop + settings/wiring  
3. Chat UI Portal panel  
4. Docs (research note + this handoff)
