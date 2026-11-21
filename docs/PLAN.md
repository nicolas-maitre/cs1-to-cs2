# Plan

## Milestones

| | Goal | Status |
| --- | --- | --- |
| M0 | Feasibility spikes | done ([spikes/M0-REPORT.md](../spikes/M0-REPORT.md)) |
| **M1** | **Roads-first vertical slice**: Skibidi.02 → `.cs1x` → web tool (crop + road mapping) → CS2 importer builds the road network in a new CS2 city, all scripted | done (2026-11-21) |
| M2 | Terrain and water (CS1 heightmap → CS2 map), zoning, TM:PE (lane arrows, priority signs), Node Controller, transit lines, the importer as a user-facing mod with an in-game button | done (2026-11-21) |
| M3 | Buildings and assets (asset mapping in the web tool), parks, services, props, decals for IMT node fills | done (2026-11-21): buildings kept at their CS1 spot (#106), utilities, power hookup (#150) |

| Release | Release-grade mods and website: RC3 passed on b71a160 ([QA.md](QA.md) §11); the web tool is hosted on GitHub Pages | polish in progress; publishing is the user's call |

## M1 scope

| Piece | Folder | Done when |
| --- | --- | --- |
| Formats | `schema/` | `.cs1x` v1 and import package v1 are specified ([schema/README.md](../schema/README.md)) |
| CS1 exporter | `cs1-exporter/` | The mod and an `exec` entry point write a valid `.cs1x` of Skibidi.02: network, NetInfos (with parents), IMT fills and terrain heightmap |
| Web tool | `web/` | Loads a `.cs1x` and previews the network. The user picks a crop square. Every used NetInfo is mapped to a CS2 prefab or a generated Road Builder config, with IMT lane overrides. It writes the import package. The same core runs as a Node CLI for scripts. |
| CS2 importer | `cs2-importer/` | An `exec`-callable library builds the plan's roads in CS2: batches, shared node positions, node map, elevation and bridges, then reports what was built. The CS2 bridge can create a new city and save it as `cs1conv-…`. |
| Terrain (head start on M2) | `terrain/` | Converts the CS1 heightmap to a CS2 heightmap for the crop, and loads it into CS2 by script (editor or in-game), so imported roads sit on the right ground. |
| End to end | – | One script runs: export Skibidi.02 → plan a crop → copy RB configs → start CS2 → new city → (terrain) → import → screenshot → save `cs1conv-skibidi-m1` |

## How work runs overnight

- **Workers:** sub-sessions in the shared checkout on `main`, each owning its folder. They don't commit; the coordinating session reviews, commits and pushes.
- **Games:** one session drives each game at a time.
  - CS1: the exporter session.
  - CS2: the importer and terrain sessions coordinate. Before launching, check `gamectl cs2 status`, and wait if the game is busy.
- **Decisions:** every important one goes into [DECISIONS.md](DECISIONS.md).
