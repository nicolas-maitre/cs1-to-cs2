# transit

M2: CS1 transit lines (`plan.transit`) → CS2 stops and transport lines, by script. Research: [NOTES-cs2-transit.md](NOTES-cs2-transit.md).

```bash
dotnet build -c Release transit/cs2-exec
node transit/transit.mjs apply --plan <cs1conv-plan.json> [--theme EU|NA] [--only "Bus Line 5"]   # after the road import (and buildings)
node transit/transit.mjs plan --plan <f>          # offline: stops/lines it would create
node transit/transit.mjs list | stops [--type Bus] | counts | prefabs
node transit/run-transit.mjs --plan <f> [--load cs1conv-m3-buildings] [--depot <buildings plan>]   # full in-game check, saves cs1conv-m2-transit
cd transit && npm test
```

`apply` does the following:
1. **Kerbside stops:** it creates them as permanent object definitions attached to the nearest road edge, on the plan's side.
   - Prefabs are `EU_BusStop01` / `EU_TramStop01`, or the `NA_` ones with `--theme NA`.
   - It works in batches of 20.
2. **Lines:** it creates one line per call.
   - Each line is a route definition, then one forced apply frame (`applyMode = Apply`, so `ApplyRoutesSystem` runs, as for a click).
   - Station lines use the station stops of their transport type within 200 m of the CS1 stations. If none are found, the line is reported as skipped.
3. **Names and numbers:** it sets the CS1 names and numbers in the main phase.

**Verified in game** (run 4, `.gamectl/transit/run4/summary.json`). The test loads `cs1conv-m3-buildings` (Skibidi roads + buildings incl. the subway stations), applies the transit and saves **`cs1conv-m2-transit`**:
- **Stops:** 145/145 kerbside stops (`EU_BusStop01`) created, 0 errors, no crash.
- **Lines:** 9/9 created and `complete`, with CS1 names, numbers and colours:
  - 8 bus lines, 165 of 167 stops resolved (Bus Line 11: 2 missing);
  - Metro Line 3 on the 4 placed `SubwayStation03` platforms.
- **Depot:** a bus depot was added for the test with the buildings placer (`--depot`, BusDepot01 moved 117 m to free land).
- **Traffic:** 29–34 buses ran during 4.5 minutes of simulation, and most lines had vehicles at the end.
- **Screenshots:** the transport overlay (Transportation infoview: 8 bus lines + 1 subway line) and stop close-ups in `.gamectl/transit/run4/shots/`.

**Open:**
- **Metro vehicles:** Metro Line 3 has none, because there is no subway yard in the save (the buildings plan has none).
- **Stop visuals:** the close-up shows the stop marker at the kerb lane of a wide avenue. Whether the stop object sits on the pavement or the lane edge on every road type is not checked.
- **Bus Line 11:** 2 of its stops didn't resolve.

**Notes for scripts:**
- **Load the save twice:** Road Builder shows a modal ("reload this save-game") the first time a save with converted roads loads in a session. `run-transit.mjs` reloads once, which dismisses it.
- **Order in the e2e:** run transit after the road import and after the buildings (stations, depots).

**Contract with Cs2ImporterMod** (worker-3):
- **Compiled in by reference:** `cs2-importer/mod/Cs2ImporterMod.csproj` compiles `transit/cs2-exec/*.cs` directly. That code must keep compiling against the CS2 DLLs alone.
- **Stable API:** `Cs1ConvTransit.Ops.Run(dict)` and the ops `stops.create`, `lines.create`, `lines.name`, `lines.list`, `counts`, with their argument and result shapes, must stay stable.
- **Ported planner:** `transitJobs` in `lib/plan.mjs` is ported to C# as `TransitStep.BuildJobs` in cs2-importer. Change both together, or tell worker-3.
