---
name: utility-networks
description: How electricity, water and sewage networks work in CS2.
---

# Utility networks

## Roads carry utilities
- Most roads carry electricity, water and sewage automatically. Buildings on roads connect to all three.
- Do NOT draw parallel pipes/cables next to roads. Wasted money.

## When separate underground networks are needed
- Off-road facilities need separate underground connections.
- Connect water/sewage plant to road network with underground pipes at one end.
- Use gridmap (groundWater) to find water for pumps.

## Burying underground networks
- Underground pipes/cables need NEGATIVE elevation: e1=e2=-10 to -20m.
- Positive values = elevated/bridge segments (wrong for buried).

## Sewage is critical
- If problems[] shows sewage or notifications show "Sewage Notification": BUILD SewageOutlet01 near water immediately.
- find_placement → place_building with exact coordinates in SAME turn.
- After building, use wait_simulation briefly (max 60s) and re-check problems[].
- Population cannot grow with sewage backing up.
