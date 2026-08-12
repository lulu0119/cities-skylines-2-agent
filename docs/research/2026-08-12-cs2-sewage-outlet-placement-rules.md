# Cities: Skylines II shoreline utility placement rules (2026-08-12)

**Status:** official-source audit; no code changed.  
**Question:** In Cities: Skylines II 1.6.0f1, do the Water Pumping Station and
Sewage Outlet require a road, a shoreline, and a particular land/water
orientation?  
**Sources accessed:** 2026-08-12.

## Conclusion

The official material supports a narrower rule than "the road side must be on
land and the intake/outfall side must be in water":

- Both assets are shoreline facilities. The Water Pumping Station is "placed
  on the shoreline" and the Sewage Outlet is "built on the shoreline."
- The Water Pumping Station has a road placement requirement in the official
  Feature Highlight video: its in-game placement preview visibly reports
  `Road required` at 02:58.
- The Sewage Outlet is explicitly connected to the city through a sewage pipe.
  The official sources reviewed do **not** state that it also requires a road.
- No official source reviewed defines the exact geometric validator as
  "building center/road side on land, intake/outfall side in water." Official
  images visually match that orientation, but images are examples, not a
  specification of probe points or footprint tests.

Therefore, describing a Sewage Outlet rule as "road side on land and discharge
side in water" overstates the evidence. "Place it on the shoreline and connect
it through a sewage pipe" is the official rule we can actually cite. For the
Water Pumping Station, a road requirement is directly visible, but the exact
land/water-side test remains undocumented.

## Requirement matrix

| Requirement | Water Pumping Station | Sewage Outlet |
| --- | --- | --- |
| Shoreline placement | **Directly confirmed.** "It is placed on the shoreline." | **Directly confirmed.** "It is built on the shoreline." |
| Road as a placement condition | **Directly shown in official in-game footage.** The placement preview reports `Road required`. | **Not established by the official sources reviewed.** The asset-specific text names a sewage pipe, not a road. |
| Road as a separate operating condition after construction | **Not separately documented.** The footage proves a placement-validator condition, not a distinct post-build operating rule. | **Not documented.** |
| Utility-network connection | It brings surface water into the city's water system. General official pipe documentation says roadside buildings connect automatically to road-borne pipes; it does not say a separate manually drawn pipe is always mandatory for this station. | **Directly confirmed.** It is "connected to the city through a sewage pipe." |
| Exact land/water orientation or center-point rule | **Not documented.** Official imagery shows the access arm approaching from land and the intake structure in water. | **Not documented.** Official imagery shows the facility body on land and its discharge hose extending into water. |

"Not established" is deliberately different from "officially confirmed not
required." The public first-party sources are sufficient to reject presenting
a Sewage Outlet road requirement as a documented official rule, but not to
prove every internal prefab flag in version 1.6.0f1.

## Official evidence

### 1. Paradox Feature Highlight #6: written placement and pipe rules

Paradox published
[Cities: Skylines II Feature Highlight #6: Electricity & Water](https://www.paradoxinteractive.com/games/cities-skylines-ii/features/electricity-water)
on 2023-07-24.

Water Pumping Station:

> The Water Pumping Station pumps water from surface water areas, such as
> lakes, rivers, and the ocean. It is placed on the shoreline and has a stable
> output of water unless the water level drops significantly.

Sewage Outlet:

> A Sewage Outlet pumps dirty water directly back into surface water areas. It
> is built on the shoreline and connected to the city through a sewage pipe.

General pipe behavior:

> Water infrastructure is built-in into most road types, similar to electric
> cables, though pipes do not have a capacity to keep an eye on. Both water and
> sewage pipes are automatically connected to all the buildings constructed
> alongside roads. Buildings that don’t require road access can be connected
> with separate underground pipes, either single pipes for water or sewage and
> as a combined water-sewage dual pipe.

The same first-party article is mirrored in
[Steam News](https://store.steampowered.com/news/app/949230/view/3659786371370994889).

These passages directly establish shoreline placement, the Sewage Outlet's
sewage-pipe connection, and how roadside versus off-road utility connections
work in general. They do not identify which internal point of either asset the
shoreline validator samples.

### 2. Official Feature Highlight video: Water Pumping Station road condition

The official Cities: Skylines channel published
[Electricity & Water | Feature Highlights Ep 6 | Cities: Skylines II](https://www.youtube.com/watch?v=-aNNVd9pH9Q&t=178s)
on 2023-07-24. At 02:58, the Water Pumping Station is selected in the Water &
Sewage build menu. Its placement preview is red and the visible game messages
include:

> In water  
> Road required

This is direct first-party evidence that the Water Pumping Station placement
tool expected road access in the shown build. It also demonstrates that merely
putting the asset in water is invalid; the preview must satisfy more than water
coverage alone. It still does not expose the exact footprint, center-point, or
"road side on land" implementation.

The video description warns that the footage was captured before release and
might change. The current patch notes checked below do not announce a later
road/shoreline placement-rule change, but absence from patch notes is not a
versioned API guarantee.

The video's narration at 02:50-03:16 also says:

> Build a Groundwater Pumping Station to tap this resource and a Water Pumping
> Station to bring surface water into your city's water system. [...] There's
> another way to manage sewage: Sewage Outlets. They release waste into open
> water, which can help prevent sewage from backing up.

That supports the functional water-network and open-water roles, but adds no
road rule for the Sewage Outlet.

### 3. Official images: orientation examples, not validator specifications

The Feature Highlight's
[Water Pumping Station image](https://images.ctfassets.net/u73tyf0fa8v1/3S67UOzUxQVihptP2SUbF4/d33079fabc5248b0038216e80ceb8ca1/cities-skylines-ii-feature-6-20_Water_pump.jpg)
shows a road approaching from land and the intake tower standing in water.

Its
[Sewage Outlet image](https://images.ctfassets.net/u73tyf0fa8v1/D306GesWAV8DTSKYiHh8d/10694418940822d5c9fe2ac151ad8585/cities-skylines-ii-feature-6-24_Sewage_outlet.jpg)
shows the facility bodies on land and flexible discharge hoses extending into
surface water. The outlets visibly discharge pollution without an obvious
direct local-road attachment.

These images make the intended physical orientation intuitive. They cannot by
themselves prove that the game validates a named "road side," the asset center,
or any particular sample distance.

### 4. Version 1.6.0f1 patch notes

The official
[Patch 1.6.0f1 - Summer Solstice](https://store.steampowered.com/news/app/949230/view/699893179027031259)
notes were published on 2026-06-22. They contain no Sewage Outlet, Water
Pumping Station, or utility-building placement change. The only shoreline item
is:

> Fixed shoreline being misaligned with the water.

That entry does not define or change a building placement rule. It also means
the 2023 Feature Highlight should be treated as the best public first-party
description available, not as a formal 1.6.0f1 validator specification.

## Implication for placement tooling

A placement planner may reasonably use "landward body/access, waterward
intake/outfall" as a search heuristic because it matches the official images.
It should not present that heuristic as a documented game rule, especially for
the Sewage Outlet. The externally supported contract is:

1. locate a shoreline;
2. let the native placement preview validate the exact asset pose;
3. for a Water Pumping Station, satisfy the native road requirement shown by
   the game;
4. for a Sewage Outlet, connect the accepted building to the city with a sewage
   pipe.

The native preview remains authoritative for the undocumented geometric
details.
