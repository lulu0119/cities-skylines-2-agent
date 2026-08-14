# Cities: Skylines 2 Agent

The in-game AI mayor: a Gameface chat UI talks to a C# loop that enqueues construction and city tools onto the simulation thread. Players install the mod and paste an API key; there is no external agent process.

## Language

### Runtime

**Agent**:
The in-game mayor runtime: one session, one model, tools queued onto the simulation thread.
_Avoid_: MCP client, external agent process, apeira

**Model-facing surface**:
The tools and text the model is allowed to call or see.
_Avoid_: HTTP route, backend handler, catalog row (those may exist without being model-facing)

**Mayor skill**:
A playbook the mayor model loads with `agent_read_skill`.
_Avoid_: engineering skills under `.agents/` or `~/.agents/`

**Traffic governance**:
The mayor's congestion loop over existing tools: ledger trigger, topology QA plus `list_networks` ranked by congestion or traffic volume, existing road writes, then `wait_simulation` and re-measure.
_Avoid_: a new traffic tool, adding lanes to every local street, treating degree-1 dead ends as automatic errors

**Wait simulation**:
The tool that advances in-game time, then restores the previous speed and pause. The player owns the clock.
_Avoid_: forced pause as the product runtime, polling `game_state` to wait

**Context budget**:
How many tokens the loop treats as the window. Auto uses the named-model profile; Custom replaces that window with the player setting for every model name.
_Avoid_: Endpoint or provider as the source of the window; Custom as a change to the server model limit

**Compaction**:
Summarizing older turns when estimated tokens reach the compact threshold.
_Avoid_: deleting the session, starting a new chat

### Construction

**Prefab**:
An exact named game asset. The Agent picks one before placing or building.
_Avoid_: service `role` as a `place_building` argument

**place_building**:
The write that places one standalone prefab and pose.
_Avoid_: `find_placement`, `find_infrastructure_candidate`, preview-then-commit

**build_road**:
The write that constructs a linear network between endpoints. Distinct from placing a building.
_Avoid_: `place_road`, `build_bridge` as a current tool

**Ground**:
Default road mode: follow terrain; reject water and steep grades instead of rewriting the route.
_Avoid_: implied bridge, auto-elevate

**Grade-separated**:
Explicit road mode for a bridge, elevated road, or tunnel. The model must ask for it.
_Avoid_: `build_bridge`, silent promotion from a failed ground path

**Native validation**:
The game's ordinary placement and apply checks. The product does not bypass them.
_Avoid_: Anarchy, `force`, collision bypass

**Auto-connect**:
Placement-owned follow-up that attaches matching water, sewage, or low-voltage networks.
_Avoid_: Agent-drawn pipes or cables as the happy path

**Network**:
Roads, pipes, and cables as linear infrastructure.
_Avoid_: "road" as a synonym for every utility line

**Typed network**:
A top-level linear edge classified as road, fresh water, sewage, or low-voltage from native prefab layers. Auto-connect, list, demolish, and topology QA share that identity.
_Avoid_: a second utility-versus-road taxonomy; high-voltage as part of this set

**Isolated component**:
A connected set of typed-network edges that does not reach an outside connection (roads) or a road edge (pipes and cables).
_Avoid_: treating every degree-1 dead end as isolated

**Operational area**:
An owner-linked lot polygon on a facility (storage or extractor). The current product expands only.
_Avoid_: district, a standalone area with no owner

**Transit stop**:
An existing passenger or cargo boarding object (station sub-stop or roadside stop). Listed for line tools; not a `place_building` role.
_Avoid_: inventing a stop by placing a transport building role

**Transit line**:
An ordered loop of transit stops applied through the native route tool. Vehicles stay native.
_Avoid_: Gameface `transportLines$` as the write path; a vehicle or production tool

### Perception and authority

**LOCAL_MAP**:
Budgeted semantic-vector text from `terrain`. Spatial evidence, not construction approval.
_Avoid_: heightmap, 8×8 samples as the Agent interface

**Player permission**:
A durable setting that shows or hides a write tool (demolition, spending Development Points, visual tools).
_Avoid_: per-call `force`, a confirmation modal after the setting is already on

**Development tools**:
Default-off diagnostics (`replace_road_type`, `debug_zone_blocks`, `save_game`). Not a permission bypass.
_Avoid_: anarchy mode, debug as always-on
