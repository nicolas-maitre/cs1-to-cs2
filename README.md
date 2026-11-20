# cs1-to-cs2

Personal tool that rebuilds a Cities: Skylines 1 city as a functional Cities: Skylines 2 city.

| Folder | What |
| --- | --- |
| `schema/` | `.cs1x` interchange format (zip of JSON + raw heightmap). Every mod-data section is optional. |
| `cs1-exporter/` | CS1 mod (C#, .NET 3.5, ICities + CitiesHarmony) that exports live game data. |
| `web/` | Client-side web tool (TypeScript + Vite + React): preview, crop, asset / Road Builder mapping. |
| `cs2-importer/` | CS2 mod (C#, Unity ECS) that rebuilds the city (editor: terrain/water; in-game: networks, zoning, buildings, transit, traffic). |
| `spikes/` | Throw-away feasibility spikes. Start with `spikes/M0-REPORT.md`. |
