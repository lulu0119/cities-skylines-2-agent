# Handoff: placement / utilities / zoning WIP (2026-08-10)

**Audience:** next Codex / agent session continuing the in-game mayor loop.
**Source session:** Codex `019fe5f0-98ed-7661-84f5-1ac5f2633ad2` (local JSONL under
`~/.codex/sessions/2026/08/09/…`, originally DeepSeek / later tagged `custom`).
**Status:** Superseded as an active handoff. Main and hot-reload projects compile,
the new-map city passed 10k acceptance, and the remaining gameplay capability gaps
are tracked in [the post-10k backlog](./2026-08-10-gameplay-capability-backlog.md).

Related:

- [10k loop task](./2026-08-09-10k-loop-task.md) — still the acceptance goal
- [Sewage handoff](./2026-08-07-sewage-handoff.md) — earlier “sees problem, never builds outlet”
- [Windows MCP game debug loop](./2026-08-08-windows-mcp-game-debug-loop.md)

---

## 1. What you should do next (ordered)

1. **Do not resume the old Codex thread** for work continuity unless you only
   need history. ChatGPT login cannot load provider=`custom` sessions without
   rewriting metadata. Prefer this handoff + the working tree.
2. Inspect uncommitted diff (`git status` / `git diff`). For handler/catalog/skill
   changes use `cd Mod/HotReload && dotnet build` while the game remains open.
3. Only after stable host/ECS changes: close the game, run `cd Mod && dotnet build`,
   then immediately rebuild `Mod/HotReload` so its host reference matches.
4. Continue on a **new map** (user rule: never reopen old saves for this work).
5. Verify next:
   1. wind/sewage connector endpoints and buried-net elevation reporting
6. Continue the 10k population loop per the task brief.

---

## 2. User rules locked in this session

- In-game model: **DeepSeek official** (`api.deepseek.com`), **not** OpenRouter.
- Always **new save**, never continue the user’s old city.
- Population / growth issues are tooling/feedback problems, not “agent laziness”.
- Zoning does **not** need tree clearing — write that into skills (done).
- Utility buildings need a **road at the site first**.
- Prefer **one-step place** (find+orient+place), keep manual connect as fallback.
- Auto-connect water/power/sewage to nearest road when placing pumps / outlets /
  turbines / plants.
- `wait_simulation` should advance **in-game time** (default **1 game hour**),
  not a tiny wall-clock stub.
- Wanted tools: `list_tiles` (filter owned/unowned/all) + `buy_tiles`.

---

## 3. Working tree (uncommitted)

HEAD at handoff time: `034fb68` (docs) / prior feature HEAD `4cd5dbb`.

Dirty (all uncommitted):

| Area | Files |
| --- | --- |
| Agent loop / surface / DeepSeek profile | `Mod/Agent/AgentLoop.cs`, `AgentToolBridge.cs`, `AgentToolSurface.cs`, `Profiles/DeepSeekProfile.cs` |
| Skills | `Skills/city-building/SKILL.md`, `Skills/utility-networks/SKILL.md` |
| Catalog | `Mod/Agent/ToolCatalog.json` |
| Bridge / tools | `BridgeSystem.cs`, `BridgeToolSystem.cs`, `RequestHandlers*.cs` (Build/Meta/Perception/Zoning/…) |

**Do not commit unless asked.** Diff is large; verify in-game before shipping.

---

## 4. What landed in code (claim vs verified)

### 4.1 Done in code (session claimed compile OK; live proof incomplete)

| Change | Where / notes |
| --- | --- |
| `wait_simulation(hours=1..24)` | `RequestHandlers.Meta.cs` — default **1 in-game hour**, restores prior speed/pause. **Breaks** older docs that say `seconds` 1–60. |
| HV / LV rules in skill | `utility-networks/SKILL.md` — wind→LV; other plants→HV; need transformer |
| “Road first” + no tree-clear for zones | `city-building/SKILL.md` |
| `place_building` + `radius` one-step | `RequestHandlers.Build.cs` — mod-side candidate grid (≤8 positions), auto-rotate to road, then **single** tool-pipeline place |
| Exact place without `rotation` | auto-tries road-facing yaw |
| Auto-connect after place | `ResolveAutoConnect` + `BridgeToolSystem` queue: road-fronted water pumps use the road's built-in pipe (no center stub); sewage→Small Sewage Pipe, wind→LV cable, other plants→HV line; skip if net already nearby |
| Buried defaults | `build_road` / auto-connect: names with `Pipe` / `Ground Cable` default `e1=e2=-10`; HV stays ground |
| Prefab component fix | Use `*Data` components (`SewageOutletData`, …) — earlier wrong names meant auto-connect never armed |
| `find_placement` | Multi-candidate tool probe still **clamped to attempts=1** (workaround from earlier hang) |
| Zoning ToolUpdate pipeline | `zone_area` maps generic residential/commercial names to the current theme, then queues a shallow request; `BridgeToolSystem` paints cells during ToolUpdate and adds `Updated` through `ToolOutputBarrier` |
| Zoning diagnostics | `zoning` / perception adds `pendingZoneDefinitions`, `tempZoneBlocks` |
| `list_tiles` / `buy_tiles` | Meta handlers + catalog; buy resolves the live tile Entity/version before `MapTilePurchaseSystem.PurchaseSelection` |
| `debug_zone_blocks` | Diagnostic-only catalog entry |
| DeepSeek output cap | Profile tweak toward larger completion budget (was hitting 120s gen timeout on huge reasoning dumps) |
| Continuous / auto-start prompts | Already on main earlier; session reinforced “keep building” |

### 4.2 Verified live on new maps

- Handler hot reload: same `Cities2.exe` PID loaded six payloads (`1/32` through `6/32`);
  changed builds produced new MVIDs, `ping.handlerRevision` matched the payload,
  and no Mods watcher reload occurred.
- `zone_area`: 60 cells / 4 blocks changed during ToolUpdate; after one in-game
  hour, multiple observed blocks had `vacantLotCount=1` (the old path stayed at 0).
- `list_tiles`: owned/unowned filters returned live map data. With zero permits,
  `filter=available` correctly returned no candidates and `buy_tiles` reached the
  game validator with `NoCurrentlyAvailable` instead of a fake entity 404.
- After the first milestone, `filter=available` returned seven tiles and
  `buy_tiles` purchased entity `56250:1` for 13,443. This completes the live
  purchase-path acceptance.
- A later expansion compared terrain around three candidate directions (north
  3.1% water, east 0%, west 10.9%), purchased dry east tile `56532:1` at grid
  `(12,12)`, and verified owned tiles increased 10→11. Roads and 250 industrial
  cells were then created on the new land.
- One-step utility placement has placed a sewage outlet, groundwater pump and two
  wind turbines in earlier new-map passes without the old multi-probe wedge.
- A road-fronted groundwater pump built after the water-connector fix added no
  `Small Water Pipe`, raised fresh-water capacity, and had no pipeline warning.
  Removing the old center-to-road stub cleared `Pipeline Not Connected` without
  reducing capacity. A wind-turbine experiment proved wind still needs its LV cable.
- Generic `Residential Low` cells had demand and VacantLots but stayed sterile.
  Repainting the same cells `NA Residential Low` produced nine buildings and the
  first residents. Hot payload `5/32` then proved generic `Residential Low` and
  `Commercial Low` resolve to `NA Residential Low` / `NA Commercial Low`; population
  subsequently reached 147 before the next diagnosis.
- Vanilla binds `GarbageAccumulationSystem.garbageAccumulation` as the garbage UI's
  **production rate**, not a service deficit. The old handler falsely emitted a
  warning for every non-empty city and induced a second landfill. Hot payload `6/32`
  now returns `garbage.productionRate` without a problem; live output was 7040 with
  `problems=[]` and no `GarbagePilingUp` notification. The agent then demolished only
  the redundant new landfill and kept the original.
- The same new-map run reached population 10,228 (10,296 including move-ins).
  Acceptance reads reported `city_services.problems=[]`, electricity
  282,441/107,132, water 54,750/20,697, sewage 78,000/20,697 and a positive
  monthly balance of 1,168,698. There were no electricity, water, sewage or
  garbage notifications. Remaining non-core icons were traffic bottlenecks,
  transient ambulance/crime requests and the map's pre-existing disconnected
  high-voltage line.
- The 10k city was quick-saved to
  `Saves/76561198152466558/10-August-05-18-17.cok` (38.2 MB) before the final
  host build.
- `Residential High` / `NA Residential High` remained locked because normal
  high density requires Big Town (46,700 XP). `Residential LowRent` was already
  unlocked: 78 painted cells produced 62 occupied cells and real
  `ResidentialLowRent01/02` buildings within two game hours. Expanding it raised
  population from 6,939 to 7,937 in one 12-hour window and unlocked the practical
  route to 10k.
- `zone_area` overwrites existing zones in its radius. Painting commercial over
  an occupied medium-residential block condemned three buildings; restoring the
  original `NA Residential Medium` zone cleared all condemned notifications
  without demolishing occupied buildings. The city-building skill now warns to
  use fresh frontage and restore an accidental rezone before bulldozing.
- UI Interrupt previously cancelled only the current turn; Continuous immediately
  queued another one. `AgentLoop` now suppresses the next auto-continuation when
  interrupted, while a later explicit Send re-enables the configured continuous
  behavior. Cold acceptance loaded `Platakotada-10k-accepted` at population
  10,307 with the hot payload directory temporarily disabled, exercised the
  built-in adapter, then proved a single Interrupt ended at `turn.finish` and
  stayed `Idle` for more than 25 seconds with no new `turn.start`.

### 4.3 Still not fully verified live

- Inspect wind/sewage connector entities to prove endpoints and buried `y=-10`
  elevation, not just successful building placement.

---

## 5. Hard bug: multi-candidate tool probe wedges

**Symptom:** `stage=ProbeCreate probeIndex=1/N tried=1` → 60s watchdog; “another build operation in progress”.

**Root cause (session conclusion):** Game `ToolSystem` only enables the **active** tool each frame. A rejected preview causes the game to switch `activeTool` back to default and **disable** our `BridgeToolSystem` → state machine freezes.

**Implications:**

- Multi-candidate probing **through** the game tool pipeline is a dead end.
- `find_placement` stays at **1 attempt**.
- `place_building` with `radius` must search **outside** the tool (heuristics), then place **one** candidate via the normal queue.
- If hangs return, do **not** “wait more frames between candidates” in the tool FSM — that path was already tried (4 frames / shrink 192→32) and still died at ProbeCreate.

---

## 6. Zoning diagnosis (resolved)

- Generic and themed prefabs can share the **same** `ZoneType`, but growable assets
  are theme-specific. On this NA map, generic `Residential Low` produced VacantLots
  but no buildings; the same cells immediately grew after `NA Residential Low`.
- Old path wrote cells during UIUpdate, too late for the zoning update lifecycle;
  VacantLot generation was not triggered, so agent zones were sterile.
- Fixed path queues the request into `BridgeToolSystem`, mutates visible/unblocked
  cells during ToolUpdate and adds `Updated` with `ToolOutputBarrier`. Live
  `debug_zone_blocks` proved VacantLots appeared after simulation advanced.
- `ResolveZoneNameForTheme` now hides that asset naming from callers by using
  `CityConfigurationSystem.defaultTheme` + `ThemePrefab.assetPrefix` when the
  matching themed prefab exists. Live generic-name calls returned the NA variants.
- Skill: do not waste turns clearing trees when growth fails.

---

## 7. Utility connection diagnosis

User guess confirmed in session:

- Skills lacked HV/LV rules (now added).
- Model connected from **building centers**, producing weird stubs; the center of a
  groundwater pump is not a water socket.
- Roads already carry water pipes, so a normally placed road-fronted water pump must
  not receive another water stub. Sewage and electrical producers still need their
  type-specific connections; blanket removal broke a live wind turbine.
- Manual `build_road` for pipes/cables without elevation left lines on the surface — default −10m fix.

Hidden “short pile” entities the model blamed for demolish failures were likely a red herring vs bad connector geometry.

---

## 8. Risks / footguns in the WIP

- Hot reload uses `Assembly.Load(byte[])`; old payload assemblies cannot unload from
  the main AppDomain. Restart after 32 successful reloads and always do final cold acceptance.
- Sewage/power auto-connect endpoint is still “nearest road point”, not a true
  service connection node — it may still look ugly or fail validation.
- Heuristic `IsCandidateBuildable` may disagree with full game placement validation → false negatives/positives.
- Garbage capacity/processing totals are not yet surfaced in `city_services`;
  `notifications` is the authority for an actual collection failure.
- Deploy only with game closed; never write runtime files into the Mods deploy folder (black-screen cause).

---

## 9. Suggested first commands for the next session

```text
Read docs/ops/2026-08-10-placement-utilities-handoff.md and docs/ops/2026-08-09-10k-loop-task.md.
Summarize uncommitted placement/zoning/utility diffs.
If host/ECS code changed, close game, dotnet build Mod, rebuild HotReload, then start a NEW infinite plains / easy map; otherwise keep the live city and hot-build the payload.
Prove: wind/sewage connectors are buried at -10 and list_tiles/buy_tiles complete a real purchase.
Then continue the 10k loop. Smallest diffs only. Do not commit unless asked.
```

---

## 10. Environment cheatsheet

- Repo: `C:\Users\super\Documents\GitHub\cities-skylines-2-agent`
- Deploy: `%LocalLow%\Colossal Order\Cities Skylines II\Mods\CitiesSkylines2Agent\`
- Agent logs: `%LocalLow%\Colossal Order\Cities Skylines II\CitiesSkylines2Agent\logs\`
- Game logs: `%LocalLow%\Colossal Order\Cities Skylines II\Logs\`
- Build: `cd Mod && dotnet build`
- Hot payload: `cd Mod/HotReload && dotnet build`
