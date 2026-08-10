# Tool deepening: next seams

**Date:** 2026-08-11
**Scope:** research only; no Mod behavior was changed
**Current source anchor:** `e53c750f4d818afc0363806e1029ef8c3df22e80`
**Installed game anchor:** Steam build `23700737`; `Game.dll` SHA-256 `721E7E17BF74299AA2B988C1BD07E90874BB8BC72D263229500C4BF639E7E4EE`

## Recommendation

The next useful investment is not another loop policy. It is a smaller Tool surface backed by deeper implementations:

1. Add ECS-backed `role`/`capabilities` filtering to prefab discovery first, then add a read-only candidate planner that owns placement preflight and ranking.
2. Narrow the current `upgrade_road` to `set_road_features` (or `decorate_road`) and retain `upgrade_road` as a deprecated compatibility alias. Treat road-prefab replacement as a separate, unproven tool.
3. Add `inspect_operational_areas(building)` before writing areas. Then prove one owner-linked landfill expansion on a disposable map through the native `GenerateAreas -> Validation -> ApplyAreas` pipeline. Use the same eventual write interface for storage and extractor areas.

These are deep-module seams: the model supplies domain intent; the Tool implementation owns ECS taxonomy, entity discovery, geometric invariants, native transaction construction, validation, and concise evidence. The resulting interface is smaller even though the implementation becomes more capable.

## Evidence discipline

This note keeps three kinds of evidence separate:

- **Current implementation:** source at commit `e53c750`, especially [RequestHandlers.Build.cs](../../Mod/CS2MCP/RequestHandlers.Build.cs), [BridgeToolSystem.cs](../../Mod/CS2MCP/BridgeToolSystem.cs), and [ToolCatalog.json](../../Mod/Agent/ToolCatalog.json).
- **Installed-game evidence:** types and methods decompiled from the installed `Game.dll` identified above. Type names and ILSpy output locations are listed below so they can be reproduced against the same binary; this is not a claim about other game builds.
- **Design inference:** proposed interfaces and rollout stages. These are not established native behavior until their listed in-game gates pass.

The 2026-08-10 loop log reported that `find_prefabs("Water")` produced 530 matches and that the first 50 were mostly irrelevant growables. That is **historical runtime evidence**, not a current API guarantee. It remains relevant because the current `GetPrefabs` implementation and schema still expose only broad category plus name substring; the classification seam did not change in `e53c750`. See [the backlog](../ops/2026-08-10-gameplay-capability-backlog.md).

## 1. Prefab roles and starter-site candidates

### Current module and leakage

`GetPrefabs` currently queries four broad structural sets: `BuildingData`, `RoadData`, `NetGeometryData`, and `TreeData`. Its interface accepts `category`, a name substring, and `limit`, then returns name/type/lock state and a few dimensions ([source, `RequestHandlers.Build.cs` lines 251-350](https://github.com/lulu0119/cities-skylines-2-agent/blob/e53c750f4d818afc0363806e1029ef8c3df22e80/Mod/CS2MCP/RequestHandlers.Build.cs#L251-L350); [schema, `ToolCatalog.json` lines 421-464](https://github.com/lulu0119/cities-skylines-2-agent/blob/e53c750f4d818afc0363806e1029ef8c3df22e80/Mod/Agent/ToolCatalog.json#L421-L464)).

This is a shallow interface for gameplay discovery. The model must know naming conventions, perform several searches, distinguish service buildings from growables, check lock state, and then compose map/terrain/road queries. The same placement knowledge already exists lower down in `PlaceBuilding`, `IsCandidateBuildable`, and `ResolveAutoConnect`, so discovery and execution currently leak related knowledge across Tool calls.

### Stable ECS capabilities available in the installed build

The following are typed prefab ECS components, not name heuristics:

| Domain capability | Component/type evidence | Suggested public role |
| --- | --- | --- |
| Water supply | `WaterPumpingStationData`, `WaterTowerData` | `water_supply` |
| Sewage handling | `SewageOutletData`, `WastewaterTreatmentPlantData` | `sewage` |
| Electricity generation | `PowerPlantData`; `WindPoweredData`, `SolarPoweredData`, and `GroundWaterPoweredData` refine generation mode | `electricity_generation` plus capabilities |
| Electricity transformation | `Transformer` adds its typed prefab data | `transformer` |
| Road | `RoadData` | `road` |
| Generic city service | `ServiceObjectData.m_Service -> ServiceData.m_Service` | `service`, with service kind metadata |
| School / hospital / garbage | `SchoolData`, `HospitalData`, `GarbageFacilityData` | service-specific roles |
| Growable building | `SpawnableBuildingData` | `growable` |
| Specialized-industry hub | `ExtractorFacilityData` | `specialized_industry` |

`BuildingData` and `NetGeometryData` are too broad to be public gameplay roles. A prefab may have several capabilities, so the implementation should return a capability set rather than force every prefab into one mutually exclusive category. Human-readable names should remain labels, not classification inputs.

Reproducible installed-assembly locations:

- `Game.Prefabs.WaterPumpingStation` ILSpy output lines 22-50 adds `WaterPumpingStationData`.
- `Game.Prefabs.SewageOutlet` lines 21-48 adds `SewageOutletData`.
- `Game.Prefabs.PowerPlant` lines 17-46 adds `PowerPlantData`/`ElectricityProducer`; `WindPowered` lines 22-47 and `GroundWaterPowered` lines 21-45 add refinements.
- `Game.Prefabs.SpawnableBuildingData`, `ServiceObjectData`, `ServiceData`, `ExtractorFacilityData`, `RoadData`, `SchoolData`, `HospitalData`, and `GarbageFacilityData` expose the remaining markers and metadata.

Example reproduction command:

```powershell
ilspycmd -t Game.Prefabs.WaterPumpingStation "C:\SteamLibrary\steamapps\common\Cities Skylines II\Cities2_Data\Managed\Game.dll"
```

### Minimal implementable phase

**Phase 1 should deepen discovery only.** Extend prefab discovery with a bounded, typed interface such as:

```text
find_prefabs(role, unlocked_only=true, limit<=10)
  -> [{ prefab, roles[], capabilities{}, footprint, locked }]
```

An internal `PrefabRoleClassifier` can own all component queries and service-kind resolution. It need not be a new public module or a pass-through class: its value is one local owner for evolving ECS taxonomy. Tests should exercise the public role semantics, not mirror every Unity component.

This phase is immediately implementable because it is read-only, is backed by current ECS markers, and does not change construction semantics. It also supplies measurable evidence about which roles have zero or ambiguous unlocked candidates.

**Phase 2 should add a read-only candidate planner**, for example:

```text
find_infrastructure_candidates(role, center?, radius?, limit=3)
  -> [{ prefab, x, z, rotation, evidence[], warnings[] }]
```

The implementation should own role filtering, owned/unlocked checks, terrain/resource prerequisites, footprint clearance, road frontage, shoreline needs, and deterministic ranking. Return a few legal candidates with evidence, not a large coordinate cloud. The current building placement preflight is a useful starting implementation, but a candidate is only "legal" after the same native validation used by the eventual write Tool.

Airicraft demonstrates the relevant deep-tool shape in a different game: `find_placement_sites` checks support/state/clearance/standability/range and returns ranked actionable sites ([source](https://github.com/shinohara-rin/airicraft/blob/10f95ab4bcb259209b263fa2e1a09525fc0f902c/src/client/java/ai/moeru/airicraft/agent/llm/CurrentWorldQueryService.java#L149-L233)); `find_world_features` hides flood/search/filter/ranking and returns targets plus evidence and confidence ([entry](https://github.com/shinohara-rin/airicraft/blob/10f95ab4bcb259209b263fa2e1a09525fc0f902c/src/client/java/ai/moeru/airicraft/agent/llm/WorldFeatureSearchService.java#L68-L105), [ranking](https://github.com/shinohara-rin/airicraft/blob/10f95ab4bcb259209b263fa2e1a09525fc0f902c/src/client/java/ai/moeru/airicraft/agent/llm/WorldFeatureSearchService.java#L509-L535)). This is structural precedent, not Cities: Skylines II API evidence.

The action-plan-advisor project likewise keeps deterministic candidate ordering and bounded route search behind a small advice interface ([ranking source](https://github.com/shinohara-rin/action-plan-advisor/blob/fce80786a95128965a4a1863736b5f464a1d1a03/src/main/java/ai/moeru/actionplan/BackwardChainingPlanAdvisor.java#L13-L17), [bounded selection](https://github.com/shinohara-rin/action-plan-advisor/blob/fce80786a95128965a4a1863736b5f464a1d1a03/src/main/java/ai/moeru/actionplan/BackwardChainingPlanAdvisor.java#L101-L124)). It contains no CS2 spatial implementation.

**Do not make Phase 1 an atomic `build_starter_site` Tool.** Atomic multi-build introduces rollback and partial-success semantics before candidate quality is proven. After Phases 1-2 are stable, a higher-level Tool may orchestrate the same deep primitives without exposing their coordinate-search loop.

### Depth assessment

- **Module:** role-aware prefab/candidate discovery.
- **Interface:** one domain role, an optional search region, and a small bound.
- **Implementation:** ECS taxonomy, lock/ownership checks, spatial preflight, ranking, and evidence.
- **Seam:** between gameplay intent and Unity-specific discovery, not between each query step.
- **Leverage:** every placement/orchestration Tool benefits from one classification and ranking policy.
- **Locality:** component-version changes and ranking changes remain in one implementation.
- **Depth:** high if it returns actionable candidates; low if it merely renames `query` or exposes component flags to the model.

## 2. Road features versus road-type replacement

### What the current Tool actually does

`upgrade_road` maps grass, trees, wide sidewalk, sound barrier, parking, lighting, median grass, and median trees into `CompositionFlags`, then calls `TryQueueUpgrade` ([handler lines 689-757](https://github.com/lulu0119/cities-skylines-2-agent/blob/e53c750f4d818afc0363806e1029ef8c3df22e80/Mod/CS2MCP/RequestHandlers.Build.cs#L689-L757)). `BridgeToolSystem.CreateModifyDefinitions` creates an upgrade definition for the original edge and optionally adds `Game.Net.Upgraded`; there is no target road prefab. The Tool cannot change width, road type, or lane layout.

The official Road Tools description uses "Replace" for both changing an existing road and adding grass, trees, wide sidewalks, or sound barriers, while also explaining that roads are organized into distinct types ([official source](https://www.paradoxinteractive.com/games/cities-skylines-ii/features/road-tools)). The game UI label therefore should not be copied directly into an agent schema: the native data paths contain two different bodies of knowledge.

### Native replacement pipeline in this installed build

`Game.Tools.NetToolSystem` contains a real `Mode.Replace` pipeline:

- ILSpy lines 1493-1540 read the selected target `m_Prefab` and its net/road/geometry/placeable data, construct a path for the original road, and add control points.
- Lines 2617-2623 call `CreateReplacement`.
- `CreateReplacement` constructs a `CreationDefinition` with `m_Original`, target `m_Prefab`, optional `m_SubPrefab`, alignment/sub-elevation/inversion flags, and a `NetCourse` that preserves the curve and endpoint entity/position/rotation/delta knowledge.
- Lines 3087-3141 retain bookkeeping for subnets.
- `Game.Tools.ApplyNetSystem` lines 372-415 handles `TempFlags.Replace` by deleting/replacing the original; the non-replace path instead updates data on the original.

This proves that native road-type replacement exists. It does **not** prove that the current bridge can reliably replace an arbitrary selected edge. Compatibility of required layers, connected nodes, intersections, fixed elements, owner/subnet relations, invert state, elevations, and native error propagation still has to be carried through.

### Interface decision and compatibility

Make the existing semantic contract explicit:

```text
set_road_features(index, version, grass?, trees?, wide_sidewalk?, ...)
```

`decorate_road` is acceptable, but `set_road_features` is more precise because parking and lighting are not merely decoration. Keep `upgrade_road` as a deprecated alias to the same handler for at least one compatibility window, remove it from the default prompt/catalog surface, and report the canonical replacement name in results. Do not silently change the alias to road-prefab replacement later.

Reserve a separate future interface:

```text
replace_road_type(index, version, prefab)
```

Separating the interfaces creates two coherent modules: feature flags own composition knowledge; replacement owns topology and prefab compatibility. A combined schema would be shallow because every caller would have to understand which fields select mutually different native pipelines.

### Required replacement spike

On a disposable map, first support one standalone, non-owned, non-fixed edge and reject intersections, subnets, and multi-edge selections. Port the native replacement definition rather than only assigning `PrefabRef`. Verify before/after prefab, geometry, endpoint connectivity, lane entities, navigation, zoning cells, simulation traffic, save/reload, and undo behavior if the bridge promises it. Generalize only after those observations agree with the native UI result.

Until that gate passes, road replacement is **feasible but not reliable**. Renaming/narrowing the current Tool does not depend on that spike.

## 3. Operational areas and specialized industry

### Shared native area pipeline

Landfill storage polygons and specialized-industry extraction polygons share the native `AreaType.Lot` creation/edit/apply machinery. They differ mainly in prefab data and simulation consumers:

```text
building SubArea ownership
  -> AreaToolSystem definition + full Node buffer
  -> GenerateAreasSystem
  -> area Validation / Geometry
  -> ApplyAreasSystem
  -> Storage or Extractor simulation consumer
```

Installed-assembly evidence:

- `Game.Areas.SubArea` is a building buffer element pointing to the owned area entity; `Game.Prefabs.ObjectSubAreas` supplies prefab subareas/nodes and adds the runtime buffer.
- `Game.Tools.ObjectToolSystem` lines 3932-3962 activates `AreaToolSystem` after placing a non-overlapping Lot subarea, linking the building placement flow to area editing.
- `Game.Tools.AreaToolSystem` defines Edit/Generate and Create/Modify/Remove states. Lines 3504-3590 create and complete polygons; lines 3555-3559 identify Lot areas with `ExtractorAreaData` or `StorageAreaData` as specialized-industry-style areas; lines 3825-3885 enqueue definitions rather than mutating runtime areas directly.
- Its edit definition carries `CreationDefinition.m_Original`, `m_Prefab`, `CreationFlags.Relocate`, the actual `m_Owner`, and a complete `Node` buffer. For Lot areas the first edge is treated specially; `AreaUtils.GetMinNodeDistance(AreaType.Lot)` is 8 metres.
- `Game.Tools.GenerateAreasSystem` lines 151-342 generates/reuses an area from the definition; lines 356-415 generate prefab subareas and write the owner. When modifying an original area it preserves original prefab/storage state and creates the temporary relocate representation.
- `Game.Tools.ValidationSystem` lines 1010-1044 calls `Areas.ValidationHelpers.ValidateArea`; geometry generation also reports invalid/no-triangle states.
- `Game.Tools.ApplyAreasSystem` lines 35-69 reconciles owners and `SubArea` references; lines 107-201 apply create/update/delete and copy nodes back. Existing owned-area edits retain the operational relationship rather than leaving a detached polygon.
- `Game.Prefabs.StorageAreaData` and `Game.Areas.Storage` provide capacity/storage state. `AreaUtils.CalculateStorageCapacity` derives capacity from surface area; `GarbageFacilityAISystem` walks building subareas and consumes that capacity.
- `Game.Prefabs.ExtractorAreaData` carries `MapFeature`, maximum area, and natural-resource requirement. `AreaResourceSystem` computes resource amounts/concentration from polygon nodes/triangles and the natural-resource map. `ExtractorCompanySystem` lines 214-240 reads building `SubArea` state, and lines 413-439 use area and remaining resources in production.

The official economy/production description independently confirms the product behavior: placing a specialized-industry hub activates an area tool, the player closes a corner-node loop within building range, and vehicles work/extract inside that area; resource-dependent industries require the corresponding natural resource ([official source](https://www.paradoxinteractive.com/games/cities-skylines-ii/features/economy-production)).

The current `BridgeToolSystem.CreateAreaDefinitions` writes a standalone prefab and nodes without an owner or original area ([source lines 1095-1111](https://github.com/lulu0119/cities-skylines-2-agent/blob/e53c750f4d818afc0363806e1029ef8c3df22e80/Mod/CS2MCP/BridgeToolSystem.cs#L1095-L1111)). That is sufficient for its current district use, but reusing it for landfill/extractor polygons would create an area-shaped entity without proving that the facility owns or operates it.

### First read-only diagnostic

Add one diagnostic with a building-oriented interface:

```text
inspect_operational_areas(building_index, building_version)
  -> {
       building, areas: [{
         area, kind, prefab, nodes, locked_edge,
         surface_area, validation_flags, warnings,
         storage?, extractor?
       }]
     }
```

For storage, include current amount and calculated capacity. For extraction, include map feature, natural-resource requirement, resource amount, and maximum concentration. The caller should not have to find the area entity, owner, prefab, node order, or relevant simulation component. If zero or multiple matching areas exist, return a structured diagnostic rather than guessing.

This diagnostic is the highest-leverage first step: it proves ownership discovery and supplies before/after evidence for every later write spike without creating a second write path.

### Disposable-map write spike

Use an empty landfill whose storage amount is zero:

1. Resolve its owned `Storage` area through the building's `SubArea` buffer.
2. Preserve the first two Lot nodes exactly and expand by moving/adding nodes from index 2 onward. Normalize duplicate closing points and winding; require at least three distinct nodes, a simple polygon, at least 8 m node spacing, owned land, facility range, and no unsafe overlap.
3. During `ToolUpdate`, enqueue a relocate definition containing the original area, the owner building, existing prefab, and full node buffer. Keep `ToolOutputBarrier` and `GetAllowApply` as the transaction/validation boundary.
4. Compare the diagnostic before and after. Require increased surface area/capacity, unchanged stored amount, no geometry/tool errors, normal garbage simulation, and persistence after save/reload.

Version 1 should allow expansion only. Shrinking a non-empty landfill can reduce computed capacity below stored amount; defining that case out of existence is a deeper interface than distributing overflow policy across callers. A later shrink mode can explicitly require computed capacity greater than or equal to current storage.

After storage passes, repeat with one specialized-industry hub over the correct natural resource. Success requires an owner-linked extractor area, non-zero/effective resource concentration when appropriate, active facility production/vehicles, and save/reload persistence. A visible polygon alone is not success.

### Eventual unified write interface

```text
set_operational_area(building_index, building_version, points, mode="expand")
  -> { kind, area, before, after, evidence[], warnings[] }
```

The implementation resolves exactly one operational subarea, preserves the locked edge, normalizes geometry, checks facility range and resource requirements, and uses the native definition/validation/apply pipeline. `expected_kind` may be an optional optimistic safety guard, but the model should not provide area entity, owner, area prefab, winding, elevation sentinel, or command-buffer details.

This is a deep general-purpose module because storage and extraction share geometry, ownership, validation, and application knowledge. Specialized simulation checks remain internal strategies selected by the resolved area kind. It has higher locality and leverage than separate `resize_landfill` and `draw_extractor_polygon` implementations that duplicate the native area protocol.

Do not initially combine building placement and area editing into one public atomic operation. The native flow is two transactions, so such an interface would imply rollback semantics the bridge does not yet provide. Once both primitives are reliable, `place_specialized_industry` may orchestrate them internally and report partial failure explicitly or implement real compensation.

## Decisions

### Can be made now without product input

| Decision | Reason |
| --- | --- |
| Implement read-only ECS role/capability filtering first | Current typed components support it; no game mutation or product autonomy policy changes. |
| Keep name matching only as optional text refinement | Names are useful labels but unstable role classifiers. |
| Bound and deterministically order candidate results | Reduces Tool tokens and makes repeated calls inspectable. |
| Rename the canonical road-feature operation | Current behavior is unambiguously composition flags, not road-type replacement. |
| Keep road replacement separate and experimental | It uses a materially different native pipeline and has unresolved topology risks. |
| Build the operational-area diagnostic before writes | It is required evidence for both landfill and extractor work. |
| Use owner-linked native area definitions, not standalone district definitions | Ownership is part of operational correctness. |
| Make operational-area expansion the only v1 write mode | It defines the unsafe occupied-storage shrink case out of existence. |

### Product decisions for the user

| Decision | Recommended default | Consequence |
| --- | --- | --- |
| Canonical name: `set_road_features` or `decorate_road` | `set_road_features` | More precisely includes parking/lighting/wide sidewalk; `decorate_road` is friendlier but slightly narrower than behavior. |
| Compatibility lifetime for `upgrade_road` | One release as a deprecated hidden alias | Preserves old prompts/log replays without perpetuating the misleading default surface. |
| Starter-site autonomy | Read-only ranked candidates first | Lets live-loop evidence establish candidate quality before multi-build/rollback semantics are promised. |
| Whether v1 may shrink an occupied landfill | No | Avoids choosing an implicit waste-loss/overflow policy. |
| Specialized-industry public UX | Two-step primitives first; add orchestration later | Keeps transaction boundaries truthful while the native placement-to-area handoff is being proven. |

None of these product choices blocks the first research-driven implementation step: read-only role filters and operational-area diagnostics.

### Real-game verification gates

| Gate | Required observation |
| --- | --- |
| Role filter | Representative unlocked/locked prefabs appear under correct roles; growables do not contaminate water/sewage/service queries; capability metadata matches the UI/prefab behavior. |
| Candidate planner | Returned sites pass native placement validation and repeated runs rank deterministically; rejection evidence explains shoreline, frontage, overlap, resource, or ownership failures. |
| Road-feature rename | Canonical tool and legacy alias produce identical feature flags, visual result, and save/reload state. |
| Road replacement spike | Prefab/type, topology, endpoints, lanes, navigation, zoning, traffic, and persistence match native Replace on the restricted edge class. |
| Landfill expansion | Same owned area entity/relationship remains operational; area/capacity increase; stored amount is preserved; trucks and save/reload behave normally. |
| Extractor area | Building owns the area; resource concentration is meaningful; production/vehicles operate; save/reload retains geometry and ownership. |

## Reference-project scope

- **Airicraft** supplies primary-source examples of strong candidate-search Tools and high-level planner surfaces, not CS2 ECS implementation. Its planner explicitly prefers a high-level action goal over low-level action tools ([source](https://github.com/shinohara-rin/airicraft/blob/10f95ab4bcb259209b263fa2e1a09525fc0f902c/src/client/java/ai/moeru/airicraft/agent/llm/PlannerToolCatalog.java#L135-L173)).
- **action-plan-advisor** supplies deterministic bounded candidate-selection structure, not spatial/game integration.
- **Cities2-MCP** describes itself as a knowledge corpus/local encyclopedia/mod-development workflow; no live gameplay mutation bridge for these three seams was found in the inspected revision ([README](https://github.com/mayor-modder/Cities2-MCP/blob/b455267256a3e395c38435acbf76fa15b958b42b/README.md#L5-L47)).
- **Apeira** defines model-independent Tool/schema/lifecycle abstractions and progressive Tool discovery, but no CS2 native replacement or area pipeline ([Tool guide](https://github.com/moeru-ai/apeira/blob/47508a5bbf4f7c493632686d372aa0ce91edc99c/docs/guide/tools.md#L1-L35), [lifecycle section](https://github.com/moeru-ai/apeira/blob/47508a5bbf4f7c493632686d372aa0ce91edc99c/docs/guide/tools.md#L119-L128)).

Negative findings are recorded to avoid treating architectural analogies as engine evidence. For these seams, the installed `Game.dll`, current bridge source, and live-game verification remain authoritative.
