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
| Node types, offsets, hidden markings | – | listed in the report only | CS2 builds junction geometry itself. |

## Transit

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Bus and tram lines | CS2 lines with colour, name, number and stops | converts, approximated | Trolleybus, tourist and intercity buses become bus lines. |
| Train, metro, ship and plane lines | CS2 lines at the placed stations | converts, approximated | Need their stations from the buildings step. Metro and train yards go at the crop edge where the track leaves it (or you place them); never inside the city. |

## Buildings

| CS1 | CS2 | Status | Notes |
| --- | --- | --- | --- |
| Service buildings, stations, depots, parks, landmarks | Closest CS2 building (same service, then capacity and size) | converts, approximated | Check or change each type in the Assets tab. A building that doesn't fit at its spot is not moved by default: it is listed for you to place. |
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
