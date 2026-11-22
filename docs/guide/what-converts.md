<!-- Generated from web/src/core/docs.ts by `node web/scripts/gen-docs.mjs` (MATRIX). Edit the source, not this file. -->

# What converts

Per city, the web tool also shows this per category with your numbers (Overview → What converts) and lists every approximation in `report.json`.


## Roads

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Roads, highways, one-ways, Vanilla+ and workshop roads | A matching vanilla CS2 road, else a generated Road Builder road | converts, approximated | Lanes, medians, parking, bus and tram lanes, trees and speed are kept; widths can change (Road Builder sidewalks max 7 m). A vanilla road is used only when the lanes match (each within 0.5 m, total within 1 m). |
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
| Terrain heightmap (and the land around the square) | CS2 terrain at the same heights, plus the world map around it | converts | Applied by the importer's Prepare map, in a new city, before the import. |
| Ground shaped to the roads (CS1 flattens it under its roads) | Road profiles burned into the CS2 heightmap, with earth slopes | converts | Per road type in the Mapping tab: ground follows the road (streets, tracks), CS1 height with retaining walls (highways), or road follows the ground (paths). |
| Sea level, rivers and lakes (natural water sources) | CS2 sea level and water sources | converts, approximated | Set by Prepare map. CS2 re-simulates the water; extra sources keep rivers and lakes above the sea filled. Pumps and drains come with their buildings. |
| Highways and railways leaving the square | CS2 outside connections | converts | The importer's last step (Outside connections), so traffic and trains can enter and leave the city. |

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
| Lane arrows, lane connections, U-turn rules | Traffic mod lane connections and U-turn settings | converts, approximated | Applied exactly at junctions (Lane connections (Traffic mod), on by default), in right- and left-hand cities; lane splits where a road continues without a junction stay as CS2 builds them. |
| Timed traffic lights, turn on red, blocked junctions, parking rules | – | listed in the report only | Timed lights become plain traffic lights. |

## Node Controller

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Segment-end shifts (lanes lined up across junctions and lane drops) | Road ends off the node centre, as in CS1 | converts | Not on free-form nodes, which CS2 has no equivalent for. |
| Offsets at junctions (stretched junctions) | Road ends pulled back, a longer junction | converts | At junctions of 3+ roads, where the offset is more than 5 m above Node Controller's default (at most 45 % of the segment); option "Node Controller offsets at junctions". |
| Node types, offsets between 2 roads, slopes, twists, hidden markings | – | listed in the report only | CS2 builds junction geometry itself. |

## Transit

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Bus and tram lines | CS2 lines with colour, name, number and stops | converts, approximated | Trolleybus, tourist and intercity buses become bus lines. A depot is placed for them; a city with few depots may not have enough buses for every line. |
| Train, metro, ship and plane lines | CS2 lines at the placed stations | converts, approximated | Need their stations from the buildings step. Metro and train yards go at the crop edge where the track leaves it (or you place them); never inside the city. |

## Buildings

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Service buildings, stations, depots, parks, landmarks | Closest CS2 building (same service, then capacity and size) | converts, approximated | Check or change each type in the Assets tab. A building that doesn't fit at its spot is not moved by default: it is listed for you to place. Bus and tram depots are the exception when none of their type fits: one is placed nearby so the lines have a depot. |
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
| Pipes and power lines along roads | Built by CS2 with the roads | converts | CS2 roads carry water, sewage and low-voltage power themselves. |
| Pipes and power lines away from roads | CS2 pipes and power lines at the same place | converts, approximated | Placed by the importer's buildings step; a line that runs along a converted road for most of its length is left to the road. |
| Power plants and wind turbines | CS2 power plants, linked to the grid | converts, approximated | Wind turbines and other low-voltage producers get a short power line to the nearest road. Coal, gas and incineration plants need a transformer station: an opt-in in the importer's options (Add a transformer station next to each imported power plant); otherwise the report names them and the city runs on its outside connection. |
| Electricity and water bought from outside the square | CS2 electricity and water outside connections | converts | The importer's Outside connections step adds them at the edge of the imported roads (a short power line and pipe with the connection at the end), so a square without its own plants and pumps still gets power, water and sewage. |
| Free-standing trees and rocks | CS2 trees (same species, or same kind and closest height) and boulders | converts | Trees on a road are moved just past its edge; trees inside building lots are left out. |
| Other free-standing props (benches, lamps, decorations) | – | not converted | No sensible CS2 equivalent; counted in the report. |
