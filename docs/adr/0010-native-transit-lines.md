# Native route-tool apply for transit lines

Status: accepted

Transit writes enqueue on the simulation thread through the same Route Tool definition pipeline the player uses: `CreationDefinition` plus a closed `WaypointDefinition` loop, then `GenerateRoutesSystem` / native pathfinding / `ApplyRoutesSystem`. Line delete adds `Deleted` via `EndFrameBarrier`, matching `TransportationOverviewUISystem.DeleteLine`. Gameface `transportLines$` / `deleteLine` bindings stay UI-only. Stops are listed as existing `TransportStop` entities; they are not a `place_building` role.

## Considered Options

- **Call Gameface `deleteLine` / `selectLine` from the Agent.** Rejected: writes must run on the simulation thread with native validation, not Cohtml triggers.
- **Assemble route ECS components without ApplyRoutesSystem.** Rejected: that skips pathfinding validation (`PathfindFailed`, stop-type mismatch) and is not the native apply path.
- **Place stops through `place_building` with a transport role.** Rejected: a `TransportStop` is an object prefab, not a building place; [ADR-0003](0003-one-step-building-placement.md) already forbids a `role` argument on place.
