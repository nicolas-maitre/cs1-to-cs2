# Interchange formats (v1)

```
CS1 + Cs1Exporter ──▶ city.cs1x ──▶ web tool (preview, crop, road mapping) ──▶ import package ──▶ CS2 importer
                                                                      ├─ cs1conv-plan.json
                                                                      └─ roadbuilder/*.json  (copied to CS2 ModsData/RoadBuilder before CS2 starts)
```

These rules apply to every file below:
- **Encoding:** JSON is UTF-8 with no BOM.
- **Units:** distances are in metres and angles in degrees unless stated otherwise.
- **Unknown fields:** readers ignore fields they don't know.
- **Missing sections:** every section is optional, and readers must handle one that is missing or `"failed"`.
- **Versioning:** the format version bumps only for breaking changes. Adding fields is not breaking.

---

## 1. `.cs1x`: CS1 export

`.cs1x` is a zip file (deflate or stored) containing these entries:

| Entry | Required | Content |
| --- | --- | --- |
| `manifest.json` | yes | metadata and the status of each section |
| `network.json` | M1 | nodes and segments |
| `netinfos.json` | M1 | every NetInfo used, plus its variants and parents |
| `imt.json` | M1 | IMT fillers (fills) with world polygons |
| `terrain.json` + `terrain.r16` | M1 | raw heightmap |
| `report.json` | yes | non-fatal problems |
| `zones.json`, `buildings.json`, `water.json`, `tmpe.json`, `nodecontroller.json`, … | M2+ | later; absent in M1 |

### Coordinates

| What | Convention |
| --- | --- |
| Space | CS1 world space, in metres |
| Axes | X east, Y up, Z north. The map centre is the origin. |
| Positions | `[x, y, z]` |
| Map size | The full 81-tile area is 17 280 m × 17 280 m (−8640 … +8640) |
| Heights | `y` is the absolute height in metres. Terrain heights use the same datum. |

### `manifest.json`

```json
{
  "format": "cs1x", "formatVersion": 1,
  "exporter": { "name": "Cs1Exporter", "version": "1.0.x" },
  "exportedAt": "2026-11-21T01:23:45+01:00",
  "city": { "name": "Skibidi", "saveName": "Skibidi.02" },
  "game": { "version": "1.21.1-f9" },
  "mods": [ { "name": "IntersectionMarkingTool", "version": "1.15.0.0" }, { "name": "TrafficManager", "version": "…" } ],
  "coordinateSystem": "CS1 world space, metres, Y up, origin at map centre",
  "mapSize": 17280,
  "sections": { "network": "ok", "netinfos": "ok", "imt": "ok", "terrain": "ok", "tmpe": "absent", "nodecontroller": "absent" }
}
```

Each section has one of these statuses:
- `"ok"`: written completely.
- `"failed"`: the entry may be missing or partial; `report.json` says why.
- `"absent"`: not exported (not implemented, or the mod isn't present).

### `network.json`

```json
{ "nodes": [ { "id": 12, "info": "Train Track", "position": [x,y,z], "flags": "Created, Middle, OnGround",
               "elevation": 0, "heightAboveTerrain": -0.07, "building": 0, "segments": [24308, 17418] } ],
  "segments": [ { "id": 2, "info": "Train Track", "ai": "TrainTrackAI", "startNode": 13593, "endNode": 3469,
                  "startDirection": [x,y,z], "endDirection": [x,y,z],
                  "bezier": [[x,y,z],[x,y,z],[x,y,z],[x,y,z]], "flags": "…", "inverted": true, "length": 93.08,
                  "oneWay": false, "elevationKind": "ground" } ] }
```

- **`bezier`** is the segment's cubic curve from the start node to the end node, using the game's own middle points (`NetSegment.CalculateMiddlePoints`).
- **`elevationKind`** is one of `ground`, `elevated`, `bridge`, `slope`, `tunnel`.
- **`info`** is the NetInfo name, which is the key into `netinfos.json`. For an elevated, bridge, slope or tunnel segment it names the variant NetInfo.

### `netinfos.json`

```json
{ "netInfos": [ { "name": "Basic Road", "title": "…", "ai": "RoadAI", "service": "Road", "subService": "None",
                  "level": "Level1", "className": "…", "fields": { "m_halfWidth": 8, "m_pavementWidth": 3, … },
                  "aiFields": { … }, "parent": null,
                  "variants": { "elevated": "Basic Road Elevated", "bridge": "Basic Road Bridge", "slope": "Basic Road Slope", "tunnel": "Basic Road Tunnel" },
                  "lanes": [ { "index": 0, "fields": { "m_position": -5, "m_width": 3, "m_laneType": "Pedestrian", … },
                               "props": [ { "prop": "…", "tree": null, "finalProp": "…", "finalTree": null, "fields": { … } } ] } ] } ] }
```

- **What's included:** every NetInfo a node or segment uses, plus its variants, plus the parent of every variant. The parent is the ground NetInfo that owns the variant.
- **`parent`:** on a variant, the name of its ground NetInfo; `null` on a ground NetInfo.
- **`variants`:** on a ground NetInfo, the names of its variants; a variant that doesn't exist is omitted.

### `imt.json`

```json
{ "fillers": [ { "markingType": "Node", "markingId": 2302, "fillerId": 537978560, "isMedian": true,
                 "styleClass": "SolidFillerStyle", "styleType": "FillerSolid", "color": [136,0,0,94],
                 "styleProperties": { … }, "polygon": [[x,y,z], …] } ] }
```

- **`markingType`** is `Node` or `Segment`, and `markingId` is that node or segment id.
- **`styleType`** is the IMT style, e.g. `FillerSolid`, `FillerPavement`, `FillerGrass`, `FillerStripe`, `FillerChevron`, `FillerGravel`, `FillerRuined`, `FillerCliff`.
- **Interpretation** is in `docs/DECISIONS.md`:
  - red solid fill → bus lane;
  - pavement or grass fill → median;
  - full coverage only;
  - node fills and grey paint → report only.

### `terrain.json` + `terrain.r16`

```json
{ "resolution": 1081, "size": 17280, "heightScale": 0.015625, "file": "terrain.r16",
  "rowOrder": "row 0 = minimum z (south), column 0 = minimum x (west)", "byteOrder": "little-endian uint16",
  "seaLevel": null, "min": 40.2, "max": 612.7 }
```

- **`terrain.r16`** holds `resolution × resolution` uint16 values. Height in metres is `raw × heightScale`.
- **Sample spacing** is `size / (resolution − 1)` metres. Sample `(col, row)` is at `x = −size/2 + col × spacing`, `z = −size/2 + row × spacing`.
- **The exporter verifies the orientation** against node heights. Anything that differs from this spec gets reported in `report.json`, and the spec is corrected here.

### `report.json`

```json
{ "items": [ { "level": "info|warning|error", "section": "imt", "message": "IMT assembly: IntersectionMarkingTool 1.15.0.0" } ] }
```

---

## 2. Import package: web tool → CS2 importer

The web tool, or its CLI, writes these from a `.cs1x` and the user's choices:

```
<package>/
  cs1conv-plan.json
  roadbuilder/<id>-cs1conv.json     # generated Road Builder configs (see spikes/m0-roadbuilder-configs)
  report.json                       # everything that was dropped or approximated, for the user
```

Road Builder reads its configs once, when CS2 starts. So the `roadbuilder/*.json` files must be copied into `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\RoadBuilder\` **before** CS2 starts. The CLI does that; the web UI offers them as a download.

### `cs1conv-plan.json`

The plan is already in CS2 coordinates, so the importer makes no mapping decisions.

```json
{
  "format": "cs1conv-plan", "formatVersion": 1,
  "source": { "city": "Skibidi", "saveName": "Skibidi.02", "exportedAt": "…", "cs1xSha256": "…" },
  "crop": { "centerX": 1200, "centerZ": -300, "size": 4000 },
  "transform": { "dx": -1200, "dz": 300, "heightMode": "terrain-relative" },
  "prefabs": {
    "rb:5266315b": { "type": "RoadBuilderPrefab", "name": "r5266315b…-cs1conv", "source": "Four-Lane Road with Decorative Trees" },
    "v:Small Road": { "type": "RoadPrefab", "name": "Small Road", "source": "Basic Road" }
  },
  "nodes": [ { "id": 12, "x": 262.6, "z": -343.9, "y": 273.1, "heightAboveTerrain": 0.0, "elevation": 0 } ],
  "segments": [ { "id": 2, "prefab": "v:Small Road", "start": 13593, "end": 3469,
                  "bezier": [[x,y,z],[x,y,z],[x,y,z],[x,y,z]],
                  "elevationStart": 0, "elevationEnd": 0, "kind": "ground" } ],
  "stats": { "segments": 1234, "skipped": 56 }
}
```

- **Crop and transform:**
  - `crop` is a square in CS1 coordinates (M1: no rotation).
  - CS2 position = CS1 position + (`dx`, `dz`), so the crop centre lands at the CS2 map origin.
  - `size` must fit the CS2 buildable area. The importer checks it and reports the actual CS2 bounds.
- **Height** (`heightMode: "terrain-relative"`, M1): the importer puts each node at CS2 terrain height + `heightAboveTerrain`. CS2 terrain doesn't match CS1 until the terrain work lands. The bezier `y` values are informational only.
- **Elevation:** `elevationStart` / `elevationEnd` is the height above terrain at each end, passed to CS2 as the course elevation. It's > 0 for elevated roads and bridges and < 0 for tunnels.
- **Nodes and segments:**
  - Only nodes used by an included segment are listed.
  - A segment is included when both its nodes are inside the crop and its prefab maps to something.
  - Everything excluded is counted in `stats` and explained in `report.json`.
- **`prefabs` keys** are opaque ids used by `segments[].prefab`:
  - `type`: `RoadPrefab`, `RoadBuilderPrefab`, `TrackPrefab` or `PathwayPrefab`;
  - `name`: the CS2 prefab name (for a Road Builder road, its config id).

### `report.json` (package)

```json
{ "items": [ { "level": "info|warning", "kind": "imt-node-fill|imt-partial-fill|unmapped-netinfo|outside-crop|width-loss|…",
               "message": "…", "cs1": { "segment": 394 } } ] }
```
