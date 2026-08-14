# Budgeted local map, not a raw grid

Status: accepted

A fixed 8×8 sample matrix makes the model reconstruct rivers, slopes, and roads from numbers. `terrain` returns budgeted `LOCAL_MAP` text: a local frame, sectors, connected regions, real road topology, and explicit omissions. High-resolution sampling stays inside the perception module.

The map is spatial evidence, not construction approval. Native write-tool validation remains authoritative. `candidate_buildable` only means owned, dry, and within the declared slope band.

An internal `format=samples` path may keep the old JSON for diagnostics; it is not on the model-facing surface.
