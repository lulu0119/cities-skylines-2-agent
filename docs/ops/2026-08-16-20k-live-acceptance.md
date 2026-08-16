# 0→20,000 population live acceptance (2026-08-16)

**Status:** in progress

This note is the evidence ledger for the 2026-08-16 Windows acceptance campaign on one named city. The target is a genuinely new city grown to at least 20,000 population, with named stage checkpoints and cold reloads allowed for memory safety. It checks the items currently awaiting live acceptance in [open-work.md](../open-work.md), records observations, and does not replace the product contract or an ADR.

## Scope and authority

- The acceptance authority is the game running on Windows, plus first-party artifacts produced by that run: agent timeline events, game/mod logs, the resulting `.cok` save, and screenshots captured from the game.
- The run must include `Loading mode Game with purpose NewGame`. A loaded pre-existing city cannot establish the initial state or close any gate below.
- The first valid `wait_simulation` snapshot must report `overview.population == 0`; the final snapshot before the last save must report `overview.population >= 20000`.
- Evidence created on or before 2026-08-15 may explain log formats or earlier failures, but cannot pass a 2026-08-16 gate.
- A result is signed only from evidence generated after this run's cutoff. Missing fields and untriggered gameplay conditions remain `pending`, `not triggered`, or `inconclusive`; they are never inferred as success.
- The run is not acceptance-complete until the post-20k save reports `Saving completed`, produces a non-empty `.cok`, leaves `GameMode.Game`, reloads that exact save into a new agent session, and rechecks the required durable state.

### Run cutoff

| Field | Value |
| --- | --- |
| New-game local start time | Original `NewGame` line no longer recoverable after log rotation; earliest retained timeline event is 2026-08-16 12:38:28 local / 04:38:28Z |
| New-game UTC cutoff | `2026-08-16T04:38:28.3069261Z` is the earliest retained event, but is not a substitute for the missing `NewGame` line |
| Pre-reload sessions | `45ee560a`, `06495cbe`, `b4301f16`, `48d3ae9b`, `4025fc81` |
| Stage 1 reload session | `8cef1a6f` |
| Post-Stage 1 sessions | `770d4087` (growth diagnosis), `2a78c8be` (read-only road/traffic diagnosis), `bc20adfd` (growth write batch) |
| Stage 1 save | `Saves\<steam-user-id>\OpenWork-20k-20260816-stage1.cok` |
| Stage 1 exit | `Logs\SceneFlow.log` 14:28:04 local, `GameManager destroyed` |
| Intermediate autosave | `Saves\<steam-user-id>\16-August-15-15-33.cok` |
| Intermediate reload session | `7bac0587` |
| Later growth sessions | `7f62d7eb` (32k schema read), `ba8c7391` (medium-row rezone/growth), `ea3ffe9a` (1k wait and named save), `84901790` (1k checkpoint reload/growth), `6077b137` (latest-autosave diagnosis, node purchase, and named save), `706f4896` (node-checkpoint reload and service placements), `7d5b3426` (northern-grid write attempt), `23d32a27` (northern-grid reapplication, notification-filter proof, and 3k checkpoint), `9a83b0db` (3k checkpoint reload and traffic intervention), `e839bef1` (latest-autosave cold reload and traffic remeasurement), `7e87a226` (landfill/industry read-only planning and named save), `d68c224f` (landfill placement failure audit), `c4b68635` (resource-guarded empty reload; no turn), `c93154d1` (successful landfill placement and baseline storage read), `57495fc5` (placement-checkpoint reload and one-hour storage baseline), `3187e642` (hour-1 checkpoint reload and second bounded hour), `b5c82cec` (hour-2 checkpoint read-only zero-fill diagnosis), `449fe512` (hour-2 checkpoint reload and third bounded hour producing fill), `3bba82ab` (fill-checkpoint reload and one bounded storage expansion), `b83906b4` (expanded-checkpoint final read-only verification) |
| 1k named checkpoint | `Saves\<steam-user-id>\OpenWork-20k-20260816-1k.cok` |
| 3k named checkpoint | `Saves\<steam-user-id>\OpenWork-20k-20260816-3k-3363.cok` |
| Traffic checkpoint | `Saves\<steam-user-id>\OpenWork-20k-20260816-3k-traffic.cok` |
| Planning checkpoint | `Saves\<steam-user-id>\OpenWork-20k-20260816-3k-plan.cok` |
| Landfill placement checkpoint | Steam Cloud `OpenWork-20k-20260816-3k-landfill-place.cok` |
| Landfill hour-1 checkpoint | Steam Cloud `OpenWork-20k-20260816-3k-landfill-hour1.cok` |
| Landfill hour-2 checkpoint | Steam Cloud `OpenWork-20k-20260816-3k-landfill-hour2.cok` |
| Landfill fill checkpoint | Steam Cloud `OpenWork-20k-20260816-3k-landfill-fill.cok` |
| Landfill expanded checkpoint | Steam Cloud `OpenWork-20k-20260816-3k-landfill-expanded.cok` |
| Latest reconciled autosave | `Saves\<steam-user-id>\16-August-17-45-58.cok` |
| Development-node checkpoint | `Saves\<steam-user-id>\OpenWork-20k-20260816-services-node.cok` |
| Latest autosave reload session | `e839bef1`; cumulative statistics below freeze at its final normal `turn.finish`, seq 22 |
| Latest named-save reload session | `b83906b4`; complete through normal `turn.finish`, seq 11 |
| Current reconciliation cutoff | 2026-08-16 20:16:13 local, after expanded-state cold verification and native-finalization crash capture |
| Final evidence cutoff | pending |

## Runtime baseline

This section must describe the executable and product artifacts actually used by the accepted run. Values observed during an earlier main-menu-only launch are not acceptance evidence and must not be copied forward as if they were.

| Artifact | Accepted-run value | Evidence |
| --- | --- | --- |
| Repository commit | `0d2fdd1daf32a5879d2cc77b4f837c71e88357d9` | `git rev-parse HEAD`; commit precedes the Stage 1 cold load |
| `CitiesSkylines2Agent.dll` SHA-256 | `BD76400788AE2B8B198E630086C99147B2595C5DDC187805B96774D243D58A92` | deployed DLL, written 2026-08-16 13:43:09 local |
| `ToolCatalog.json` SHA-256 | `E272F80548E59C71241D8D1D68D38788DEAD46D719623080A56DEC71E399BD78` | repository catalog inspected before Stage 1 |
| Catalog hot reload present | no | no runtime `hot-reload/ToolCatalog.json` at either checked runtime location |
| Request-handler source | built-in handlers | `Logs\CS2MCP.log` 14:23:51: stale 2026-08-14 payload rejected, last known-good handlers retained |
| `Cities2.exe` PID | not retained | process had exited when evidence was reconciled |
| Game/mod version | CS2 `1.6.0f1 (419.d6c6) [6216.19404]`; product loaded from deployed DLL | `Logs\SceneFlow.log` 14:22:00; mod log 14:22:09 |
| Fixed Stage 1 settings | `deepseek-v4-flash`; custom window 199,000; development tools on; vision auto; AutoStart/Continuous off | redacted settings projection last written before Stage 1; timeline `task.start` seq 2 confirms model/window/compact threshold |
| Later context segments | `deepseek-v4-flash`; window 32,000 for `7f62d7eb`/`ba8c7391`, then 16,000 for `ea3ffe9a` through `b83906b4`; compact threshold unchanged | each event-bearing timeline `task.start` seq 2; empty session `c4b68635` has only a UTF-8 BOM and contributes no configuration or KV event |

The acceptance settings for vision, demolition, progression, provider/model, and context budget must be fixed before turn 1. Changing them during the run creates a new configuration segment; it must be recorded explicitly and cannot silently share KV-cache statistics with the previous segment.

## Evidence sources

Paths below are relative to `%CSII_USERDATAPATH%`, normally `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II`.

| Evidence | Relative path | What it establishes |
| --- | --- | --- |
| Agent timeline | `ModsData\CitiesSkylines2Agent\logs\agent-timeline-*.jsonl` | sessions, turns, tool arguments/results, timing, usage, and coverage |
| Scene lifecycle | `Logs\SceneFlow.log` | new-game load, leave-game boundary, save completion, and reload |
| Mod lifecycle | `Logs\CitiesSkylines2Agent.Mod.log` | product load, session lifecycle, adapter/catalog state |
| MCP/tool execution | `Logs\CS2MCP.log` | handler source, construction dispatch, validation, and execution failures |
| UI lifecycle | `Logs\UI.log` | chat availability and Gameface lifecycle signals |
| Unity/game log | `Player.log` | game version, scene/runtime failures, clean termination |
| Save artifact | `Saves\<steam-user-id>\*.cok` or Steam userdata `949230\remote\*.cok` | non-empty durable local/Steam Cloud save and modification time |
| Screenshots | `ModsData\CitiesSkylines2Agent\logs\screenshots\` | visual state where structured tools expose no authoritative field |

Timeline timestamps are UTC. Game logs commonly use local time. Every cross-file claim must state the time basis or show the conversion.

## Acceptance matrix

Allowed statuses are `pending`, `pass`, `fail`, `not triggered`, and `inconclusive`.

| Gate | Status | Required evidence | Accepted-run evidence |
| --- | --- | --- | --- |
| New city and 0→20k envelope | pending | `NewGame`; first valid wait population 0; final population ≥20,000; money, XP, game time, and population milestones | First retained wait is population 0, but the `NewGame` line rotated away; highest retained authoritative snapshot is 3,504 after exact reload of the landfill hour-2 checkpoint and one additional game hour |
| `wait_simulation` digest shape | pass | Only top-level `hours`, `completed`, `targetReached`, `note`, `overview`, `problems`; population nested under `overview`; notification/service data nested under `problems` | Through `449fe512` seq 12, 30/30 successful waits have the exact required top-level set and no legacy fields; seven additional wait calls failed or were interrupted |
| Ledger text removed | pass | No generation input contains `Problem ledger`, `lifecycle`, `firstSeen`, or `just now` | 0 matches for all four forbidden terms across 219 generation events through `b83906b4` seq 11 |
| KV cache ≥90% | pending | Per provider/config segment: weighted overall and median cache ratios ≥90%, with metric coverage reported | Earlier 92-generation sample and the 32k segment meet target; the 199k segment remains below target at 86.95% overall, and the cumulative 16k segment is 62.20% overall / 93.37% median at the current cutoff |
| Tool surface present from turn 1 | pass | Representative first-turn tools succeed; no `agent_enable_tool_group`, group tool, or “group not enabled” failure in the run | 495 retained function events through `b83906b4` seq 11; zero group-enablement calls and zero group-not-enabled errors |
| Tool schema stable | pass | Fixed acceptance settings; catalog/DLL hashes recorded; no catalog hot reload during the run | Context windows changed only at recorded configuration boundaries (199k → 32k → 16k); tool calls remained available without groups, and stale handler payloads were rejected rather than changing the catalog |
| Road topology QA | pass | Controlled `<32 m` `too_close_junctions` and `near_miss`; no response contains a `short_stub` finding | Stage 1 seq 25: eight too-close findings at 17.7–31.0 m and four near misses at 20.0 m; all 21 retained topology results through `b5c82cec` seq 15 contain no `short_stub` |
| Water auto-connect | pending | Successful placement returns a short, near-perpendicular `Small Water Pipe` connector into a matching lane; later service/topology evidence shows it works | Connector return exists (session `45ee560a` seq 45/56), but later water topology remained isolated and no complete geometry/service chain was captured |
| Sewage auto-connect | pending | Successful placement returns a short, near-perpendicular `Small Sewage Pipe` connector into a matching lane; later service/topology evidence shows it works | Connector return exists (`45ee560a` seq 49), but later sewage topology remained isolated; the agent manually rebuilt sewage in a later session |
| Low-voltage auto-connect | pending | Successful placement returns a short, near-perpendicular `Low-voltage Ground Cable` connector into a matching lane; later service/topology evidence shows it works | Connector returns exist (`45ee560a` seq 42/44), but later low-voltage topology still reported isolated components |
| Development-node persistence | pass | Before/after `get_progression`, point delta and `purchasedNodes`; same purchase present after save/reload | `6077b137` seq 17 purchased `CrematoriumNode` (19→18), seq 23 confirmed it in `purchasedNodes`, seq 44 explicitly saved `OpenWork-20k-20260816-services-node`, and `706f4896` seq 5 confirmed the exact node and 18 points after cold reload |
| Landfill expansion persistence | pass | Surface area and capacity increase without losing stored garbage; expanded state persists after reload | `3bba82ab` made the sole expansion write and immediately read back 3,264→12,000 m², capacity 51,000→187,500, amount 52→52, and work amount 3,693→3,693. After the named Steam Cloud save and exact cold reload, `b83906b4` seq 5/8 found remapped owner `90090v1` at the same position and retained owner-linked `Landfill Site Lot` storage at 12,000 m² / 187,500 / amount 52 / work amount 3,693; seq 6 also retained `CrematoriumNode` in `purchasedNodes` |
| Specialized-industry operation | pending | Hub, owner-linked extractor, positive resource coverage, and extraction counters changing after simulation | pending |
| Specialized-industry vehicles | pending | First-party visual or structured evidence of operating vehicles associated with the industry | pending |
| Traffic-governance loop | fail | Observe → map/topology → congestion/volume sort → road write → wait 1–2h → same-location remeasure, with improvement or notification resolution | `9a83b0db` seq 15–18 observed two traffic notifications and measured 51 roads; seq 27 added a ground road from `(-407,-615)` to `(-283,-486)`. After autosave and exact cold reload, `e839bef1` seq 5/7/8 supplied the one-hour wait and identical-scope remeasure: the road persisted, but notifications stayed 2→2, the 51 common geometries' mean congestion rose 52.349→57.888 and mean volume 109.202→115.406, and bottlenecks stayed 2→2 |
| Session lifecycle | pass | Main menu has no active/sendable session; new city creates A; leaving stops and clears A; reload creates distinct B with no leaked history/pending state | Main-menu inspection found no agent store, panel, input, Send, or interruption control. Session A `e839bef1` began with an Idle, empty store, then emitted `OnDispose`, left `GameMode.Game`, and the process terminated successfully. Exact cold load of `OpenWork-20k-20260816-3k-traffic.cok` created distinct B `7e87a226`; before input it was Idle, not busy, with zero pending inputs and an empty transcript |
| Notification spatial filtering | pass | Unfiltered and two spatial queries preserve aggregate counts/top issues; only details vary and all returned details satisfy their filters | At one paused simulation point, `23d32a27` seq 33–35 issued an unfiltered query plus 500 m and 350 m spatial queries. All three returned identical citywide counts/top issues; the 500 m query correctly returned no details, and all 17 details from the 350 m query were within radius (maximum 281.1 m) |
| Road clears conflicting growable | pass | Ordinary non-signature growable identified before write; road crosses its footprint; no target demolition call; road succeeds and persists; old entity disappears | Stage 1 seq 9/11/13/14: ten ordinary growables before; road entity `55789v3` committed through two footprints; count became eight; zero `demolish` calls in session |
| Building clears conflicting growable | pass | Ordinary non-signature growable identified before write; exact standalone placement overlaps it; no target demolition call; placement succeeds; old entity disappears | Stage 1 target `254204v1` `NA_ResidentialLow01_L1_2x6` at `(3,-836.5)`; seq 21 placed `TransformerStation02` exactly there; seq 23 found the transformer at distance 0 and no old target; zero `demolish` |
| Final 20k save/reload | pending | Completed non-empty save after 20k; new session after reload; population, purchased node, and landfill state rechecked | The named landfill expanded checkpoint is non-empty, and the highest authoritative snapshot remains population 3,504. The campaign remains far below 20k; this checkpoint is not the final save |

## Measurement rules

### Population and long-run milestones

Record the first valid population snapshot, then milestones near 1k, 5k, 10k, 15k, and 20k. Do not invent a milestone if no snapshot falls close to it.

| Milestone | Timeline UTC / seq | Population | Money | XP | Game time | Problems summary |
| --- | --- | ---: | ---: | ---: | --- | --- |
| Initial retained snapshot | `45ee560a` 04:38:56Z / 5 | 0 | 2,000,000 | 0 | 2026-01-01 09:05 | Powerline Not Connected: 1; no service gap |
| Stage 1 cold-reload snapshot | `8cef1a6f` 06:24:16Z / 5 | 99 | 1,981,599 | 1,023 | 2026-01-01 17:40 | Powerline: 1; Hearse: 1; MissingUneducatedWorkers: 11; no service gap |
| Stage 2 pre-growth peak | `770d4087` 06:35:49Z / 12 | 124 | 1,956,934 | 1,436 | 2026-01-01 22:40 | Powerline: 2; MissingUneducatedWorkers: 10; no service gap |
| Intermediate autosave reload | `7bac0587` 07:32:45Z / 5 | 340 | 2,295,308 | 2,515 | 2026-01-02 11:16 | Powerline: 2; Hearse: 1; Leveling Building: 8; Building Level Up: 2; no service gap |
| Pre-1k growth snapshot | `ba8c7391` 08:01:09Z / 27 | 778 | 2,316,398 | 3,324 | 2026-01-02 15:16 | Powerline: 2; Hearse: 4; Leveling Building: 8; Building Level Up: 1; no service gap |
| ~1,000 | `ea3ffe9a` 08:09:51Z / 5 | 1,289 | 2,369,180 | 4,500 | 2026-01-02 23:16 | Powerline: 2; Hearse: 7; Ambulance: 2; no service gap |
| Latest retained snapshot | `84901790` 08:18:08Z / 5 | 1,852 | 2,919,500 | 5,889 | 2026-01-03 11:16 | Powerline: 2; Crime Scene: 4; Hearse: 14; Ambulance: 6; Leveling Building: 7; no service gap |
| Latest service snapshot | `6077b137` 08:33:19Z / 20 | 1,897 | 2,928,278 | 5,961 | 2026-01-03 12:16 | Powerline: 2; Hearse: 14; Ambulance: 8; Crime Scene: 4; Leveling Building: 13; Building Level Up: 1; no service gap |
| Post-service snapshot | `706f4896` 08:48:33Z / 15 | 1,996 | 2,569,261 | 7,146 | 2026-01-03 14:16 | Powerline: 2; Leveling Building: 22; Ambulance: 1; Building Level Up: 2; no service gap |
| Pre-3k reload snapshot | `23d32a27` 09:06:50Z / 5 | 2,687 | 3,207,669 | 8,412 | 2026-01-03 19:44 | Powerline Not Connected, Traffic Bottleneck Notification, Leveling Building, and Building Level Up present; no service gap |
| 3k named-checkpoint snapshot | `23d32a27` 09:16:39Z / 51 | 3,363 | 3,322,729 | 10,208 | 2026-01-04 07:44 | Powerline: 2; Leveling Building: 11; Traffic Bottleneck: 1; no service gap |
| 3k checkpoint reload | `9a83b0db` 09:32:02Z / 5 | 3,374 | 3,334,721 | 10,285 | 2026-01-04 08:44 | Powerline: 2; Leveling Building: 15; Traffic Bottleneck: 2; Building Level Up: 1; no service gap |
| Latest autosave reload | `e839bef1` 10:12:14Z / 5 | 3,397 | 3,346,831 | 10,335 | 2026-01-04 09:44 | Powerline: 2; Leveling Building: 12; Traffic Bottleneck: 2; Building Level Up: 2; no service gap |
| Landfill placement reload + hour 1 | `57495fc5` 11:15:19Z / 5 | 3,468 | 3,324,737 | 10,880 | 2026-01-04 13:42 | Powerline: 2; Leveling Building: 33; zero garbage-filter matches; no service gap |
| Landfill hour-1 reload + hour 2 | `3187e642` 11:22:54Z / 5 | 3,485 | 3,335,768 | 10,923 | 2026-01-04 14:42 | Powerline: 2; Crime Scene: 1; Leveling Building: 27; zero garbage-filter matches; no service gap |
| Landfill hour-2 reload + hour 3 | `449fe512` 11:48:17Z / 5 | 3,504 | 3,347,060 | 11,017 | 2026-01-04 15:42 | Powerline: 2; Leveling Building: 29; Building Level Up: 3; zero garbage-filter matches; no service gap |
| ~5,000 | pending | pending | pending | pending | pending | pending |
| ~10,000 | pending | pending | pending | pending | pending | pending |
| ~15,000 | pending | pending | pending | pending | pending | pending |
| ≥20,000 | pending | pending | pending | pending | pending | pending |
| After reload | pending | pending | pending | pending | pending | pending |

### `wait_simulation` shape

For every accepted-run `wait_simulation` result, enumerate its top-level property names. The required set is:

```text
hours, completed, targetReached, note, overview, problems
```

The legacy fields `running`, `speed`, `startFrame`, `targetFrame`, and `waitedMs` must not occur. Population must be read from `overview`; `notificationCounts` and `serviceGaps` must be read from `problems`.

### KV-cache calculation

Split generations whenever provider, model, endpoint configuration, context mode/budget, or another prompt-shaping setting changes. For each segment:

```text
overall = sum(cachedInput) / sum(input)
median  = median(cachedInput / input)
coverage = generations with both fields / total generations
```

Both `overall` and `median` must be at least 90%. A generation with a missing `cachedInput` or `input` field is excluded from the ratio and counted as uncovered; it is not assigned zero. Report `reasoningTokens` and `additional` only as aggregate counts, never by copying complete provider payloads.

| Provider/config segment | UTC range | Generations | Metric coverage | Overall | Median | Result |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| Early retained sessions; configuration provenance incomplete | 04:38:31Z–06:04:55Z | 92 | 92/92 (100%) | 93.19% | 98.29% | sampled target met; not the fixed Stage 1 baseline |
| Fixed baseline from Stage 1 through first intermediate reload: `deepseek-v4-flash`, custom 199k | 06:23:51Z–07:32:57Z | 32 | 32/32 (100%) | 86.95% | 97.03% | pending: weighted overall below 90% |
| `deepseek-v4-flash`, custom 32k (`7f62d7eb`, `ba8c7391`) | 07:51:48Z–07:59:38Z | 8 | 8/8 (100%) | 90.60% | 93.96% | target met for this segment |
| `deepseek-v4-flash`, custom 16k (`ea3ffe9a`, `84901790`, `6077b137`, `706f4896`, `7d5b3426`, `23d32a27`, `9a83b0db`, `e839bef1`, `7e87a226`, `d68c224f`, `c93154d1`, `57495fc5`, `3187e642`, `b5c82cec`, `449fe512`, `3bba82ab`, and `b83906b4`) | 08:06:43Z–12:15:07Z | 87 | 87/87 (100%) | 62.20% | 93.37% | pending: both thresholds remain below target |

The 199k aggregate is 590,891 input tokens, 513,792 cached-input tokens, and 36,754 reasoning tokens. The 32k aggregate is 141,701 input / 128,384 cached-input / 11,278 reasoning tokens; the frozen 16k aggregate is 1,198,699 / 745,600 / 29,046. Complete provider payloads are not retained in this note.

### Tool-surface proof

The first agent turn must call representative read and write tools directly. The complete timeline is then searched for:

- `agent_enable_tool_group` or any other group-enablement tool;
- tool errors containing an equivalent of “group not enabled”;
- a catalog hot reload or a settings/schema change after the first turn.

Zero matches is required for the first two searches. Catalog and deployed DLL hashes are recorded before interpreting this result.

### Road topology QA

Use controlled separations around 20–24 m so the test is unambiguously below 32 m instead of sitting on the boundary. Preserve the write arguments, the returned `too_close_junctions` and `near_miss` findings, and the post-write topology query. Search all accepted-run topology findings for `short_stub`; its count must be zero.

### Utility auto-connect geometry

For each utility type, preserve the `place_building` result showing:

```text
placed: true
connected: true
connection.prefab
connection.start
connection.end
```

The prefab must be exactly `Small Water Pipe`, `Small Sewage Pipe`, or `Low-voltage Ground Cable` for the corresponding test. From the returned coordinates and matched road lane, calculate connector length, angle relative to the road, and whether the target endpoint lies inside the compatible lane span. Then wait and confirm the new segment is not isolated and that service/notification evidence shows the facility operates.

### Development node and landfill

Before purchasing a node, record available points and `purchasedNodes`; after purchase, record the point delta and the exact node. For the landfill, identify it by prefab and position, then record `surfaceArea` and `storage.amount`, `storage.workAmount`, and `storage.capacity` before and after expansion. Acceptance requires increased area and capacity while existing garbage remains accounted for.

After reload, entity IDs may change. Re-identify both objects by durable facts such as prefab and position; do not require the pre-save entity ID to persist.

### Specialized industry

The structured proof requires a hub, an extractor linked to that hub by owner, positive resource coverage, and activity in `extractedAmount`, `workAmount`, or `totalExtracted` after waiting. The current tool surface has no authoritative vehicle field. Hub existence or positive extraction alone therefore cannot pass the vehicle sub-gate; use a real in-game screenshot/visual artifact that clearly ties active vehicles to the industry, otherwise mark it `inconclusive`.

### Traffic governance

The mayor must actually perform this sequence at one location:

```text
wait/notifications
→ local map or topology
→ list networks sorted by congestion or traffic volume
→ road write
→ wait 1–2 game hours
→ repeat the same metric at the same location
```

The gate passes only if the comparable metric improves or the associated traffic notification disappears. If no actionable congestion occurs, mark `not triggered`; if an intervention occurs without comparable before/after evidence, mark `inconclusive`.

### Session lifecycle

Establish four boundaries: main menu, new-city session A, leaving `GameMode.Game`, and reloaded session B. Session A must stop receiving timeline events when the game scene is left and must be cleared before B begins. B must have a distinct session identity and no inherited transcript, pending tool work, or automatic send from A. Main-menu Send must be unavailable and must not create a turn.

### Notification filtering

At one stable simulation point, issue an unfiltered query and two queries using distinct spatial filters (`x`, `z`, `radius`, optionally `type` and `limit`). Compare `countsByType` and `topIssues` exactly across all three results. Those aggregates must be invariant; only the `notifications` details may change. Calculate every returned detail's distance from its query center and confirm it falls within the requested radius and type filter.

### Native growable clearance

For each of the two write tools:

1. Use `list_buildings` and inspection data to identify an ordinary, non-signature growable and capture its prefab, entity ID, position, rotation, and footprint.
2. Choose a road corridor or exact standalone-building footprint that clearly intersects it.
3. Search the timeline window from target identification through post-write verification and confirm there is no `demolish` call targeting that growable.
4. Require the write itself to succeed through native validation.
5. Prove the resulting road/building exists with the relevant query and prove the old growable entity no longer exists. A later wait may establish simulation persistence, but immediate entity replacement is authoritative for the native apply transaction.

The tested interaction is the normal player expectation: construction owns recovery of an overridable growable conflict. A separate model-visible demolition step is not part of the accepted behavior.

### Save and reload

After reaching 20k, call `save_game`, then wait for both `Saving completed` in `SceneFlow.log` and a non-zero `.cok` whose modification time follows the call. Record its relative path, size, and SHA-256. Leave the game scene, load that exact save, obtain new session B, and re-query population, the purchased development node, and landfill geometry/storage.

## Live evidence ledger

Add entries only as this run produces evidence. Cite the relative file, timestamp, and timeline sequence where applicable; quote only the smallest payload fragment needed to establish the claim.

| Local/UTC time | Source | Seq | Observation | Gate impact |
| --- | --- | ---: | --- | --- |
| 12:38:56 / 04:38:56Z | `agent-timeline-45ee560a.jsonl` | 5 | First retained wait: population 0, money 2,000,000, XP 0; exact nested digest shape | Establishes the retained zero-population snapshot, not the missing `NewGame` scene boundary |
| 12:38–14:05 / 04:38–06:05Z | five early timelines | — | 92 generations, 271 functions; highest successful wait population 96; no `save_game`; each long wait at the end of a turn was interrupted | 0→20k and final-save gates remain open |
| 13:43:09 local | deployed DLL | — | Latest DLL written after the first four early sessions and before Stage 1; SHA-256 recorded above | Early sessions and Stage 1 are not one immutable binary run |
| 14:22:30 local | `Logs\SceneFlow.log` | — | Exact load of `16-August-14-03-03.cok` with purpose `LoadGame`; loading completed 14:22:40 | Stage 1 is a cold-reload checkpoint, not a new-game boundary |
| 14:23:51 local | `Logs\CS2MCP.log` | — | Stale request-handler payload rejected; built-in/last-known-good handlers retained | No handler hot-reload changed Stage 1 semantics |
| 14:24:16 / 06:24:16Z | `agent-timeline-8cef1a6f.jsonl` | 5 | Population 99, money 1,981,599, XP 1,023, no service gaps | Stage 1 baseline |
| 14:24:23–14:24:31 / 06:24:23–06:24:31Z | same timeline | 9, 11, 13, 14 | Ten ordinary growables identified; `Medium Road` `(91.5,-905)→(91.5,-760)` placed; road `55789v3` verified; same-area growables fell to eight; no demolition in session | Road growable-clearance gate passes |
| 14:24:31–14:25:02 / 06:24:31–06:25:02Z | same timeline | 14, 20, 21, 23 | Target `254204v1` `NA_ResidentialLow01_L1_2x6` at `(3,-836.5)` identified; exact `TransformerStation02` placement succeeded; target absent afterward | Building growable-clearance gate passes |
| 14:25:07 / 06:25:07Z | same timeline | 25 | Eight `too_close_junctions` at 17.7–31.0 m and four `near_miss` at 20.0 m; no `short_stub` | Road topology gate passes |
| 14:27:15 / 06:27:15Z | same timeline | 51 | `save_game(OpenWork-20k-20260816-stage1)` returned `saving:true` | Stage checkpoint requested, not final 20k save |
| 14:27:16 local | `Logs\SceneFlow.log`; save artifact | — | `Saving completed`; named save is 34,731,133 bytes, SHA-256 `F626280B5EE08365377CB143F90D4E99942CC17CD29B6097324D8E9836B8D3A4` | Durable Stage 1 checkpoint established |
| 14:27:24 / 06:27:24Z | Stage 1 timeline | 53 | Turn finished normally: 17 generations, 29 functions, 0 failures, 0 demolitions | Stage 1 evidence window is closed cleanly |
| 14:28:04 local | `Logs\SceneFlow.log` | — | `GameManager destroyed`; mod `OnDispose` occurred at 14:28:02 | Stage 1 left the game scene after the checkpoint save |
| 14:34:00–14:39:01 / 06:34:00–06:39:01Z | `agent-timeline-770d4087.jsonl` | 5, 6, 8, 10, 12, 14, 15, 25 | Four completed waits moved population 112 → 122 → 124 → 123 while money rose from 1,952,481 to 1,963,290 and XP from 1,185 to 1,758. Services reported no critical gap; residential low/medium building demand was 100, while 1,057 of 1,058 low-density cells were occupied | Diagnoses the first Stage 2 growth stall as exhausted low-density zoning rather than a service-capacity deficit |
| 14:34:05 / 06:34:05Z | same timeline | 8 | Spatial notification query centered at `(91,-832)`, radius 120 m, preserved citywide counts of Powerline 2 and MissingUneducatedWorkers 11; its only detail was 72.1 m from the center | Partial spatial-filter evidence only; a stable three-query comparison is still missing |
| 14:40:03 / 06:40:03Z | same timeline | 27, 28 | Final six-hour wait was interrupted; the turn still emitted a normal `turn.finish` after 9 generations and 12 functions | No post-wait digest exists, so this turn establishes diagnosis but not a growth milestone |
| 14:56:03–14:58:23 / 06:56:03–06:58:23Z | `agent-timeline-2a78c8be.jsonl` | 5–12 | Read-only road diagnosis inspected local connections/topology and sorted 282 road edges by traffic volume. Highest volume was 56.5, maximum returned congestion was 23.3, and all returned edges had zero active bottlenecks; no construction or simulation wait occurred | External-access diagnosis did not trigger a traffic-governance intervention; also adds one topology result with no `short_stub` |
| 15:12:48–15:12:49 / 07:12:48–07:12:49Z | `agent-timeline-bc20adfd.jsonl` | 5–27 | Baseline population 125. Of 20 `build_road` calls, 18 committed and two were rejected by native ground-slope validation at 10.6% and 10.2% against the 10% ceiling. Two low-density rectangles reported 1,262 and 1,348 cells changed | Growth capacity was expanded while preserving native validation; no force, collision bypass, or grade-separated promotion was used |
| 15:15:33–15:15:34 local | `Logs\SceneFlow.log`; save artifact | — | Automatic save `16-August-15-15-33` completed after the growth writes. The `.cok` is 35,483,749 bytes, SHA-256 `012B626AD3352CEA93A9EC3630525A629B9846F3E8850716C90E32B5CDA26C38` | Durable intermediate checkpoint established; it is an autosave, not the required explicit post-20k save |
| 15:16:01 / 07:16:01Z | `agent-timeline-bc20adfd.jsonl` | 28, 29 | The 12-hour wait was interrupted after the autosave; `turn.finish` recorded 1 generation and 24 functions, with the two slope conflicts plus the interrupted wait as the three failures | No authoritative pre-save final digest exists in this session; the saved growth state must be measured after reload |
| 15:16:49 local | `Logs\SceneFlow.log`; `Player-prev.log` | — | `GameManager destroyed`; the previous player process ended with `Game terminated successfully` | Clean exit after the intermediate autosave |
| 15:31:09–15:31:18 local | `Logs\SceneFlow.log` observed before the next log rotation | — | `--continuelastsave` selected exact save `16-August-15-15-33.cok`; loading completed and created distinct session `7bac0587` | Establishes that the intermediate autosave was loadable; the same exact save was independently loaded again at 15:39:57–15:40:06 in the subsequently rotated/current scene log |
| 15:32:45–15:32:47 / 07:32:45–07:32:47Z | `agent-timeline-7bac0587.jsonl` | 5, 7–11 | First post-reload wait reported population 340, money 2,295,308, XP 2,515, no service gap, and game time 2026-01-02 11:16. The expanded city had 3,668 low-density cells with 205 empty; budget balance was +102,955 | Confirms the growth batch persisted into the autosave and population resumed, but remains far below 20k |
| 15:32:47 / 07:32:47Z | same timeline | 11 | `purchasedNodes` contains the six basic nodes but not the earlier `WaterTreatmentPlantNode` or `AdvancedRoadServicesNode`; development points are 11 after Milestone 2 | Adverse persistence observation, but a fresh purchase immediately before a named save/reload is still required for isolated adjudication |
| 15:32:57 / 07:32:57Z | fixed-baseline timelines | — | Through `7bac0587`: 32/32 generations have cache metrics; weighted overall 86.95%, median 97.03%; no ledger-text or group-enablement matches | KV weighted-overall gate remains pending; ledger/tool-surface gates remain passed |
| 15:51:48–15:51:52 / 07:51:48–07:51:52Z | `agent-timeline-7f62d7eb.jsonl` | 4–7 | First 32k-context turn read the zone catalog and finished normally; the catalog warned that unlock flags were stale until simulation advanced | Starts a separately measured 32k KV segment; it is not gameplay-state authority |
| 15:57:31–16:01:09 / 07:57:31–08:01:09Z | `agent-timeline-ba8c7391.jsonl` | 5, 9, 15, 17, 24, 27 | Baseline population 338; low/medium residential building demand both 100; electricity/water/sewage had no critical gap; 3,668 low-density cells included 205 empty. `zone_rectangle` repainted 1,622 cells as `NA Residential Medium Row`; after four game hours population reached 778, money 2,316,398, XP 3,324, game time 2026-01-02 15:16 | Establishes a capacity-rezone growth batch and the pre-1k snapshot |
| 16:01:09 / 08:01:09Z | same timeline | 27 | Timeline ends immediately after the successful wait, with 6 generations and 18 successful functions but no `turn.finish` event | Session file is complete on disk but not evidence of a normal agent-turn finish |
| 16:06:43–16:09:54 / 08:06:43–08:09:54Z | `agent-timeline-ea3ffe9a.jsonl` | 4–7 | First 16k-context turn advanced eight game hours: population 1,289, money 2,369,180, XP 4,500, game time 2026-01-02 23:16; Powerline 2, Hearse 7, Ambulance 2, and no service gap; turn finished normally | Supplies the campaign's first retained snapshot near the 1k milestone |
| 16:10:41–16:10:43 / 08:10:41–08:10:43Z | same timeline; save artifact | 11–13 | `save_game(OpenWork-20k-20260816-1k)` returned `saving:true`; the resulting `.cok` was written at 16:10:42 local, is 35,619,578 bytes, and has SHA-256 `F1C3E0279FA51F81A9DBC11AB8237D1F1733993DC4B98EFAF514CA51CF5319A8`; the save turn finished normally | Durable explicit 1k checkpoint established by call, artifact, and subsequent exact reload; the rotated SceneFlow completion line is no longer available |
| 16:12:59–16:13:08 local | `Logs\SceneFlow.log` | — | `--continuelastsave` selected exact save `OpenWork-20k-20260816-1k.cok` with purpose `LoadGame`; loading completed and created distinct session `84901790` | Proves the named 1k checkpoint is loadable |
| 16:18:08 / 08:18:08Z | `agent-timeline-84901790.jsonl`; `Logs\SceneFlow.log` | 5 | One 12-hour wait completed at population 1,852, money 2,919,500, XP 5,889, game time 2026-01-03 11:16; Powerline 2, Crime Scene 4, Hearse 14, Ambulance 6, Leveling Building 7, and no service gap. Autosave `16-August-16-18-08` began about 75 ms before the digest timestamp | Highest retained population; the concurrent autosave cannot be assigned that exact population until reloaded and queried |
| 16:18:10 local | `Logs\SceneFlow.log`; save artifact | — | `Saving completed 16-August-16-18-08`; the `.cok` is 36,073,029 bytes, SHA-256 `55B360C10786E0AD22C13E0B7A40FC3A9CA03D14A41B1F905134209B0871425D` | Durable latest autosave established, but not yet reloaded |
| 16:18:12 / 08:18:12Z | `agent-timeline-84901790.jsonl` | 7 | Turn finished normally after 2 generations and one successful wait; the 16k segment totals 6/6 generations, 98.26% weighted cache and 98.40% median | 16k KV segment passes independently |
| 16:19:09 local | `Logs\SceneFlow.log`; `Player.log` | — | `GameManager destroyed`; player log reports `Game terminated successfully` | Clean exit after the latest autosave |
| 16:28:01–16:28:11 local | `Logs\SceneFlow.log` | — | Exact save `16-August-16-18-08.cok` loaded with purpose `LoadGame`; loading completed and created distinct session `6077b137` | Proves the latest autosave is loadable |
| 16:31:07 / 08:31:07Z | `agent-timeline-6077b137.jsonl` | 5, 10 | Read-only post-reload queries, with no wait or write, returned the same 33 citywide notifications as the 1,852 snapshot and progression at XP 5,889 / Milestone 3 / 19 development points. Purchased nodes were the eight basic service nodes; `WaterTreatmentPlantNode` and `AdvancedRoadServicesNode` remained eligible, not purchased | The autosave preserved queried notifications/progression; it does not supply an authoritative post-reload population because simulation was deliberately not advanced |
| 16:31:14 / 08:31:14Z | same timeline | 12 | First turn finished normally after 2 generations and 6 read-only functions. At that point the 16k segment was 8/8 generations, 93.16% weighted cache, 98.40% median | Intermediate KV snapshot only; the later turn changed this result |
| 16:32:47–16:33:37 / 08:32:47–08:33:37Z | same timeline | 17, 20, 23, 24 | `purchase_development_node(CrematoriumNode)` succeeded with points 19→18; after one game hour population was 1,897, money 2,928,278, XP 5,961. `get_progression` confirmed `CrematoriumNode` in `purchasedNodes`, and `Crematorium01` became unlocked | Establishes a fresh before/after development-node purchase chain before saving |
| 16:33:11–16:33:13 local | `Logs\SceneFlow.log`; save artifact | — | Autosave `16-August-16-33-11` completed after the node purchase. The `.cok` is 36,082,461 bytes, SHA-256 `66488BCC53C485009EE25F53FA706DFC75782ACD80D5A75C089D2D46DBAD1D25` | Durable post-purchase checkpoint established; cold reload is required to pass node persistence |
| 16:34:58 / 08:34:58Z | `agent-timeline-6077b137.jsonl` | 40 | Bounded service-repair turn finished normally after 4 generations and 16 functions. It purchased the node and diagnosed placement sites but placed none of the requested crematorium/clinic/police buildings and made no named save call | Partial service-repair attempt; no facility outcome can be signed |
| 16:34:25 / 08:34:25Z | 16k timeline segment | 16, 19, 22, 31 | Four post-compaction generations had cache ratios 0%, 4.62%, 4.54%, and 3.10%. Full 16k segment is now 12/12 coverage, 52.27% weighted overall, 96.97% median | KV weighted-overall gate remains pending and the 16k segment no longer passes |
| 16:37:24–16:37:39 / 08:37:24–08:37:39Z | `agent-timeline-6077b137.jsonl` | 41–47 | A bounded follow-up called only `save_game(name=OpenWork-20k-20260816-services-node)`; seq 44 returned `saving:true`, and the turn finished normally with one function | Explicit named checkpoint requested after the controlled node purchase, with no intervening simulation or city write |
| 16:37:27–16:37:29 local | `Logs\SceneFlow.log`; save artifact | — | `Saving completed OpenWork-20k-20260816-services-node`; the `.cok` is 36,176,254 bytes, SHA-256 `72787B516F34750B8F89F3ABFFDC2648888F4D553513E99180253B30D552AAE4` | Durable named development-node checkpoint established; exact cold reload and post-load progression query remain required |
| 16:38:07 local | `Logs\SceneFlow.log` | — | `GameManager destroyed` after the named checkpoint completed | Establishes a leave-game boundary before the required cold reload |
| 16:37:27–16:37:39 / 08:37:27–08:37:39Z | 16k timeline segment | 43, 46 | The save-only turn added cache ratios of 44.78% and 5.86%. Full 16k segment is now 14/14 coverage, 48.56% weighted overall, 81.90% median | Both KV thresholds remain pending |
| 16:45:15–16:45:27 local | `Logs\SceneFlow.log` | — | Exact save `OpenWork-20k-20260816-services-node.cok` loaded with purpose `LoadGame`; loading completed and created distinct session `706f4896` | Proves the named development-node checkpoint is cold-loadable |
| 16:46:34–16:46:45 / 08:46:34–08:46:45Z | `agent-timeline-706f4896.jsonl` | 1–11 | First turn called `get_progression`; seq 5 returned `CrematoriumNode` in `purchasedNodes`, 18 development points, and XP 5,961. It then placed `Crematorium01` at `(-946.375,-562.000)`, `MedicalClinic02` at `(-649.250,-793.000)`, and `PoliceStation02` at `(-446.375,-793.000)`; all three native writes succeeded | Development-node persistence passes. The service placements are valid growth-run changes, but their operational effect still needs simulation evidence |
| 16:46:38 local | `Logs\CS2MCP.log` | — | The stale hot-reload handler payload was rejected again; last known-good handlers remained active | No runtime handler change contaminated the cold-reload persistence result |
| 16:47:42–16:48:36 / 08:47:42–08:48:36Z | `agent-timeline-706f4896.jsonl` | 12–17 | One two-hour wait moved population 1,897→1,996, money to 2,569,261 and XP to 7,146. Ambulance notifications fell 8→1; Hearse and Crime Scene disappeared; no service gap was reported | Confirms the three newly placed service buildings operate through simulation and materially clear the targeted notifications |
| 16:49:44–16:49:49 / 08:49:44–08:49:49Z | same timeline | 18–24 | `demand` showed low/medium/high residential building demand 100/0/0 and household demand 60. `count_zone_cells` showed low residential 2,214 cells (2,085 occupied / 129 empty) and medium-row 1,586 (1,168 / 418) | Growth is capacity-constrained on the demanded low-density type; new zonable frontage is available but must be painted or built out |
| 16:50:27–16:50:43 local | `Logs\SceneFlow.log`; save artifacts | — | Autosave `16-August-16-50-27` completed, then seq 28 requested `save_game(OpenWork-20k-20260816-services)` and `Saving completed` at 16:50:43. The named `.cok` is 36,064,004 bytes, SHA-256 `35B24AFA2DF22851C428AC376CBD75E3672745BAC5B177FECAE8B6A3CFF62943` | Durable population-1,996 service checkpoint established; it is not the final 20k save |
| 16:51:21 local | `Logs\SceneFlow.log` | — | `GameManager destroyed` after the service checkpoint completed | Clean leave-game boundary before the next growth segment |
| 16:53:29–16:53:39 local | `Logs\SceneFlow.log`; `agent-timeline-7d5b3426.jsonl` | — | Exact load of `OpenWork-20k-20260816-services.cok` completed and created session `7d5b3426` | Starts the audited northern-grid attempt from the population-1,996 service checkpoint |
| 16:57:48 local | `Logs\CS2MCP.log` | — | A stale hot-reload handler payload failed to load; the runtime explicitly kept the last-known-good handlers. All 13 function events later frozen in `7d5b3426` report tool-level success | Records the handler-source boundary without treating the rejected payload as a runtime schema change |
| 16:58:39–16:58:41 local | `Logs\SceneFlow.log`; save artifact | — | Autosave `16-August-16-58-39` completed before the northern-grid writes. The `.cok` is 36,126,346 bytes, SHA-256 `20EA0E8E40AFCA38E7286DA57A47A31E834DB01D0A200AD63C4FCE9BCB4F44E7` | Durable pre-write checkpoint only; it cannot prove persistence of the later roads or zoning |
| 16:59:57–17:00:06 / 08:59:57–09:00:06Z | `agent-timeline-7d5b3426.jsonl` | 12–26 | Ten `Medium Road` ground writes A–J returned `success:true` and `placed:true`; two `Residential Low` rectangles changed 1,656 and 420 cells. The second turn finished normally after 3 generations and 12 functions | Proves native tool submission for this attempt, but not post-write topology, simulation growth, or persistence |
| 17:02:08–17:02:11 local | mod/scene logs; `Player-prev.log` | — | `OnDispose`, `GameManager destroyed`, and `Game terminated successfully` closed the process. No `Saving completed` occurred after the 16:59–17:00 writes | The audited A–J/zone attempt was not captured by the 16:58 autosave and must be reapplied before a later save |
| 17:05:10–17:05:20 local | `Logs\SceneFlow.log`; Gameface store | — | Exact load of `16-August-16-58-39.cok` completed and created distinct session `23d32a27`; before the first input, the store was Idle with zero pending inputs and an empty transcript | Intermediate reload/session-isolation evidence; the later four-boundary proof closes the full gate |
| 17:06:50–17:07:14 / 09:06:50–09:07:14Z | `agent-timeline-23d32a27.jsonl` | 5–11 | First post-reload wait reported population 2,687, money 3,207,669, XP 8,412, game time 2026-01-03 19:44, and no service gap; the turn finished normally | Establishes the pre-reapplication autosave's authoritative population |
| 17:07:14 / 09:07:14Z | retained timelines through `23d32a27` seq 11 | — | Intermediate sample: 163 generations, 418 functions, 30 waits (24 successful with 24/24 exact digest shape; 6 failed/interrupted), zero group-enable calls/errors, and zero forbidden-ledger matches | Historical intermediate structural/KV snapshot; superseded by the current cutoff below |
| 17:08:29–17:08:33 / 09:08:29–09:08:33Z | `agent-timeline-23d32a27.jsonl` | 15–27 | Reapplied ten `Medium Road` ground writes and two `Residential Low` rectangles after loading the pre-write autosave; all 12 calls succeeded | Restores the northern-grid growth batch that the earlier pre-write autosave did not contain |
| 17:09:12 / 09:09:12Z | same timeline | 33–35 | At one paused simulation point, an unfiltered notification query and two spatial queries returned identical citywide counts/top issues. The 500 m query returned no matching detail; all 17 details returned within 350 m were inside the filter, with maximum distance 281.1 m | Notification spatial-filtering gate passes |
| 17:10:38–17:11:04 / 09:10:38–09:11:04Z | same timeline | 43–46 | `save_game(OpenWork-20k-20260816-3k-growth)` returned `saving:true`, and the save turn finished normally | Durable post-reapplication growth checkpoint requested; it is intermediate evidence, not the final 20k save |
| 17:12:01–17:16:51 / 09:12:01–09:16:51Z | same timeline | 50–54 | A 12-hour wait completed at population 3,363, money 3,322,729, XP 10,208, game time 2026-01-04 07:44, with one traffic-bottleneck notification and no service gap; the turn finished normally | Establishes the campaign's retained 3k milestone |
| 17:17:30–17:17:52 / 09:17:30–09:17:52Z | same timeline; save artifact | 59–62 | `save_game(OpenWork-20k-20260816-3k-3363)` returned `saving:true`; the resulting `.cok` is 37,005,965 bytes, SHA-256 `708407F80BEFB60CCD3C65E85A339B49E830723959E9236D4DA7F8C3A9EA8117`; the turn finished normally | Durable named 3k checkpoint established. The `3363` suffix is the pre-save population label, not the authoritative post-reload population |
| 17:30:54–17:30:58 local | `Logs\SceneFlow.log` observed before rotation; `agent-timeline-9a83b0db.jsonl` | — | Exact load of `OpenWork-20k-20260816-3k-3363.cok` used purpose `LoadGame`, completed, and created distinct session `9a83b0db` | Establishes that the named 3k checkpoint was loadable; the cited SceneFlow lines later rotated away and cannot now be independently reread |
| 17:31:35–17:32:17 / 09:31:35–09:32:17Z | `agent-timeline-9a83b0db.jsonl` | 1–10 | First cold-reload wait reported population 3,374, money 3,334,721, XP 10,285, game time 2026-01-04 08:44, and no service gap. `CrematoriumNode` remained purchased; medium-row zoning was fully occupied and low residential had 4,165 occupied / 125 empty cells; all four functions succeeded and the turn finished normally | Confirms the named checkpoint's authoritative post-reload state and continuing development-node persistence |
| 17:35:01–17:35:46 / 09:35:01–09:35:46Z | same timeline | 15–22 | Two active traffic notifications triggered the governance loop. `local_map`, topology, and a congestion-sorted list of 51 roads established a maximum congestion index of 196.8, maximum volume index of 252.5, two bottlenecked roads, one isolated-road finding, and ten dead ends; the diagnostic turn finished normally | Supplies actionable before-state traffic evidence rather than a `not triggered` condition |
| 17:37:12–17:37:15 / 09:37:12–09:37:15Z | same timeline | 27–29 | A ground `Medium Road` from `(-407,-615)` to `(-283,-486)` committed with width 24 m and zero endpoint elevations; the write turn finished normally | Traffic intervention occurred; persistence and outcome require the later save/reload remeasurement |
| 17:35:58–17:46:00 local | `Logs\SceneFlow.log` observed before rotation; save artifact | — | Autosaves completed at 17:35:58, 17:40:58, and 17:45:58; the latter two were after the traffic write. Latest artifact `16-August-17-45-58.cok` is 36,956,254 bytes, SHA-256 `E9E644C3A039122B1B4DE72409B78560757C175778839698ADF0054671269E42` | The intervention is captured by a non-empty autosave; cold reload is still required to prove the road itself persisted |
| 17:48:48 local | Windows Resource Exhaustion Detector events 1003/2004 | — | Windows diagnosed low virtual memory; `Cities2.exe` PID 22776 was the largest consumer at 166,646,640,640 bytes of virtual memory. That process has no subsequent `OnDispose`, `GameManager destroyed`, or `Game terminated successfully` | Abnormal resource-exhaustion exit; this process cannot establish a clean session-lifecycle boundary |
| 18:10:47–18:10:51 local | `Logs\SceneFlow.log`; Gameface store | — | Exact `16-August-17-45-58.cok` load used purpose `LoadGame` and completed. The main-menu store had been null with no panel/send surface; distinct session `e839bef1` was Idle before input with zero pending inputs and an empty transcript | Proves the latest autosave is cold-loadable and contributes to the later complete session-lifecycle proof |
| 18:11:47–18:12:30 / 10:11:47–10:12:30Z | `agent-timeline-e839bef1.jsonl` | 1–10 | One-hour wait reached population 3,397, money 3,346,831, XP 10,335, and game time 09:44. Traffic notifications remained 2. Identical-scope road remeasurement returned 52 roads and found the intervention geometry as entity `52900v1`, volume 0.3 / congestion 0.1 | Proves the traffic road persisted through autosave/reload and closes the after-measurement loop |
| 18:12:16 / 10:12:16Z | `9a83b0db` seq 18; `e839bef1` seq 8 | — | Across the 51 common road geometries, mean congestion rose 52.349→57.888 and mean volume rose 109.202→115.406; bottleneck count stayed 2→2, maximum congestion rose 196.8→225.3, and traffic notifications stayed 2→2 | Intervention did not improve a comparable metric or resolve the notification; traffic-governance gate fails |
| 18:14:55–18:16:10 / 10:14:55–10:16:10Z | `agent-timeline-e839bef1.jsonl` | 11–16 | A four-hour wait was interrupted; the turn still emitted a normal `turn.finish` | Adds one failed/interrupted wait but no authoritative population snapshot |
| 18:16:43–18:16:45 / 10:16:43–10:16:45Z | same timeline; save artifact | 20–22 | `save_game(OpenWork-20k-20260816-3k-traffic)` returned `saving:true`; the resulting `.cok` is 36,924,370 bytes, SHA-256 `0AEF156902277C67DCAF9B1BC0A2E9038D1A34115B3CB44DE98082CE52847813`; the turn finished normally | Establishes the named traffic checkpoint later used for the lifecycle reload boundary |
| 18:17:48–18:17:49 local | mod/scene logs; `Player-prev.log` | — | Session A `e839bef1` emitted `OnDispose`, `GameManager destroyed` followed, and the player process ended with `Game terminated successfully` | Clean leave-game and process-exit boundary |
| 18:20:19–18:21:14 local | `Logs\SceneFlow.log`; Gameface store | — | A fresh process reached MainMenu. Store inspection found no agent store, panel, input, Send, or interruption control | Main menu cannot send or retain an active game session |
| 18:21:54–18:22:33 local | `Logs\SceneFlow.log`; Gameface store | — | Exact `OpenWork-20k-20260816-3k-traffic.cok` load used purpose `LoadGame` and completed at 18:21:59. Distinct session B `7e87a226` was Idle before input, `busy:false`, with zero pending inputs and an empty transcript | Completes the four-boundary session-lifecycle proof; gate passes |
| 18:22:33 local | retained timelines through `e839bef1` seq 22; `7e87a226` pre-input state | — | Cumulative frozen sample: 185 generations, 450 functions, 34 waits (27 successful with 27/27 exact digest shape; 7 failed/interrupted), zero group-enable calls/errors, and zero forbidden-ledger matches | Current structural and KV-statistics cutoff; `7e87a226` timeline events beginning at seq 1 are excluded |
| 18:22:33–18:26:41 / 10:22:33–10:26:41Z | `agent-timeline-7e87a226.jsonl`; save artifact | 1–27 | Read-only planning found no landfill/storage-area or specialized-industry/extractor building. `Landfill01` was unlocked with a 136 × 120 m footprint. Nine contiguous owned tiles form a 3 × 3 block centered on x/z values `-1246.6`, `-623.3`, and `0.0`. A local map centered at `(-900,-1250)` reported 69.6% owned and 59.0% candidate-buildable, then `save_game(OpenWork-20k-20260816-3k-plan)` completed; the `.cok` is 36,860,934 bytes, SHA-256 `F0C530D328E0C377EE2D07FFE848E723D04029B68C021CF204FB9469ED67528C` | Establishes the planning checkpoint and terrain/ownership evidence; `candidate_buildable` excludes water and >12% slope but does not prove building clearance |
| 18:37:17–18:37:21 local | `Logs\SceneFlow.log`; `agent-timeline-d68c224f.jsonl` | — | Exact `OpenWork-20k-20260816-3k-plan.cok` load used purpose `LoadGame`, completed, and created session `d68c224f`, turn `59ba09c8` | Starts an isolated landfill-placement attempt from the planning checkpoint |
| 18:38:03 / 10:38:03Z | `agent-timeline-d68c224f.jsonl` | 5 | The turn's only city write called `place_building` for `Landfill01` at `(-430,-830)`, radius 80, with rotation omitted. Native placement returned `success:false`, `kind:not_found`: 69 seeds produced 69 distinct resolved poses, rejected as 56 building-footprint overlaps and 13 road overlaps | No landfill was placed. Native validation was respected; there was no force/bypass, demolition, retry, expansion, wait, or other city write |
| 18:38:08–18:38:17 / 10:38:08–10:38:17Z | same timeline | 7–11 | Three read-only follow-ups found 51 buildings within 150 m (32 returned), zero garbage-role buildings, garbage production 40,912/day, and no critical service problem. The turn finished normally | Confirms the chosen center was densely occupied and that the landfill gate remains pending; no landfill-place save exists |
| 18:38:56–18:38:57 local | mod/scene logs; `Player.log` | — | `OnDispose` occurred at 18:38:56.584, `GameManager destroyed` at 18:38:57.446, the process disappeared, and the player log reports `Game terminated successfully` | Clean exit and resource-recovery boundary after the failed write |
| 18:38:57 local | retained timelines through `d68c224f` seq 11 | — | Cumulative frozen sample: 194 generations, 463 functions, 34 waits (27 successful with 27/27 exact digest shape; 7 failed/interrupted), zero group-enable calls/errors, and zero forbidden-ledger matches | Current structural and KV-statistics cutoff |
| 18:54:33–18:54:41 local | `Logs\SceneFlow.log`; Gameface store; `agent-timeline-c4b68635.jsonl` | — | A new process exactly loaded `OpenWork-20k-20260816-3k-plan.cok` with purpose `LoadGame`. After loading completed, distinct session `c4b68635` was `Idle`, `busy:false`, with zero pending inputs and an empty transcript; its timeline was still empty | Starts the bounded second placement attempt without carrying over a prior turn |
| 18:58:42 local | Windows memory counters; same timeline and store | — | Before any product-binding send, free virtual memory fell to 42.83 GB while `Cities2.exe` private memory reached 44.73 GB. The store remained Idle/pending 0/messages empty and the timeline remained zero bytes, so the `<50 GB` guard stopped the attempt before a prompt or turn existed | No model-facing input, function call, landfill write, simulation wait, or save occurred; this attempt cannot change the landfill gate |
| 18:59:01–18:59:58 local | process state; mod/scene/player logs; crash artifact | — | One `CloseMainWindow()` call returned true. `OnDispose` occurred at 18:59:02.061 and `GameManager destroyed` at 18:59:03.469; PID 28936 eventually disappeared and port 9444 closed. The player log first recorded `Game terminated successfully`, but its shutdown tail then faulted in `UnityEngine.Rendering.VirtualTexturing.Resolver.ReleaseNative` and the crash handler created `Crash_2026-08-16_105920449` | Scene/mod disposal completed and resources recovered, but this is not a clean process-exit boundary because the native crash handler intercepted finalization |
| 18:59:58 local | `agent-timeline-c4b68635.jsonl`; save inventory | — | The closed timeline is exactly three bytes (UTF-8 BOM only), SHA-256 `F1945CD6C19E56B3C1C78943EF5EC18116907A4CA1EFC40A57D48AB1DB7ADFC5`; no `OpenWork-20k-20260816-3k-landfill-place.cok` exists. After PID exit, free virtual/physical memory recovered to 87.64/51.63 GB | Definitive zero-event/zero-write result for the aborted second attempt; cumulative generation/function/wait and KV statistics remain unchanged |
| 19:04:09–19:04:13 local | `Logs\SceneFlow.log`; Gameface store; `agent-timeline-c93154d1.jsonl` | — | A fresh process exactly selected and loaded `OpenWork-20k-20260816-3k-plan.cok` with purpose `LoadGame`. Distinct session `c93154d1` was Idle, not busy, with zero pending inputs and an empty transcript before the product binding submitted one prompt | Starts an isolated third placement attempt without leaked state |
| 19:04:47–19:05:06 / 11:04:47–11:05:06Z | `agent-timeline-c93154d1.jsonl` | 1–11 | Exactly one `interleaved_input`, one `task.start`, and one `turn.start` produced turn `76133092`. Its only write was seq 5 `place_building(Landfill01,-1100,-1348,radius=50)` with rotation omitted; native validation committed entity `58910v3` at `(-1103.55,447.1926,-1347.0)`. Seq 7 and 9 were the only later functions and were read-only; seq 11 finished normally | Proves one bounded model-facing write with no demolition, force/bypass, retry, wait, area expansion, zoning, road, or other city write |
| 19:04:56–19:04:59 / 11:04:56–11:04:59Z | same timeline | 7, 9 | `list_buildings` found only `Landfill01` at distance 0 with `operationalArea`, `storageArea`, and `expandableStorageArea`. `get_operational_area` returned 28 owner-linked areas: one editable, locked-edge `Landfill Site Lot` storage polygon with surface area 3,264.0 m², amount 0, work amount 0.0, and capacity 51,000; the other 27 areas were non-storage and non-editable | Establishes the pre-fill/pre-expansion storage baseline and exact owner entity |
| 19:07:43–19:07:45 local | `Logs\SceneFlow.log`; Steam Cloud artifact | — | The game saved `OpenWork-20k-20260816-3k-landfill-place` to Steam Cloud and logged `Saving completed`. The materialized `.cok` is 36,909,037 bytes, modified 19:07:44.9807 local, SHA-256 `D6F0EADC65B4D8E80B3B2F30BFE31CF0C666788EDA32A2035C234D3BD2A29D60` | Durable named placement checkpoint established; cold reload and later fill/expansion evidence remain required |
| 19:08:16–19:08:39 local | process/mod/scene/player logs | — | Saving pushed free virtual memory below 50 GB, so one `CloseMainWindow()` call ended the bounded session. `OnDispose` occurred at 19:08:17.012, `GameManager destroyed` at 19:08:18.364, PID 40668 disappeared, port 9444 closed, and `Player.log` reports only `Game terminated successfully`; no new crash directory appeared | Clean exit and resource recovery after preserving the successful placement checkpoint |
| 19:08:39 local | retained timelines through `c93154d1` seq 11 | — | Cumulative frozen sample: 198 generations, 466 functions, 34 waits (27 successful with 27/27 exact digest shape; 7 failed/interrupted), zero group-enable calls/errors, and zero forbidden-ledger matches | Current structural and KV-statistics cutoff |
| 19:14:15–19:14:20 local | `Logs\SceneFlow.log`; Gameface store; `agent-timeline-57495fc5.jsonl` | — | The load UI exactly selected Steam Cloud `OpenWork-20k-20260816-3k-landfill-place.cok`; SceneFlow used purpose `LoadGame` and completed. Distinct session `57495fc5` was Idle, not busy, pending 0, and transcript-empty before one product-binding input | Proves the placement checkpoint is cold-loadable and starts the fill-baseline session without leaked state |
| 19:14:51–19:15:29 / 11:14:51–11:15:29Z | `agent-timeline-57495fc5.jsonl` | 1–11 | Exactly one input/task/turn ran. Seq 5 was the only time-advancing call: `wait_simulation(hours=1)` completed, restored pause/speed, and reported population 3,468, money 3,324,737, XP 10,880, game time 2026-01-04 13:42, two disconnected-powerline and 33 leveling notifications, and no service gap. Seq 6, 7, and 9 were read-only; turn finish was normal | Establishes one bounded game hour with no placement/build/zoning/demolition/purchase/expansion/save tool/retry or other city write |
| 19:15:19–19:15:23 / 11:15:19–11:15:23Z | same timeline | 6, 7, 9 | Cold reload remapped the landfill to entity `77134v1` but preserved prefab and exact position. Its sole editable owner-linked storage remained `Landfill Site Lot`, surface area 3,264.0 m², capacity 51,000, amount 0, work amount 0.0; a garbage-filtered notification query matched zero details | One hour did not produce a nonzero fill baseline; persistence of placement/geometry/capacity passes, but the expansion gate remains pending |
| 19:16:26–19:16:28 local | `Logs\SceneFlow.log`; Steam Cloud artifact | — | Because amount remained zero, the game correctly saved `OpenWork-20k-20260816-3k-landfill-hour1` rather than a misleading `fill` name. `Saving completed`; the materialized `.cok` is 36,901,097 bytes, modified 19:16:27.8949 local, SHA-256 `741958EDFD200AD4C0FD6597CEE2824EF628C31B4E58F4304C5BAC3583658022` | Durable zero-fill hour-1 checkpoint established |
| 19:16:47–19:17:10 local | process/mod/scene/player logs | — | One `CloseMainWindow()` call was followed by `OnDispose` at 19:16:47.443, `GameManager destroyed` at 19:16:48.311, PID 16656 disappearance, and port 9444 closure. `Player.log` reports only `Game terminated successfully`; no new crash directory appeared. Free virtual/physical memory recovered to 88.07/51.93 GB | Clean bounded exit after preserving the hour-1 result |
| 19:17:10 local | retained timelines through `57495fc5` seq 11 | — | Cumulative frozen sample: 201 generations, 470 functions, 35 waits (28 successful with 28/28 exact digest shape; 7 failed/interrupted), zero group-enable calls/errors, and zero forbidden-ledger matches | Current structural and KV-statistics cutoff |
| 19:22:03–19:22:07 local | `Logs\SceneFlow.log`; Gameface store; `agent-timeline-3187e642.jsonl` | — | The load UI exactly selected Steam Cloud `OpenWork-20k-20260816-3k-landfill-hour1.cok`; purpose was `LoadGame` and loading completed. Distinct session `3187e642` was Idle and empty before its single product-binding input | Starts the second bounded landfill hour from the exact hour-1 checkpoint |
| 19:22:27–19:23:08 / 11:22:27–11:23:08Z | `agent-timeline-3187e642.jsonl` | 1–12 | Seq 5 was the turn's only time advance: one `wait_simulation(hours=1)` completed in 23.26 seconds, restored pause/speed, and reported population 3,485, money 3,335,768, XP 10,923, game time 2026-01-04 14:42, and no service gap. The only later calls were read-only `list_buildings`, `notifications`, and `get_operational_area`; turn finish was normal | Establishes exactly one additional game hour with no placement/build/zoning/demolition/purchase/expansion/save tool/retry or other city write; wait digest 29/29 has the required shape |
| 19:22:57–19:23:00 / 11:22:57–11:23:00Z | same timeline | 7–10 | The landfill remapped to entity `96622v1` but retained exact position `(-1103.55,447.1926,-1347.0)`. Its owner-linked `Landfill Site Lot` remained 3,264.0 m² with capacity 51,000, amount 0, and work amount 0.0; garbage-filtered notifications matched zero details | A second separately bounded hour still did not establish a nonzero-fill precondition; expansion remains pending |
| 19:28:13–19:28:15 local | `Logs\SceneFlow.log`; Steam Cloud artifact | — | The Steam Cloud UI saved `OpenWork-20k-20260816-3k-landfill-hour2` and logged `Saving completed`. The materialized `.cok` is 36,896,018 bytes, modified 19:28:14 local, SHA-256 `2AACA05E11227BA83E6A896DDA2FECD336E47B28108660C6A945EC8B04610EC9` | Durable zero-fill hour-2 checkpoint established; the name does not falsely claim fill |
| 19:28:33–19:29:50 local | process/mod/scene/player logs | — | After saving, free virtual/physical memory fell to 13.73/2.12 GB, so one `CloseMainWindow()` call returned true. `OnDispose` occurred at 19:28:52.236, `GameManager destroyed` at 19:28:54.115, PID 2776 disappeared, and `Player.log` reports only `Game terminated successfully`; no new crash directory appeared. Memory recovered to 88.51/52.94 GB | Clean resource-guarded exit after preserving the hour-2 checkpoint |
| 19:35:12–19:35:17 local | `Logs\SceneFlow.log`; Gameface store; `agent-timeline-b5c82cec.jsonl` | — | A fresh process exactly selected and loaded Steam Cloud `OpenWork-20k-20260816-3k-landfill-hour2.cok`. Distinct session `b5c82cec` was `Idle | b5c82cec | ctx 0/16k` with an empty transcript before one strictly read-only diagnostic input | Proves the hour-2 checkpoint cold-loads and starts diagnosis without leaked turn state |
| 19:37:27–19:38:27 / 11:37:27–11:38:27Z | `agent-timeline-b5c82cec.jsonl` | 1–17 | The turn made ten function calls: nine successful reads and one bounded invalid `statistics(type=Garbage)` query whose error returned the complete valid-type list. It made zero waits, city writes, save calls, retries, or group-enable calls. When free virtual memory crossed below 50 GB, product Interrupt canceled the pending final generation; seq 17 still recorded `turn.finish` | Resource guard preserved the complete structured read results while preventing further model work; the missing assistant synthesis is reconstructed only from first-party function fields below |
| 19:37:36–19:37:41 / 11:37:36–11:37:41Z | same timeline | 5–15 | Cold reload remapped the landfill to `74795v1`. `inspect` returned only flag `building` and 30 employees; garbage budget/efficiency were 100/100; city garbage generation was 41,056/day; `problems` was empty and garbage notifications matched zero. Storage remained 3,264.0 m² / 51,000 / amount 0 / work 0.0. The closest returned road, `Medium Road` `333939v1`, was 73.4 m away with traffic volume 19.4, congestion 0.4, and no active bottleneck, but topology classified that edge as `isolated_road`, component size 3, with adjacent returned edges ending at degree-1 dead ends | Direct observation: `isolated_road` is a topology risk. The later nonzero fill disproves the stronger claim that it completely prevented collection. The two initial zero-fill hours are consistent with construction, dispatch, or round-trip startup delay, but that remains inference because the read surface exposes no direct reason code, vehicle count, or road-access boolean |
| 19:37:36–19:37:41 / 11:37:36–11:37:41Z | same timeline; `Mod/Agent/ToolCatalog.json` | 6, 8, 9, 11 | The current read surface exposes only garbage `productionRate` from `city_services`; it does not expose city garbage processing/stored/import/export/capacity. The valid statistics list contains no garbage-specific type. `inspect` does not expose an enabled or road-access boolean, vehicle count, or building-level efficiency; service budgets expose only service-wide efficiency | Requested absent fields remain explicitly unverified; none are inferred from omission |
| 19:38:39–19:39:23 local | process/mod/scene/player logs; crash artifact | — | One `CloseMainWindow()` call returned true after the resource guard. `OnDispose` occurred at 19:38:40.118 and `GameManager destroyed` at 19:38:41.981; PID 1476 disappeared and memory recovered to 88.13/52.83 GB. `Player.log` first reported `Game terminated successfully`, then finalization faulted in `UnityEngine.Rendering.VirtualTexturing.Resolver.ReleaseNative`; crash handler created `Crash_2026-08-16_113857778` | Mod/scene disposal completed, but this diagnostic process boundary is not clean because native crash handling intercepted finalization. It does not affect the previously completed hour-2 save or its earlier clean exit |
| 19:39:23 local | retained timelines through `b5c82cec` seq 17 | — | `3187e642` is 49,084 bytes, SHA-256 `570089C7B1B0880ED0FAEF31F94C75E1BCB72D35D7FD4B46C6F2CBBF482756F6`; `b5c82cec` is 67,334 bytes, SHA-256 `6CCF90A749CA7BB27E96C05472FE551C3E79D85A179793FED5401D729954F6D7`. Cumulative sample: 207 generations, 484 functions, 36 waits (29 successful with 29/29 exact digest shape; 7 failed/interrupted), zero group-enable calls/errors, and zero forbidden-ledger matches | Current structural and KV-statistics cutoff |
| 19:47:14–19:47:18 local | `Logs\SceneFlow.log`; Gameface store; `agent-timeline-449fe512.jsonl` | — | A fresh process exactly selected and loaded Steam Cloud `OpenWork-20k-20260816-3k-landfill-hour2.cok` with purpose `LoadGame`. Distinct session `449fe512` was Idle, not busy, with zero pending inputs and an empty transcript before one product-binding input | Starts the third bounded landfill hour from the exact hour-2 checkpoint without leaked state |
| 19:47:49–19:48:31 / 11:47:49–11:48:31Z | `agent-timeline-449fe512.jsonl` | 1–12 | One input/task/turn ran. Seq 5 was the only time advance: `wait_simulation(hours=1)` completed, restored pause/speed, and reported population 3,504, money 3,347,060, XP 11,017, game time 2026-01-04 15:42, and no service gap. Seq 7, 8, and 10 were the only later functions and were read-only; seq 12 finished normally | Establishes exactly one additional game hour with no expansion, placement, road, zoning, demolition, purchase, save tool, retry, or other city write; wait digest 30/30 has the required shape |
| 19:48:20–19:48:23 / 11:48:20–11:48:23Z | same timeline | 7, 8, 10 | The landfill remained `74795v1` at `(-1103.55,447.1926,-1347.0)`. Its owner-linked `Landfill Site Lot` remained 3,264.0 m² with capacity 51,000, while amount became 52 and work amount 3,693.0; garbage-filtered notifications matched zero details | Supplies the first nonzero stored-garbage baseline. It also shows the earlier `isolated_road` finding was a risk, not a complete collection block |
| 19:49:33–19:49:35 local | `Logs\SceneFlow.log`; Steam Cloud artifact | — | The game saved `OpenWork-20k-20260816-3k-landfill-fill` to Steam Cloud and logged `Saving completed`. The materialized `.cok` is 36,933,542 bytes, SHA-256 `35DB573EC143FDFA5EAE2139C86FDE482404C419DA66CE68C864A9032EDB61B3` | Durable nonempty pre-expansion checkpoint established; expansion and expanded-state cold reload remain required |
| 19:50:02–19:50:04 local | process/mod/scene/player logs | — | One `CloseMainWindow()` call was followed by `OnDispose` at 19:50:02.504, `GameManager destroyed` at 19:50:04.337, and `Game terminated successfully`; no new crash artifact appeared | Clean process boundary after preserving the fill checkpoint |
| 19:50:23 local | retained timelines through `449fe512` seq 12 | — | `449fe512` is 47,731 bytes, SHA-256 `BECFCBBDD3D617928D8894EA8059825F5EF91B25BD0230A52BEF841086847BE2`. Cumulative sample: 211 generations, 488 functions, 37 waits (30 successful with 30/30 exact digest shape; 7 failed/interrupted), zero group-enable calls/errors, and zero forbidden-ledger matches | Current structural and KV-statistics cutoff |
| 20:00:58–20:01:02 local | `Logs\SceneFlow.log`; Gameface store; `agent-timeline-3bba82ab.jsonl` | — | A fresh process exactly selected and loaded Steam Cloud `OpenWork-20k-20260816-3k-landfill-fill.cok` with purpose `LoadGame`. Distinct session `3bba82ab` was Idle, not busy, pending 0, transcript-empty, context 0/16k, and its timeline was zero bytes before one product-binding input | Starts the isolated expansion attempt from the durable nonzero-fill baseline |
| 20:02:12–20:02:47 / 12:02:12–12:02:47Z | `agent-timeline-3bba82ab.jsonl` | 1–14 | One input/task/turn made exactly four successful functions: read-only `list_buildings`, read-only `get_operational_area`, one `expand_operational_area(index=332527,version=1,target_area_m2=12000)`, then read-only `get_operational_area`. There was no wait, retry, save tool, or other city write; seq 14 finished normally | Establishes the bounded single-write expansion chain |
| 20:02:16–20:02:41 / 12:02:16–12:02:41Z | same timeline | 5–12 | Cold reload remapped the landfill to `332527v1` at `(-1103.55,447.1926,-1347.0)`. Owner-linked `Landfill Site Lot` changed from 3,264.0 to 12,000.0 m² and capacity from 51,000 to 187,500, while amount stayed 52 and work amount stayed 3,693.0. The write returned operational-area entity `337419v1`; readback retained building owner `332527v1` | Immediate post-write state satisfies every pre-save expansion invariant; cold persistence is still required |
| 20:06:04–20:06:06 local | `Logs\SceneFlow.log`; Steam Cloud artifact | — | After a concurrent automatic local save completed, the UI saved `OpenWork-20k-20260816-3k-landfill-expanded` to Steam Cloud and logged `Saving completed`. The materialized `.cok` is 36,940,902 bytes, SHA-256 `326EE776D91FB70B55422F63C11420E51E2C4ED584ADAE0379E71664BC3F6DFA` | Durable expanded checkpoint established without using the model-facing save tool |
| 20:06:35–20:07:24 local | process/mod/scene/player logs | — | The resource guard had fired, so one `CloseMainWindow()` call returned true. `OnDispose` occurred at 20:06:35.263, `GameManager destroyed` at 20:06:36.717, PID 11256 disappeared naturally, and free virtual/physical memory recovered to 87.90/51.19 GB. `Player.log` reports `Game terminated successfully`; no new crash directory appeared | Clean bounded exit after preserving the expanded checkpoint |
| 20:07:24 local | retained timelines through `3bba82ab` seq 14 | — | `3bba82ab` is 90,333 bytes, SHA-256 `566F82576A67AB3BEF8B16EB584BE36B84D16E300356947823DDDD420A5D22AB`. Cumulative sample: 216 generations, 492 functions, 37 waits (30 successful with 30/30 exact digest shape; 7 failed/interrupted), zero group-enable calls/errors, and zero forbidden-ledger matches | Current structural cutoff before expanded-state cold verification |
| 20:14:00–20:14:04 local | `Logs\SceneFlow.log`; Gameface store; `agent-timeline-b83906b4.jsonl` | — | A fresh process exactly selected and loaded Steam Cloud `OpenWork-20k-20260816-3k-landfill-expanded.cok` with purpose `LoadGame`. Distinct session `b83906b4` was Idle, not busy, pending 0, transcript-empty, and its timeline was zero bytes before one product-binding input | Establishes the required cold-load boundary for final persistence verification |
| 20:14:39–20:15:07 / 12:14:39–12:15:07Z | `agent-timeline-b83906b4.jsonl` | 1–11 | One strictly read-only turn called only `list_buildings`, `get_progression`, and `get_operational_area`; all three succeeded. There was no `wait_simulation`, city write, save, retry, or other function, and seq 11 finished normally | The verification state was observed without changing simulation or city state |
| 20:14:43–20:14:47 / 12:14:43–12:14:47Z | same timeline | 5, 6, 8 | Cold reload remapped `Landfill01` to `90090v1` at `(-1103.55,447.1926,-1347.0)`. Its owner-linked `Landfill Site Lot` remained kind `storage`, editable, and locked to the building edge, with surface area 12,000.0 m², capacity 187,500, amount 52, and work amount 3,693.0. `CrematoriumNode` remained in `purchasedNodes` | All landfill expansion-persistence and combined development-node criteria pass after exact cold reload |
| 20:15:58–20:16:13 local | process/mod/scene/player logs; crash artifact | — | One `CloseMainWindow()` call returned true. `OnDispose` occurred at 20:15:58.709, `GameManager destroyed` at 20:15:59.507, and PID 29780 disappeared with free virtual/physical memory recovered to 87.74/52.26 GB. `Player.log` first reported `Game terminated successfully`, then native finalization faulted in `UnityEngine.Rendering.VirtualTexturing.Resolver.ReleaseNative`; crash handler created `Crash_2026-08-16_121606992` | The landfill pass was fully observed before shutdown, but this process-exit boundary is not clean; no save was requested or made in the verification session |
| 20:16:13 local | retained timelines through `b83906b4` seq 11 | — | `b83906b4` is 63,000 bytes, SHA-256 `984E6624665303DB1B32BCEB25A62409ACE08CD587609C8490729A36B52DE783`. Cumulative sample: 219 generations, 495 functions, 37 waits (30 successful with 30/30 exact digest shape; 7 failed/interrupted), zero group-enable calls/errors, and zero forbidden-ledger matches | Final structural and KV-statistics cutoff for this work item |

### Landfill next-attempt site audit

These are planning candidates, not accepted placement evidence. First-party facts are: `Landfill01` needs a 136 × 120 m footprint; the nine owned tiles form a contiguous block with approximate outer x/z bounds `[-1558.25,311.65]`; and `7e87a226` seq 17 maps a long east-west `Medium Road` around world z `-1425.2`, from roughly x `-1425.6` to `-286.8`, inside buildable region B1. The local-map candidate rule is only `owned && !water && slope <= 12%`; it does not test buildings or final frontage/orientation. This distinction is material: `9a83b0db` seq 16 mapped the central area as 100% owned, 0% water, and 93.3% candidate-buildable while also showing a dense road grid; seq 18 returned 51 nearby roads and `e839bef1` seq 8 returned 52 after the traffic write. Those calls support avoiding the developed central grid, not claiming that the southern candidates are empty.

| Candidate center / radius | First-party basis | Unverified assumption for the next attempt |
| --- | --- | --- |
| `(-1320,-1340)`, radius 90 m | In mapped region B1, about 85 m north of the observed road and at least about 130 m inside the owned boundary; farthest of the three from the proven dense core around `(-430,-830)` | A 136 × 120 m resolved pose can clear buildings, the western shoreline/steep edge, the road footprint, and operational-area rules |
| `(-1120,-1340)`, radius 90 m | In B1, about 85 m north of the same road and inside the owned block | The area is vacant at placement time; no structured building query has proved clearance here |
| `(-820,-1340)`, radius 90 m | In B1, about 85 m north of the same road and inside the owned block | The footprint and resolved frontage avoid existing development and all native collision/operational-area rules |

For the next attempt, prefer `(-1320,-1340)` if only one call is allowed; use one `place_building` call with rotation omitted and let the write tool perform its bounded nearby search and native validation. A candidate is not valid until that call succeeds; do not retry `(-430,-830,radius=80)`, demolish, force, bypass collisions, or describe any candidate as empty land from the present evidence.

The bounded second attempt selected a separate audit-derived center, `(-1100,-1348)` with radius 50, but the memory guard fired before that prompt was submitted. The third attempt then used that exact request; native placement resolved and validated the committed pose at `(-1103.55,-1347.0)`. This validates the resolved pose, not every point in the search radius.

### Northern-grid write audit (`7d5b3426`)

The frozen timeline is `ModsData\CitiesSkylines2Agent\logs\agent-timeline-7d5b3426.jsonl`, 90,690 bytes, SHA-256 `D685A7661F28E7FCC209B0F18D9464A75993750D794624B2D9A72578B835BED8`. For all ten calls, the request omitted `mode`; the result used `mode:ground`, `prefab:Medium Road`, width 24 m, zero start/end elevation, coordinates equal to the request, `success:true`, and `placed:true`.

| Road | Seq / UTC | Requested and returned `(x,z)` |
| --- | --- | --- |
| A | 12 / 08:59:57.986Z | `(-283,-610) → (-283,-360)` |
| B | 13 / 08:59:58.027Z | `(-283,-360) → (-283,-110)` |
| C | 14 / 08:59:58.052Z | `(-283,-110) → (-283,140)` |
| D | 15 / 08:59:58.088Z | `(-33,-610) → (-33,-360)` |
| E | 16 / 08:59:58.111Z | `(-33,-360) → (-33,-110)` |
| F | 17 / 08:59:58.130Z | `(-33,-110) → (-33,140)` |
| G | 18 / 08:59:58.155Z | `(-283,-360) → (-33,-360)` |
| H | 19 / 08:59:58.182Z | `(-283,-110) → (-33,-110)` |
| I | 20 / 08:59:58.201Z | `(-283,140) → (-33,140)` |
| J | 21 / 08:59:58.225Z | `(-533,-360) → (-283,-360)` |

| Zone write | Seq / UTC | Request | Result |
| --- | --- | --- | --- |
| 1 | 23 / 09:00:01.426Z | `Residential Low`, center `(-158,-235)`, 230 × 730 m, rotation 0 | `NA Residential Low`; 1,656 cells changed across 38 blocks |
| 2 | 24 / 09:00:01.441Z | `Residential Low`, center `(-408,-360)`, 230 × 230 m, rotation 0 | `NA Residential Low`; 420 cells changed across 10 blocks |

The tool results explicitly say to verify roads with `list_networks`/a screenshot and to run simulation for vacant lots/buildings. Session `7d5b3426` did neither after these writes and called no save tool. Because the only autosave completed before the writes, this table is an audited write attempt, not durable northern-grid or self-growth proof. Session `23d32a27` later reapplied the batch, saved it, and advanced simulation to population 3,363; that later evidence is recorded in the chronology above.

## Final result table

This table remains pending until the save has been reloaded and all available evidence has been reconciled. A failed, untriggered, or inconclusive gate remains explicit and is not silently omitted.

| Result | Gates |
| --- | --- |
| pass | wait digest shape; ledger removal; tool surface; Stage 1 schema stability; road topology; development-node persistence; landfill expansion persistence; session lifecycle; notification spatial filtering; road-over-growable; building-over-growable |
| fail | traffic governance |
| not triggered | none adjudicated yet |
| inconclusive | none adjudicated yet |
| pending | 0→20k envelope; KV overall; all three utility auto-connect end-to-end gates; specialized industry; final 20k save/reload |

## Redaction and evidence hygiene

- Never copy API keys, `Authorization`/`Bearer` values, endpoint credentials, or complete settings files into this note.
- Do not paste complete generation input, hidden reasoning, raw provider responses, or entire logs. Record targeted fragments, aggregate token counts, ratios, timestamps, and sequence numbers.
- Normalize user-specific paths to `%CSII_USERDATAPATH%` and `Saves\<steam-user-id>`.
- Coordinates, entity IDs, prefab names, timeline sequence numbers, file sizes, and hashes are acceptable evidence.
- Preserve raw artifacts in their runtime locations. This note is an index and analysis of those artifacts, not a second raw-log archive.
