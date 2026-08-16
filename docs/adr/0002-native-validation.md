# Native validation, no bypass

Status: accepted

An in-game mayor that ignores the game's placement rules would not be playing Cities: Skylines II. Construction uses ordinary native validation. There is no Anarchy path, no typed error suppression, and no per-call `force` that unlocks locked content.

Local search and preflight may reject candidates that cannot reach the native transaction, but they must preserve native severity. In particular, ordinary growables marked `Overridable | DeleteOverridden` remain native warnings that the same apply transaction clears; an adapter must not promote them to hard building collisions or run a separate demolish-and-retry flow.

## Consequences

Failed writes stay failed. Recovery belongs inside the write tool (search, preflight, typed auto-connect), not in a second model-facing override.
