---
name: transit-lines
description: How to list existing transit stops and create or delete a simple passenger line.
---

# Transit lines

## What this covers
- Connect existing passenger stops into a line. Vehicles spawn from depots on their own.
- Stops already exist as station sub-stops (place a Bus Station with place_building) or roadside stop objects. There is no place_building role that creates a stop.

## Read first
- Enable the construction tool group, then call list_transit_stops (type=bus near the district) and list_transit_lines.
- A line needs at least two passenger stops of the same type. Prefer stops that already sit on the roads you want served.

## Write
- create_transit_line(stops="index:version,index:version", type=bus). The tool closes the loop back to the first stop and applies through native route pathfinding. If validation rejects the path, pick different stops or add road access; do not force.
- delete_transit_line(index, version) removes the line only. Stops and stations stay.
- After create, wait_simulation(hours=1) and list_transit_lines to confirm vehicles. Do not add a production or vehicle tool.

## Do not
- Do not invent a stop by calling place_building with a transport role.
- Do not treat taxi stands, cargo lines, or work routes as this passenger-line slice.
- Do not delete a station building to remove a line.
