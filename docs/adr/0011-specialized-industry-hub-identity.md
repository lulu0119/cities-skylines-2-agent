# Specialized-industry hubs follow extractor-area declarations

Status: accepted

The model-facing `specialized-industry` role identifies independently placeable building prefabs that declare an extractor Operational area through their prefab `SubArea` graph. `ExtractorFacilityData` describes animated facilities inside an extraction site and is not hub identity: using it listed cattle sheds and similar facilities that placed successfully but owned no extractor area. Prefab classification therefore follows `SubArea` → `ExtractorAreaData`, fails closed for malformed or mixed placeholder choices, and leaves placement approval and resource viability to native validation and post-place Operational-area reads.

## Considered Options

- **Keep `ExtractorFacilityData` as the role marker.** Rejected: live placement proved it admits non-hub facilities with no extractor Operational area.
- **Add a separate hub tool or `role` argument to `place_building`.** Rejected: exact-prefab one-step placement remains the product interface; catalog classification should carry the domain identity.
- **Refactor every prefab and building capability into one broad module now.** Rejected: the immediate seam is hub identity; expanding the change into storage, runtime snapshots, and expansion policy would couple unrelated verified paths to this fix.
