# 10k acceptance follow-up: gameplay capability backlog (2026-08-10)

**Status:** Rectangle zoning, progression reads, typed prefab roles, road-feature
editing, restricted road-type replacement, and operational-area inspection are
implemented and live-accepted. Building discovery now also accepts typed service
roles and operational-area capabilities. Landfill expansion plans a multi-node fan
from a requested total surface instead of exposing a direction or depth. A positive
development-node purchase, non-empty landfill behavior, forest extraction scoring,
specialized-industry live acceptance, and cold reload of the new fan shape remain
backlog items.

This is the next gameplay backlog after the new-map run reached population 10,228.
It separates missing game capabilities from mayor-policy knowledge so a later session
does not try to fix every symptom in the prompt.

## Priority summary

| Priority | Gap | Player-visible cost | Next seam |
| --- | --- | --- | --- |
| Closed | Circular-only zoning | Low frontage coverage and accidental overlap with occupied blocks | `zone_rectangle` mutates cells during `ToolUpdate` |
| Implemented | No milestone/development-tree tools | Agent earns progression but keeps choosing basic services | Compact progression frontier + native node purchase; positive purchase/reload acceptance pending |
| Implemented | Landfill storage area stuck at its default | Capacity cannot grow with the site's available land | `expand_operational_area(building, extra_depth_m)` accepted through save/reload |
| P1 | No specialized-industry workflow | Raw materials are imported and freight/economy opportunities are missed | Resource perception + hub placement + extraction polygon |
| Policy | No road hierarchy | Local streets and a few junctions absorb most through/freight traffic | Gameplay skill updated in this change |
| Closed | Trees treated as obstacles | Wasted demolish/placement turns | Existing skill already states that growables clear vegetation |

## 1. Rectangular zoning

### Current problem

`zone_area(name, x, z, radius)` paints every eligible Zone Cell inside a circle.
Road zoning is made of rectangular frontage strips, so a circular brush wastes much
of its area away from roads while its curved edge is difficult to keep off an
occupied neighboring district. The 10k run already proved that overlap can rezone
and condemn existing growables.

### Recommended first interface

Add a rectangle operation with a caller-facing shape rather than leaking Zone Block
internals:

```text
zone_rectangle(name, center_x, center_z, width, depth, rotation=0)
```

- Keep the current circle call for compatibility, but prefer rectangles in the Tool
  catalog and city-building skill.
- Queue the shallow request into `BridgeToolSystem` and mutate cells during
  `ToolUpdate`, preserving the existing `ToolOutputBarrier`/`Updated` behavior.
- Test cell centers against the rotated rectangle; do not approximate it with many
  overlapping circles.
- Return changed-cell and touched-block counts plus the resolved themed zone name.

A later, deeper interface could zone a strip of frontage selected from a road edge,
but a rotated rectangle is the smallest useful improvement and does not require the
caller to understand road-edge ECS data.

### Acceptance

- A long rectangular residential request fills both sides of a straight road with
  materially less unused brush area than the current circle.
- No cells outside the rectangle change.
- VacantLots appear after simulation advances.
- A rectangle adjacent to occupied zoning does not touch the occupied cells.

### Implemented; read path live-accepted

`zone_rectangle(zone, x, z, width, depth, rotation=0)` is now in the construction
group. It shares zone-name/theme resolution with `zone_area`, queues the rectangle
into `BridgeToolSystem`, and performs the rotated cell-center test during
`ToolUpdate`.

Fresh-map acceptance on `ToolLoop-20260810卢艾` (Ribbon Isles, normal mode,
tutorial disabled) resolved generic `Residential Low` to `EU Residential Low` and
changed 222 cells across five zone blocks for a `44m x 290m` rectangle at
`(165, 460)`. The game process remained responsive.

## 2. Milestones and development-tree unlocks

Cities: Skylines II progression has two layers. XP reaches Milestones; Milestones
award Development Points; the player spends those points on service-specific
Development Tree nodes. Reaching a later city stage therefore does not itself make
every advanced hospital, power plant, transit mode, or waste facility available.
This is why the Agent can grow successfully while continuing to select only basic
services. See the official [Game Progression feature highlight](https://www.paradoxinteractive.com/zh-CN/games/cities-skylines-ii/features/game-progression).

Local 1.6.0f1 `Game.dll` inspection confirms stable candidate seams:

- `Game.Simulation.MilestoneSystem` exposes current XP, next milestone and required XP.
- `MilestoneLevel` and `MilestoneData` expose the achieved index, rewards,
  Development Points, map tiles and XP requirement.
- `Game.City.DevTreeSystem.points` exposes unspent points.
- `DevTreeNodeData` contains cost and owning service; `DevTreeNodeRequirement`
  contains prerequisite nodes.
- `DevTreeSystem.Purchase(Entity)` validates points, service availability,
  prerequisites and locked state, then emits the normal unlock event.

### Proposed tools

`get_progression` should return:

- achieved and next milestone, current/required XP and milestone rewards;
- unspent Development Points;
- services and development nodes with name, entity, cost, locked state and
  prerequisite names;
- which locked prefabs each node will make available, where discoverable.

`purchase_development_node(name)` should resolve a node by name and call the native
`DevTreeSystem.Purchase` path. It must spend legitimately earned points and report
why a node is ineligible. Do not call `UnlockAllMilestones` or force-disable `Locked`:
the mayor should participate in progression, not bypass it.

The gameplay policy can then choose nodes from current bottlenecks: waste processing
when landfill service is strained, advanced electricity when operating cost or
capacity is limiting growth, and transport when traffic volume warrants it.

### Acceptance

- Read output matches the Progression UI on a normal locked save.
- Purchasing an eligible node reduces points by its declared cost and unlocks the
  same assets as a manual UI purchase.
- Insufficient points and missing prerequisites leave state unchanged.
- Save/reload preserves the purchase.

### Implemented and live-accepted

The progression tool group now contains `get_progression` and
`purchase_development_node`. Purchases resolve a node by name and use the native
`DevTreeSystem.Purchase` path; ECS entity ids are not part of the model-facing
interface.

The first live read exposed an interface defect: returning all 71 nodes expanded
the turn context from about 4k to 63k tokens. The default response now returns only
the unlocked frontier plus purchased-node and locked-service summaries; `service`
explicitly expands one service tree. The same live city returned six frontier
nodes, three purchased nodes and 71 total nodes in a 3.6 KB result. Purchase remains
to be accepted after the city earns a Development Point; the zero-point rejection
path is represented directly in each node's blockers.

## 3. Current Tool-module review

### Evidence boundaries

The historical timeline corpus contains 47 JSONL files, 4,134 calls and 42
sessions from August 8-10. Those totals span multiple catalog/handler revisions and
must not be treated as failure rates for current code. For current behavior, the
authoritative historical comparison is the latest earlier session whose recorded
catalog SHA and handler MVID match the then-deployed source; the fresh-map session
above is the authority for this change.

The `e53c750` catalog reviewed against the historical corpus had 49 tools; the
catalog after the landfill expansion has 53. A normal turn exposes 12 core tools plus five
agent/meta tools; optional groups expose construction, finance, progression,
district and visual capabilities only when requested. Group gating reduces model
schema load, but it does not by itself make the Tool module deep.

### Depth assessment

The execution host is deep in several places:

- `place_building` hides road-facing search, rotation, native validation and short
  utility connector construction behind one call;
- zoning hides Zone Cell mutation order and the `ToolUpdate` barrier;
- `wait_simulation` owns speed/pause restoration instead of requiring polling;
- aggregate perception tools translate ECS state into city concepts.

The model-facing Tool module as a whole is still shallow. The caller must compose
too much map, prefab, geometry and retry knowledge across a 49-tool surface, while
the same tool/group/route knowledge is repeated in `ToolCatalog.json`,
`AgentToolSurface`, the route switch, handlers and gameplay skills. The fresh-map
loop made this visible: native tool execution was normally 10-500 ms, but the first
write arrived after roughly three minutes and 34 tool calls because the model had
to assemble a starter-site plan from `terrain`, `gridmap`, roads and prefab search.
`find_prefabs("Water")` was especially weak: it returned 50 mostly unrelated
waterfront growables from 530 matches before narrower queries found the two useful
pumps.

The first discovery phase is now implemented: `find_prefabs(role=...)` classifies
infrastructure and services from typed prefab ECS components instead of names. The
next deepening opportunity is a read-only starter-site/infrastructure candidate Tool
that owns the knowledge needed to turn an outside connection and map resources into
a small set of native-validated road/utility candidates. A second opportunity is
generating group membership and routing metadata from one catalog source so tool
knowledge stops leaking across modules.

Fresh-map acceptance on disposable city `阿什比` confirmed that `role=water`,
`role=sewage`, `role=power`, and `role=garbage` return the corresponding typed
service prefabs without the earlier waterfront-growable contamination. This is a
discovery improvement, not yet the stronger candidate planner described above.

### Tools with no observed historical calls

Against the 49 names present at the `e53c750` audit baseline, these tools had no
calls in the 47-file corpus:

`create_district`, `district_policies`, `get_fees`, `get_loan`, `list_districts`,
`policies`, `set_district_policy`, `set_fee`, `set_loan`, `set_policy`, and
`upgrade_road`.

This is not evidence that all eleven should be deleted. District tools were never
exposed because the districts group was enabled zero times; finance was enabled
only three times, versus construction 49 times. `upgrade_road` was different: it
was in the frequently enabled construction group, but its implementation only
applied composition features and could not widen/change a road type. The canonical
model-facing name is now `set_road_features`; the old name remains only as a
deprecated compatibility alias. True road-prefab replacement is a separate
disposable-map spike. `agent_add_context_block` and `agent_remove_context_block` also
had no observed calls; map-pin ownership currently sits with the player/UI, so
model-side mutation is not part of the common mayor workflow.

### Road semantics and live acceptance

The composition operation is now exposed canonically as `set_road_features`; the
old `upgrade_road` name remains a catalog-only deprecated alias and is absent from
the model-facing construction group. A separate experimental `replace_road_type`
uses the native replacement definition path and deliberately accepts only a simple,
ownerless, non-fixed standalone edge.

On disposable city `阿什比`, an isolated 80 m `Small Road` at
`(-740, -700) -> (-660, -700)` was replaced with `Small Road Asymmetric`. A refreshed
road read reported the new prefab with the same endpoints and length. Applying
`grass,lighting` through `set_road_features` then succeeded without changing the
prefab, width, or lane layout. This proves the restricted standalone case only;
intersection/chain behavior and save/reload persistence remain unaccepted.

## 4. Road hierarchy policy

The official road catalog groups roads into small, medium, large and highway
categories and supports ramps, one-way/asymmetric roads and intersection controls:
[Road Tools](https://www.paradoxinteractive.com/games/cities-skylines-ii/features/road-tools).
The Traffic AI documentation recommends identifying high-volume main roads through
traffic/road views and upgrading those roads, while the Traffic Routes feature
shows neighborhood traffic collecting onto a main road:
[Traffic AI](https://www.paradoxinteractive.com/games/cities-skylines-ii/features/traffic-ai),
[Detailer's Patch #2](https://www.paradoxinteractive.com/games/cities-skylines-ii/news/detailers-patch-2).

The actionable policy now lives in
[`Mod/Agent/Skills/city-building/SKILL.md`](../../Mod/Agent/Skills/city-building/SKILL.md):

```text
highway/outside connection -> arterial -> collector -> local street
```

The Agent should zone local streets, collect them onto several medium-road routes,
and reserve larger roads for cross-district traffic. Industrial and garbage freight
needs a short collector route toward an arterial/highway instead of crossing
residential local streets. This is a default planning discipline, not a rigid tree:
each district still needs alternate paths so all traffic is not forced through one
collector junction.

A future perception improvement could expose road traffic volume and Traffic Routes.
Until then, the Agent can apply hierarchy while expanding and use bottleneck
notifications/locations as coarse evidence for targeted alternate connections.

## 5. Trees and growable buildings

This is already closed at the policy layer. The live zoning run established that
trees, bushes and ruins do not prevent a valid growable from appearing; construction
clears them. The city-building skill already directs the Agent to inspect road
adjacency, registered zoning, demand and outside connectivity instead of demolishing
vegetation. No new Tool is required for this issue.

## 6. Specialized industry areas

The official production description says specialized extraction is created by
placing a specialized-industry hub, which activates an area tool. The extraction
area is a closed polygon of corner nodes, starts with a small default area, must stay
within the hub's range and harvests resources under that area. Grain, vegetable and
cotton farming require fertile land; forestry requires forest; coal/ore and oil need
their matching deposits; livestock and stone can operate without a resource deposit.
See [Economy & Production](https://www.paradoxinteractive.com/games/cities-skylines-ii/features/economy-production).

### Required capability chain

1. Perceive natural-resource coverage and accessible road frontage.
2. List specialized-industry hubs with lock state and resource requirement.
3. Place the hub next to a road.
4. Define or resize its closed extraction polygon.
5. Verify employees, extraction vehicles, production and resource balance after
   simulation advances.

The first implementation should use the game's Area Tool lifecycle during
`ToolUpdate`, not directly manufacture area entities from request handlers. Local
assembly inspection shows `Game.Tools.AreaToolSystem` owns create/modify/add/move/
remove/complete-area states and operates on `Game.Areas.Node` buffers. That is the
same kind of update-order seam that made zoning sterile when bypassed.

Possible interface:

```text
place_specialized_industry(prefab, x, z, area_points=[{x,z}, ...])
```

If atomic placement plus area definition proves too fragile, split it into hub
placement and a general `set_operational_area(building, points)` operation, but keep
the polygon/owner bookkeeping inside the Tool.

### Acceptance

- The chosen polygon closes and remains owned by the hub after save/reload.
- Resource-dependent extractors reject or warn about barren coverage.
- Real extraction vehicles and production appear; placing only the decorative hub
  is not success.
- Production/exports and additional truck traffic become visible to the Agent.

### Resource-aware expansion implemented; live acceptance pending

`expand_operational_area(building, target_area_m2)` now accepts an owner-linked
extractor Lot as well as landfill storage. It evaluates every clear fan candidate
against the live 256×256 natural-resource map by polygon/cell intersection, ranks
same-resource candidates by estimated remaining amount and coverage, and returns
that evidence with the native apply result. Fertile land, ore, oil and fish use this
path. Forest is deliberately rejected until the separate tree-entity scorer is
implemented; tree wood is not comparable to cell-map resource amounts. The write
path builds and deploys, but a real specialized-industry hub/resource/production
loop has not yet been run.

## 7. Landfill storage area size

The official garbage overview confirms that landfills store and slowly process
garbage and can consume significant land:
[City Services — Garbage Management](https://www.paradoxinteractive.com/games/cities-skylines-ii/features/city-services-districts-policies).
The in-game capacity is not only a fixed building number. Local 1.6.0f1 inspection
shows:

- a garbage facility owns area entities through `Game.Areas.SubArea`;
- each area stores polygon nodes in `Game.Areas.Node` and derived surface geometry
  in `Game.Areas.Geometry`;
- landfill contents live in `Game.Areas.Storage`;
- `GarbageFacilityAISystem.ProcessAreas` calculates capacity with
  `AreaUtils.CalculateStorageCapacity(geometry, StorageAreaData)`.

Therefore a later implementation should resize the owned storage polygon and allow
the game to recalculate geometry/capacity. It must not fake capacity by editing
`Storage.m_Amount` or the prefab's fixed garbage values.

The shared lower-level seam is the same owner-linked area transaction needed by
specialized industries. The landfill-facing interface is intentionally stronger and
narrower: `expand_operational_area(building, target_area_m2)` owns polygon selection,
direction, node insertion and locked-edge preservation instead of asking the model
to manufacture corner coordinates.

The read-only precursor `get_operational_area(building)` is now implemented and
live-accepted. A forced empty `Landfill01` on disposable city `阿什比` exposed 28
owned subareas while classifying only its owner-linked `Landfill Site Lot` storage
area as editable. That polygon had four nodes, a 3,264 m² surface, zero stored
garbage, and 51,000 capacity calculated through the game's `AreaUtils`; the other
27 decorative/surface areas remained non-editable. No polygon was modified during
this acceptance.

The first write path was live-accepted on the same disposable landfill. A 16 m request
kept the building-side edge at `z=-572`, moved the free edge from `z=-548` to
`z=-532`, increased surface area from 3,264 to 5,440 m², and increased native
capacity from 51,000 to 85,000. The area remained the only editable owner-linked
storage area. After saving as `ToolLoop-OperationalArea` and cold-loading it, the
same four nodes, 5,440 m² surface, 85,000 capacity, `editable=true`, and locked edge
were still present. Entity ids changed across reload, so the model-facing interface
correctly continues to identify the building rather than persisting ECS ids.

The interface has since been deepened so the model supplies only the desired total
surface. The handler preserves the building-side edge, generates centered and
skewed sector candidates, adds perimeter nodes, terrain-projects them, prefilters
unowned land/building/road crossings, enforces the native effective 4 m adjacent-node
spacing, and submits the selected full ring to native validation. Repeated live
expansion from 7,713.7 m² produced an exact 9,000.0 m², 11-node area with 140,626
capacity while preserving the locked edge. The disposable save is
`ToolLoop-FanArea`. Cold reload acceptance is not yet claimed: under the current
virtual-display session both that save and the previously accepted four-node save
enter the same long, all-core load after deserialization, so the differential does
not isolate the fan geometry.

### Acceptance

- Expanding the polygon increases reported landfill capacity in the UI and ECS.
- Existing stored garbage is preserved.
- Shrinking below the occupied requirement is rejected by the game or returned as a
  clear validation error.
- Trucks continue operating and the resized area survives save/reload.

## Suggested implementation order

1. Add a read-only starter-site candidate Tool so early-city setup does not require
   dozens of perception/search calls.
2. ~~Live-accept `find_prefabs(role=...)`, `set_road_features`, and the read-only
   building-owned operational-area diagnostic.~~ Accepted on `阿什比`.
3. ~~Restricted native road-type replacement spike on a disposable map.~~ The
   standalone-edge case is accepted; save/reload and broader topology are pending.
4. ~~Native operational-area expansion spike using the empty `阿什比` landfill.~~
   Accepted through cold save/reload; non-empty storage and active trucks remain.
5. Specialized-industry placement built on the proven area seam.

Keep each change independently hot-reloadable when it stays inside request handlers,
catalog or skills. Changes to `BridgeToolSystem`, ECS scheduling or the area-operation
host require a main build and cold acceptance.
