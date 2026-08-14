# Agent tool surface and player permissions

- **Date:** 2026-08-13
- **Status:** Accepted

## Context

The first playable tool surface accumulated preview tools, role-specific planning,
legacy HTTP-shaped errors, per-call `force` switches, and several write operations
that were always visible to the model. That made the interface wider than the
player workflow and leaked placement, progression, and validation policy into the
caller.

This decision records the product choices agreed during the 2026-08-12/13 tool
surface review. The intent is a small model-facing interface backed by deeper
modules that own prefab classification, search, native validation, and apply.

## Decision

### Building placement

The model-facing building write is:

```text
place_building(prefab, x, z, radius?, rotation?)
```

- `prefab` is the exact standalone prefab selected by the Agent from prefab
  discovery. The write tool does not accept a service `role`.
- `x/z` is the desired center. Normal autonomous placement should include a
  reasonable `radius` and omit `rotation`, allowing the implementation to resolve
  road frontage, shoreline orientation, clearance, and utility connections from
  prefab data.
- Omitting `radius` remains an exact-placement mode for an explicit player-selected
  site. If exact placement fails, the Agent should retry with a larger radius and
  no explicit rotation rather than repeat the same pose.
- The implementation takes one local ECS snapshot, generates a bounded internal
  candidate set, resolves independent prefab capabilities such as `RequireRoad`
  and `Shoreline`, preflights the complete rotated footprint against owned land,
  buildings, roads and water, and ranks valid poses deterministically by distance,
  frontage and required connector length. It sends only the best finalist through
  the native preview/apply state machine. There is no model-visible multi-candidate
  pool and no multi-candidate native probe.
- `find_placement` and `find_infrastructure_candidate` leave the model-facing
  surface. Their implementation may remain temporarily while compatibility and
  handler cleanup are completed.
- `place_building` rejects `ServiceUpgradeData`. Facility upgrades use the future
  `list_facility_upgrades(target)` /
  `set_facility_upgrade(target, upgrade, enabled)` interface and the native upgrade
  transaction.

### Native validation and progression

- The product uses normal Cities: Skylines II placement validation. It does not
  implement, expose, or depend on Anarchy/collision bypass, typed error suppression,
  or post-placement override protection.
- Per-call `force` parameters that bypass locked content are removed. An Agent may
  only construct content already unlocked by normal game progression.
- A player setting controls whether the Agent may spend legitimately earned
  Development Points. When disabled, progression remains readable but the purchase
  tool is hidden. Placement never purchases a node implicitly.
- Extractor resource acceptance is internal product policy, not a player setting.
  The first version rejects zero-resource candidates and ranks valid candidates by
  remaining resource evidence; resource-specific thresholds may be calibrated from
  live data later.
- Operational areas support expansion only. Shrinking is not part of the product
  interface and is not an active TODO.

### Destructive and development-only tools

- A player setting controls whether the Agent may demolish. When enabled, no modal
  confirmation is required; when disabled, the tool is hidden.
- Model-facing demolition is limited to buildings and road/network edges. Tree and
  plant clearing is deferred to a future spatial brush interface. District deletion
  must use a future district-specific interface rather than generic bulldoze.
- District creation and district policy mutation are removed from the current
  model-facing surface. Reliable polygon ownership and neighboring-boundary
  reasoning must be designed and accepted before they return.
- A default-off development/acceptance setting exposes diagnostic or unaccepted
  tools. It includes `replace_road_type`, `debug_zone_blocks`, and `save_game`.
  Development mode does not bypass progression or demolition permissions.
- `replace_road_type` remains a separate road-prefab replacement transaction and
  supports road-to-road replacement only. Road-to-rail or rail-to-road conversion
  will not be implemented. `set_road_features` remains separate because it edits
  composition flags without replacing the prefab. Tool names and descriptions
  should receive a later, coherent road-tool naming review.

### Roads and bridges

- `build_road` is the correct name for constructing a linear network from endpoints;
  `place_building` is the correct name for placing a discrete prefab and pose.
- Road prefabs accept `ground` and `grade-separated` modes. Omitting `mode`
  selects `ground`; non-road networks do not accept a road mode. `alignment`,
  `local_fit`, inferred endpoints, and hidden route recovery are not part of the
  interface.
- `ground` validates the final terrain-adjusted course with longitudinal samples
  spaced at most about 4 m, including water samples across the full road width and
  local centerline grades between adjacent samples. It rejects detected water
  crossings and grades above 10%, or above a stricter nonzero prefab slope limit.
  It never silently changes the route, moves its endpoints, or promotes the request
  to a bridge or tunnel. Native placement validation remains authoritative after
  this preflight.
- `grade-separated` is the explicit combined intent for bridges, elevated roads,
  and underground roads. It requires both endpoint elevations, with at least one
  nonzero; the native pipeline decides whether the requested segment is legal.
- The current road interface can construct ordinary straight or single-control-point
  curved elevated segments. It is not a reliable landmark-bridge planner: approach
  grades, multiple spans/piers, navigation clearance, symmetry, and whole-bridge
  topology are not owned by one interface today.
- A higher-level bridge-planning interface is a future design item. Do not add a
  shallow `build_bridge` pass-through until those invariants can be hidden inside a
  deep module.

### Spatial perception

- `terrain` returns a budgeted `LOCAL_MAP v1` semantic-vector view by default rather
  than a fixed 8x8 raw sample matrix. High-resolution height and water sampling,
  slope derivation, connected-region tracing, ownership classification, road
  topology, coordinate quantization, deterministic ordering, and output budgeting
  remain inside the perception module.
- The model-facing text contains a local coordinate frame, summary and directional
  sectors, connected water/steep/buildable/owned regions, real road nodes and edges,
  and explicit omission metadata. A temporary internal `format=samples`
  compatibility path retains the old 8x8 JSON response; it is not exposed in the
  Agent Tool interface.
- The compact map is spatial evidence, not construction approval. Native write-tool
  validation remains authoritative, and `candidate_buildable` only means owned,
  dry, and no steeper than the declared static slope band.
- Automatic shoreline/contour route generation, `alignment`, `local_fit`, inferred
  road endpoints, and hidden route recovery are not product capabilities.

### Model configuration and visual tools

- Remove the Provider selector. Runtime configuration consists of Endpoint, API
  key, and model name; the provider enum currently has no runtime behavior beyond
  filling an Endpoint preset.
- Model capabilities are resolved from model name only, never from Endpoint.
- Visual tools use a player setting with `Auto`, `On`, and `Off`:
  - `Auto` follows model-name capability matching.
  - `On` and `Off` are explicit player overrides.
  - An unknown model name is treated as non-visual in `Auto`.

### Runtime and data lifecycle

- The production runtime is MEAI `IChatClient` plus the hand-written loop; it is no
  longer provisional.
- An Agent session belongs to the current city load. Switching cities clears it.
  Save restoration and multiple concurrent sessions remain long-term goals.
- Runtime logs, screenshots, state, and development overlays live under
  `Cities Skylines II/ModsData/CitiesSkylines2Agent`. The product contains no legacy
  directory migration code because it has not shipped; development data is moved
  manually once.
- Logs and screenshots are retained until the player explicitly clears them; no
  automatic deletion policy is added in this decision.

## Consequences

- The common placement path is one write call rather than preview-then-commit.
- Player authority is represented by a few durable settings rather than model-chosen
  call parameters or modal dialogs.
- Native game validation remains authoritative, which keeps real-game acceptance
  representative of normal player behavior.
- Existing backend handlers may temporarily remain callable inside the process while
  the model-facing catalog is narrowed. They are compatibility/diagnostic
  implementation details, not promises to the Agent.
- Live acceptance must use a new city, exercise radius-based placement with omitted
  rotation, and verify that disabled permissions remove their write tools.

## Acceptance sequence

Real-game acceptance uses two separate new cities:

1. **Controlled capability city.** Verify each changed setting and tool, fix every
   discovered bug, and repeat from a new city when state contamination could affect
   the result. This save is not reused as the population benchmark.
2. **Long-running mayor city.** After the controlled checklist is clean, start a
   second new city and let the Agent operate without manual gameplay rescue until it
   reaches a clear practical limit: sustained progress stops, an unrecoverable tool
   or game failure occurs, or continued operation no longer changes the city
   materially.

The long run records maximum population and the reason it ended. Once population
passes 10,000, the audit specifically captures how the Agent responds to traffic:
which congestion evidence it reads, whether it establishes a road hierarchy,
changes junctions or road types/features, adds public transport, or repeatedly
widens/demolishes without improving measured flow. Preserve the relevant timeline,
tool calls, before/after road metrics, and screenshots. Do not give the Agent a
human-selected traffic solution during this benchmark.

## Deferred work

- Facility upgrade discovery and native upgrade writes.
- Tree/plant clearing through a spatial brush.
- Reliable district polygon creation and management.
- Landmark bridge planning.
- A coherent naming and description pass across road feature, road replacement, and
  road construction tools.
- Optional Python/Carto adapters and persistent/multi-session Agent state.
