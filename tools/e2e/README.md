# e2e

The whole converter in one command: CS1 export → plan → CS2 new city → terrain + water → road import → outside connections → screenshots → save.

```bash
node tools/e2e/run.mjs --apply-terrain                    # export Skibidi.02 from CS1, 3 km auto-crop, CS1 terrain
node tools/e2e/run.mjs --apply-terrain --cs1x %LOCALAPPDATA%/cs1-to-cs2/samples/skibidi.cs1x   # reuse an export, skip CS1
node tools/e2e/run.mjs --apply-terrain --cs1x … --auto-crop 14300   # the largest crop that fits CS2 (whole city)
node tools/e2e/run.mjs --cs1x …                            # no terrain: roads on the flat M1 test spot
```

| Option | Default | Effect |
| --- | --- | --- |
| `--cs1x <file>` | – | reuse an existing `.cs1x`; without it CS1 runs the exporter |
| `--save <name>` | `Skibidi.02` | CS1 save to export |
| `--auto-crop <m>` / `--crop cx,cz,size` | `3000` | passed to `web/cli/cs1conv.ts plan` |
| `--map <name>` | `Sunshine Peninsula` with terrain (fewest base nets), `Corral Riches` without | CS2 base map for `game.new` |
| `--apply-terrain` | off | convert and apply the CS1 terrain (`terrain/`), then water and outside connections. Without it, roads go to the flat M1 test spot (Corral Riches, offset 2000, −4000) |
| `--clear-with importer\|mapprep` | `importer` | how the base map's nets are bulldozed: the importer's `clear`, or `terrain/mapprep.mjs clear` (every top-level net since 2026-11-21, like the importer's; `--keep-utilities` keeps power lines, seaways and pipes, which made the import crash once) |
| `--heights flat\|exact` | `flat` | importer height mode on real terrain (exact crashes CS2, see `cs2-importer/DECISIONS-importer.md` M2) |
| `--batch n` | 400 | importer batch size |
| `--settle s` | 20 | seconds to wait after terrain and water, before roads |
| `--keep-base-nets` | – | don't bulldoze the base map's nets |
| `--no-import` / `--no-save` / `--keep-cs2` | – | stop early / don't save / leave CS2 running and locked |
| `--name <tag>` | `e2e-<timestamp>` | city name (`cs1conv-` is prefixed) |

## Steps

Each step is timed and recorded in `summary.json`. A step marked *optional* records its failure, and the run goes on.

1. **export:** takes `%LOCALAPPDATA%\cs1-to-cs2\cs1.lock`, then `gamectl cs1 start --load <save>`, `exec` of `cs1-exporter/bin/Release/net35/Cs1Exporter.dll` (type `Cs1Exporter.Exporter`), `cs1 stop`, and releases the lock.
2. **plan:** `node web/cli/cs1conv.ts plan … --install-rb`. Road Builder configs must be in ModsData **before** CS2 starts.
3. **cs2:** takes `cs2.lock` (waits while another worker holds it, up to 60 min). If a CS2 is running anyway, it releases the lock and keeps waiting, because that game belongs to someone else. Then it starts CS2, runs `game.new` and pauses.
4. **clear:** the importer's `clear` over the whole playable area (every top-level net). `--clear-with mapprep` uses `terrain/mapprep.mjs clear`, which keeps power lines and seaways; with it, the road import crashed CS2 once (see DECISIONS-importer.md #27). The base map's highway and rail would keep the old map's height and pull the new terrain up into walls.
5. **terrain** (`--apply-terrain`): `terrain/convert.mjs`, then `terrain/apply.mjs` with sea level `auto`. The order matters: CS2 never moves existing nets when the terrain changes; it builds walls up to them. So it's new city → clear → terrain → roads → connections.
6. **water** (optional, `terrain/mapprep.mjs`): CS1 water from the `.cs1x` `water.json`. Without it the map is made dry: the base map's water sources are removed and the water is reset. Without this step the base map's river keeps flowing over the new terrain; on Plains it flooded the roads.
7. **settle:** waits a few seconds.
8. **import:** the importer with `clear`, offset 0 and flat heights on real terrain. Writes `report.json` and `import-trace.txt` (the segment ids of each batch, written before the batch, to find a batch that crashes the game).
9. **connections** (optional, mapprep): rebuilds the outside connections at the crop edge. Verified: 20 markers; cars, trucks and trains spawn.
   - **counts, water-check** (optional, mapprep): outside-connection lanes and vehicles, and CS2 vs CS1 water depth.
10. **shots, save, stop:** screenshots, then `save.write cs1conv-e2e-<yyyymmdd-hhmm>`, quit, and release the locks.

`ok` is true when every planned segment was built, none failed, and no `Temp` entities are left.

**Output:** `.gamectl/e2e/<timestamp>/` holds `summary.json`, `package/`, `terrain/`, `terrain-shots/`, `report.json`, `import-trace.txt` and `roads-*.png`.

## Locks

`%LOCALAPPDATA%\cs1-to-cs2\cs1.lock` and `cs2.lock` hold JSON: `{owner, session, since, time, pid}`. Readers also accept `{session, since}`.
- A lock held by someone else that is under 2 h old means wait.
- An older lock is stale and is taken over.
- Helpers: `cs2-importer/lib/driver.mjs` (`takeLock`, `waitAndTakeLock`, `releaseLock`).

## Verified runs (2026-11-21)

| Run | Export | Map, clear | Result |
| --- | --- | --- | --- |
| `20261121-043834` | reused `.cs1x` | Plains, importer clear | ok: 2035/2035, waited for the terrain worker's lock first |
| `20261121-044148` | **CS1 export by the script** (187 s, `cs1.lock`) | Sunshine Peninsula, `mapprep clear` | export ok; **CS2 crashed during the import** (batch 4) |
| `20261121-045036` | that run's `.cs1x` | Sunshine Peninsula, importer clear | ok: 2035/2035, 20 connections; water check: dry 150/150, wet 130/150, median depth error 0.68 m |
