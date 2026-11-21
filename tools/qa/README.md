# tools/qa: measurements in converted CS2 cities

QA checks that run inside CS2 through the game bridge (`tools/PROTOCOL.md`, `exec`). They only read: nothing in the city is changed, and nothing is saved back. Owner: web worker.

```powershell
node tools/qa/run.mjs utilities --save cs1conv-acceptance-20261121-133435 --minutes 3
```

## `utilities`: electricity, water and sewage supply (docs/DECISIONS.md #109)

Utility networks (pipes, power lines) are not converted, because CS2 roads carry water, sewage and power themselves. Service buildings (power plants, pumps, water towers, sewage outlets) are matched and placed. This check measures how well that works in a converted city.

For each save, the runner loads it, lets it run `--minutes` of real time at top speed (`0` = measure it as saved), pauses, and runs the exec op `Cs1ConvQa.Utilities`. The op reports:

| Part | What | From (Game.dll 1.6) |
| --- | --- | --- |
| `coverage` | Per building category (residential, commercial, industrial including office and extractors, service): consumers, how many are connected, and how many get all they want, for electricity, water and sewage. | `ElectricityConsumer` (flags `Connected`, `NoElectricityWarning`, wanted vs fulfilled), `WaterConsumer` (`WaterConnected`, `SewageConnected`, wanted vs fulfilled fresh / sewage) |
| `totals` | Production vs consumption, as the infoviews show it, plus electricity import and export. | `ElectricityStatisticsSystem`, `WaterStatisticsSystem`, `ElectricityTradeSystem` |
| `power`, `pumps`, `outlets`, `towers` | Every facility with its prefab, position, capacity, last production, the state of its flow edge, and the game's notification icons on it. | `ElectricityProducer`, `WaterPumpingStation`, `SewageOutlet`, `WaterTower`, `ElectricityBuildingConnection` / `WaterPipeBuildingConnection` → flow edge flags, `IconElement` vs the notification prefabs in `ElectricityParameterData` / `WaterPipeParameterData` |
| `problems` | Power plants, pumps and outlets that aren't connected (edge disconnected, or a "not connected" icon); power plants that are connected but produce nothing; pumps not on water (a "not enough groundwater / surface water" icon, or capacity 0 against the prefab's); outlets with capacity 0. | the above |
| `buildingNotifications` | How many buildings show each utility notification (no electricity, no water, sewage backup, not connected, …). | `IconElement` |
| `outsideConnections` | Electricity and water outside connections in the city, and all outside connections for context. | `Game.Objects.ElectricityOutsideConnection`, `WaterPipeOutsideConnection`, `OutsideConnection` |

`--oc-utilities` adds the electricity and water outside connections that the importer now creates (MapPrep `oc.utilities`, docs/DECISIONS.md #136) right after loading, before simulating. Use it to measure an old save as a current import would be. The output file is then `utilities-<save>-oc.json`. `--save-as <name>` keeps the result as a new save.

Output: `.gamectl/qa/utilities-<save>.json` (the summary and the full result), plus a summary on stdout. The exit code is 1 when a city has any `problems`, or less than 90 % of consumers supplied for electricity, water or sewage. That threshold isn't a verdict on CS2's economy, but below it something structural is missing.

**CS2 is shared.** The runner takes `%LOCALAPPDATA%\cs1-to-cs2\cs2.lock`. It waits up to 2 h while another owner's lock is younger than 2 h, then releases it at the end. It stops CS2 at the end only if it started it (`--keep` leaves it running).

**In the release acceptance:** `acceptance-utilities.patch` adds a read-only `utilities` step to `tools/release/acceptance.mjs`, which is the buildings worker's file. The step runs right after the simulation and the vehicle count, and its lines go into `summary.json`. The step is informational: it doesn't fail the run.

Build: `dotnet build tools/qa/Cs1ConvQa.csproj -c Release` (the runner does it). It references the game's DLLs through `CS2Managed` in `local.props`.
