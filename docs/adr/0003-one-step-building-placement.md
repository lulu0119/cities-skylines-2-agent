# One-step building placement

Status: accepted

Preview-then-commit leaked placement policy to the model and wedged the native tool after a rejected probe. The model-facing write is `place_building(prefab, x, z, radius?, rotation?)`: the implementation searches, preflights, and submits one finalist.

`find_placement` and `find_infrastructure_candidate` left the model-facing surface. `place_building` does not take a service `role` and rejects `ServiceUpgradeData`; facility upgrades need a dedicated interface. Operational areas expand only — shrinking a non-empty site would invent an overflow policy we do not have.

## Considered Options

- **Model-visible candidate pools.** Rejected: native preview cannot retry in-call; a single finalist is the honest interface.
- **Keep preview tools for "planning".** Rejected: a single recommendation is not a choice, so it should not be a separate tool.
