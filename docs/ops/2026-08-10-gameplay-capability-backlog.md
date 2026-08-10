# 10k acceptance follow-up: gameplay capability backlog (2026-08-10)

**Status:** Findings and implementation candidates only. The 10k city is accepted;
none of the Tool/API gaps below are implemented by this note.

This is the next gameplay backlog after the new-map run reached population 10,228.
It separates missing game capabilities from mayor-policy knowledge so a later session
does not try to fix every symptom in the prompt.

## Priority summary

| Priority | Gap | Player-visible cost | Next seam |
| --- | --- | --- | --- |
| P0 | Circular-only zoning | Low frontage coverage and accidental overlap with occupied blocks | `zone_rectangle` request handled during `ToolUpdate` |
| P0 | No milestone/development-tree tools | Agent earns progression but keeps choosing basic services | Read progression + purchase an eligible development node |
| P1 | No building-owned area editing | Specialized industry hubs and landfill storage areas stay at their tiny defaults | Shared operational-area Tool over the native Area Tool pipeline |
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

## 3. Road hierarchy policy

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

## 4. Trees and growable buildings

This is already closed at the policy layer. The live zoning run established that
trees, bushes and ruins do not prevent a valid growable from appearing; construction
clears them. The city-building skill already directs the Agent to inspect road
adjacency, registered zoning, demand and outside connectivity instead of demolishing
vegetation. No new Tool is required for this issue.

## 5. Specialized industry areas

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

## 6. Landfill storage area size

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

The preferred seam is the same general `set_operational_area(building, points)` Tool
considered for specialized industries. It should resolve the building's owned
subarea, submit polygon edits through the native Area Tool pipeline, preserve the
building-facing locked edge, and return old/new surface area and capacity.

### Acceptance

- Expanding the polygon increases reported landfill capacity in the UI and ECS.
- Existing stored garbage is preserved.
- Shrinking below the occupied requirement is rejected by the game or returned as a
  clear validation error.
- Trucks continue operating and the resized area survives save/reload.

## Suggested implementation order

1. Rectangle zoning: smallest change with immediate coverage and safety benefit.
2. Read-only progression: lets the Agent understand why assets remain unavailable.
3. Development-node purchase: unlock advanced services through normal game rules.
4. Read-only natural-resource and building-owned-area diagnostics.
5. Native operational-area edit spike using landfill on a disposable new map.
6. Specialized-industry placement built on the proven area seam.

Keep each change independently hot-reloadable when it stays inside request handlers,
catalog or skills. Changes to `BridgeToolSystem`, ECS scheduling or the area-operation
host require a main build and cold acceptance.
