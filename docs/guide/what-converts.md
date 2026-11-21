<!-- Generated from web/src/core/docs.ts by `node web/scripts/gen-docs.mjs` (MATRIX). Edit the source, not this file. -->

# What converts

Per city, the web tool also shows this per category with your numbers (Overview → What converts) and lists every approximation in `report.json`.


## Roads

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Roads, highways, one-ways, Vanilla+ and workshop roads | Generated Road Builder roads | converts, approximated | Lanes, medians, parking, bus and tram lanes, trees and speed are kept; widths can change (Road Builder sidewalks max 7 m). |
| Elevated roads, bridges, slopes, tunnels | The same Road Builder road at the CS1 height | converts | Road Builder makes the elevated, bridge and tunnel forms itself. |
| Painted bike lanes in the carriageway | Sidewalk with bike lane | converts, approximated | Only at the road edge; CS2 has no painted carriageway bike lanes. |

## Rail and paths

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Train, metro and tram tracks | CS2 double, two-way or one-way tracks | converts |   |
| Pedestrian and bike paths | Pavement paths, bike paths | converts |   |
| Monorail, cable car | – | not converted | CS2 has neither. |

## IMT

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Red paint over a whole lane / pavement or grass island over a whole lane | Bus lane / median in the Road Builder road | converts | Partial fills and grey paint are reported. |
| Pavement, grass and gravel fills at junctions | CS2 surfaces (pavement, grass, sand) | converts | Placed with the buildings step. |
| Painted markings at junctions | – | listed in the report only | No CS2 equivalent yet (decals are planned). |

## Terrain and water

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Terrain heightmap (and the land around the square) | CS2 terrain at the same heights, plus the world map around it | in the package; CS2 side in progress | In the package (terrain folder); the importer doesn't apply it yet, so the roads follow the map's own ground. |
| Sea level, rivers and lakes (natural water sources) | CS2 sea level and water sources | in the package; CS2 side in progress | In the package; not applied by the importer yet. CS2 re-simulates the water; extra sources keep rivers and lakes above the sea filled. |
| Highways and railways leaving the square | CS2 outside connections | in the package; CS2 side in progress | In the package; not applied by the importer yet. |

## Zoning

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Residential, commercial, industrial and office zones | CS2 zones (EU or NA theme) | converts, approximated | District specialisations become generic zones. |

## Traffic (TM:PE)

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Speed limits, bus-only lanes | Road Builder variants with that speed / bus lane | converts | CS2 has one speed per road. |
| Traffic lights, all-stop junctions, turn restrictions from lane arrows, crosswalks | Vanilla CS2 junction settings | converts |   |
| Priority signs | Traffic mod | converts | Needs the Traffic mod in CS2. |
| Lane arrows, lane connections, U-turns | Traffic mod | in the package; CS2 side in progress | In the package; not applied in this version (listed in the import report). |
| Timed traffic lights, turn on red, blocked junctions, parking rules | – | listed in the report only | Timed lights become plain traffic lights. |

## Node Controller

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Node types, offsets, hidden markings | – | listed in the report only | CS2 builds junction geometry itself. |

## Transit

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Bus and tram lines | CS2 lines with colour, name, number and stops | converts, approximated | Trolleybus, tourist and intercity buses become bus lines. |
| Train, metro, ship and plane lines | CS2 lines at the placed stations | converts, approximated | Need their stations from the buildings step; a default depot or yard is added when the city has none. |

## Buildings

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Service buildings, stations, depots, parks, landmarks | Closest CS2 building (same service, then capacity and size) | converts, approximated | Check or change each type in the Assets tab, including what happens when it doesn't fit. |
| Zoned (growable) buildings | Grown by CS2 on the converted zones | converts | Not placed one by one. |
| Libraries, taxi stands, cable car stops | – | not converted | No CS2 equivalent. |

## Districts

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| District names and outlines; policies | CS2 districts and policies | converts, approximated | Policies with a CS2 equivalent (heavy traffic ban, recycling, …); zone specialisations are reported. |
| Parks, industry areas, campuses, airports | – | listed in the report only | Their buildings are placed as single buildings. |

## Other

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Pipes, power lines | Built by CS2 along roads | not converted | CS2 lays water and electricity with the roads. |
| Free-standing trees and rocks | CS2 trees (same species, or same kind and closest height) and boulders | converts | Trees on a road are moved just past its edge; trees inside building lots are left out. |
| Other free-standing props (benches, lamps, decorations) | – | not converted | No sensible CS2 equivalent; counted in the report. |
