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
| Markings at junctions | – | listed in the report only | No CS2 equivalent yet (decals are planned). |

## Terrain and water

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Terrain heightmap | CS2 terrain (same heights) | converts | Applied by the terrain step before the roads. |
| Sea level, rivers and lakes (natural water sources) | CS2 sea level and water sources | converts, approximated | CS2 re-simulates the water; pumps and drains come with buildings. |

## Zoning

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Residential, commercial, industrial and office zones | CS2 zones (EU or NA theme) | in the package; CS2 side in progress | District specialisations become generic zones. |

## Traffic (TM:PE)

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Speed limits, bus-only lanes | Road Builder variants with that speed / bus lane | converts | CS2 has one speed per road. |
| Traffic lights, all-stop junctions, turn restrictions from lane arrows, crosswalks | Vanilla CS2 junction settings | in the package; CS2 side in progress |   |
| Priority signs, lane arrows, lane connections, U-turns | Traffic mod | in the package; CS2 side in progress | Needs the Traffic mod in CS2. |
| Timed traffic lights, turn on red, blocked junctions, parking rules | – | listed in the report only | Timed lights become plain lights. |

## Node Controller

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Node types, offsets, hidden markings | – | listed in the report only | CS2 builds junction geometry itself. |

## Transit

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Bus and tram lines | CS2 lines with colour, name and stops | in the package; CS2 side in progress | Trolleybus, tourist and intercity buses become bus lines. |
| Train, metro, ship and plane lines | CS2 lines | in the package; CS2 side in progress | Need their stations (placed with the buildings). |

## Buildings

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Service buildings, stations, depots, parks, landmarks | Closest CS2 building (same service, then capacity and size) | in the package; CS2 side in progress | Check or change each type in the Assets tab. |
| Zoned (growable) buildings | Grown by CS2 on the converted zones | converts | Not placed one by one. |
| Libraries, taxi stands, cable car stops | – | not converted | No CS2 equivalent. |

## Districts

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| District names and outlines; policies | CS2 districts and policies | in the package; CS2 side in progress | Policies with a CS2 equivalent (heavy traffic ban, recycling, …); zone specialisations are reported. |
| Parks, industry areas, campuses, airports | – | listed in the report only | Their buildings are placed as single buildings. |

## Other

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Pipes, power lines | Built by CS2 along roads | not converted | CS2 lays water and electricity with the roads. |
| Free-standing props and trees | – | not converted | Exported; conversion is planned. |
