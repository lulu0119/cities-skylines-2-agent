---
name: city-building
description: How to grow a city from empty land through early, mid and late phases.
---

# City building playbook

## Priorities

- Check city_services.problems[] and notifications FIRST. Fix blocking problems (sewage, water, electricity, garbage, road access) before zoning or expanding.
- Zone what demand asks for. Regular residential / commercial / industrial / office buildings grow from zone_area along roads; use place_building only for standalone buildings (service buildings, unique/landmark/signature buildings, special production or extraction facilities).
- Expand roads outward on owned tiles in short segments; keep utilities ahead of demand.
- After changes, use wait_simulation briefly (10-30s, max 60s) and re-read problems[]/demand before deciding the next step.

## Phases (adapt to the city, not a fixed recipe)

- Empty land: connect a road from the highway; make sure the city has power, water and sewage handling (choose prefabs by the map with terrain / gridmap / find_prefabs).
- Small city: add education and medical care, fix access and utility problems as they appear.
- Growing city: add garbage service, police/fire, medium-density housing and more utility capacity.
- Larger city: hospitals, universities, offices, cargo/industry upgrades — only as demand and problems require.

## Always

- Verify each problem is cleared before moving on.
- The simulation clock belongs to the player: use wait_simulation, never force long runs or poll game_state.
