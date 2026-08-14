# Native validation, no bypass

Status: accepted

An in-game mayor that ignores the game's placement rules would not be playing Cities: Skylines II. Construction uses ordinary native validation. There is no Anarchy path, no typed error suppression, and no per-call `force` that unlocks locked content.

## Consequences

Failed writes stay failed. Recovery belongs inside the write tool (search, preflight, typed auto-connect), not in a second model-facing override.
