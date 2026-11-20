# gamectl

Start, stop and drive Cities: Skylines 1 and 2 from scripts, through the bridge mods in `tools/cs1-bridge/` and `tools/cs2-bridge/`. The wire contract is [`tools/PROTOCOL.md`](../PROTOCOL.md).

Node ≥ 20, no npm dependencies, no install step.

```bash
node tools/gamectl/bin/gamectl.js cs2 status
```

On Windows you can also call `tools\gamectl\gamectl.cmd cs2 status`. Running `npm link` in `tools/gamectl` puts `gamectl` on your PATH.

## Commands

`<game>` is `cs1` or `cs2`. Every command accepts these options:

- `--json`: prints one JSON object on stdout, `{"ok":true,"result":...}` or `{"ok":false,"code":...,"error":...}`.
- `--quiet`: drops the progress lines `start`/`stop` write to stderr.

Without `--json` you get short human text.

| Command | What it does |
| --- | --- |
| `start [--load <save>] [--flags "..."] [--timeout 300] [--load-timeout 900]` | Launches the game directly, without the Paradox launcher, and waits for the bridge. If the bridge already answers, it reuses that game. See [How `start` decides the game is ready](#how-start-decides-the-game-is-ready). |
| `stop [--force] [--wait 60]` | Sends the rpc `quit` and waits up to `--wait` s for the process to exit. If it is still running after that, or the bridge doesn't answer, it runs `taskkill /T /F`. `--force` kills at once. |
| `status` | Shows whether the game is running, its pids, the discovery file and the `/health` snapshot. Exits 0 only when the bridge is ready, and 3 otherwise. |
| `call <method> [jsonParams] [--timeout s]` | Raw RPC, for example `call camera.set '{"x":0,"z":0}'`. `--timeout` is sent as `timeout_s`. |
| `state`, `ping`, `methods` | Print the state object, or the list of methods. |
| `saves [--limit n]` | `saves.list` |
| `load <save> [--timeout 900]` | `save.load`. The bridge replies once `city_loaded` is true. |
| `menu` | Returns to the main menu. Unsaved progress is lost. |
| `camera [x z [distance yaw pitch]] [--y n] [--smooth]` | With no arguments it prints the camera (`camera.get`); with arguments it calls `camera.set`. Negative numbers work as written: `camera -1200 300`. |
| `pause`, `resume`, `speed <n>` | `sim.set` |
| `wait <frames> [--seconds s]` | Waits until the frames (and the seconds, if given) have passed in the game. |
| `screenshot [file.png] [--supersize 1-4]` | Without a file it writes `<repo>/.gamectl/shots/<game>-<yyyymmdd-hhmmss-ms>.png`. A relative file is resolved against the current folder, `.png` is added when missing, and a folder argument gets the default file name. Prints the path. |
| `exec <dll> [--type T] [--method M] [--args json] [--phase main\|tool]` | Hot-loads a DLL and runs its entry point. The DLL path is made absolute. `--phase` exists only on CS2. |
| `log [--lines 50] [--grep re] [--follow] [--file name\|path]` | Tails the game's logs. `--grep` is a case-insensitive JS regex. With `--json`, `--follow` prints NDJSON `{file,line}`. |

`log` reads these files by default:

| Game | Files |
| --- | --- |
| CS2 | `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Player.log`, plus `Logs\SceneFlow.log`, `Logs\*Bridge*.log` and `Logs\Modding.log` |
| CS1 | `<CS1>\Cities_Data\output_log.txt` |

A bare `--file` name is looked up in the game's log folder and then in its parent, for example `--file Player.log` or `--file Logs\UI.log`.

### Examples

```bash
gamectl cs2 start --load "Skibidi.02"
gamectl cs2 camera 0 0 1500 0 60
gamectl cs2 wait 30
gamectl cs2 screenshot
gamectl cs2 exec path/to/Spike.dll --phase tool --args '{"count":3}' --timeout 120
gamectl cs2 log --grep "exception|bridge" --lines 20
gamectl cs2 stop
```

### Exit codes

| Code | Meaning |
| --- | --- |
| 0 | ok |
| 1 | error, including a bridge answering 400 (unknown method, handler threw, game loading) |
| 2 | usage: bad arguments, unknown command, invalid JSON, DLL not found |
| 3 | not running: no game process, no or stale discovery file, or the bridge is unreachable |
| 4 | auth: 401 (wrong token), 403 or 415 |
| 5 | timeout: a 504 from the bridge (`executed` in `--json` output is `false` when the command never ran, `"unknown"` when it timed out while running), or no answer within `timeout_s` + 15 s |
| 6 | start or stop failed: Steam not running, exe not found, the game exited during startup, the ready marker or the bridge didn't arrive in time, or the process survived a kill |

## How `start` decides the game is ready

1. **Steam check.** `steam.exe` must be running, or `start` refuses.
2. **Reuse.** If the discovery file names a live pid and `/health` on its port answers with that pid, the running game is reused. If `Cities2.exe` / `Cities.exe` is running but the bridge doesn't answer, `start` waits for that process's bridge instead of spawning a second game.
3. **Snapshot, then spawn.** Before spawning, `start` records the marker log's size and creation time:
   - CS2: `Logs\SceneFlow.log`, marker `MainMenu reached`.
   - CS1: `Cities_Data\output_log.txt`, marker `Scene [MainMenu] loaded`.

   It then spawns the exe detached, with `SteamAppId` and `SteamGameId` set (CS2 949230, CS1 255710). CS2 always gets `-developerMode`, followed by your `--flags`.
4. **Ready marker.** Only bytes written after the snapshot are scanned for the marker, so a marker left by the previous run never counts. If the file shrank or its creation time changed (Unity recreates its logs), it is read again from offset 0, because all of it is new.
5. **Bridge.** After the marker, `start` needs all three of these:
   - a discovery file whose `pid` is the spawned process and whose mtime is after the spawn;
   - `/health` answering with that pid;
   - a non-null `status`, meaning the main thread has ticked.

   If the spawned process exits, for example because Steam relaunched it, a new process with the game's image name is accepted for up to 30 s.
6. **Load.** With `--load`, `start` calls `save.load` with a long `timeout_s` (`--load-timeout`, default 900) and then polls `state` until `city_loaded` is true.

On timeout `start` exits with 6 and leaves the game running. The error message says which stage it was waiting for.

## As a module

```js
import { connect, start, stop, resolveShotPath } from '../tools/gamectl/lib/index.js';

const { bridge: cs2 } = await start('cs2', { load: 'Skibidi.02' }); // or: await connect('cs2')
await cs2.cameraSet({ x: 0, z: 0, distance: 1500 });
await cs2.wait(30);
await cs2.screenshot(resolveShotPath('cs2'));
const r = await cs2.exec('path/to/Spike.dll', { phase: 'tool', args: { n: 3 }, timeout: 120 });
await cs2.call('camera.get');
await stop('cs2');
```

A `Bridge` has these methods:

- `call(method, params, {timeout})`
- `health()`, `ping()`, `state()`, `methods()`
- `saves(limit)`, `load(name)`, `menu()`, `quit()`
- `cameraGet()`, `cameraSet(cam)`
- `pause()`, `resume()`, `speed(n)`
- `wait(frames, seconds)`
- `screenshot(absPath, {supersize})`
- `exec(dll, {type, method, args, phase, timeout})`

Errors are `GamectlError`, which carries:

- `code`: one of the names in the exit code table;
- `status`: the HTTP status;
- `executed`: for timeouts, as described in the exit code table;
- `exitCode`.

`start`, `stop` and `status` take `{config, deps}` overrides for the game paths and the process helpers, which is how the tests run without a real game.

## Configuration

| What | Source |
| --- | --- |
| Install folders | `CS1Managed` / `CS2Managed` in `local.props` at the repo root, two folders up. Override with env `CS1_DIR` / `CS2_DIR`. |
| Discovery folder | `%LOCALAPPDATA%\cs1-to-cs2\bridge`. Override with `GAMECTL_BRIDGE_DIR`. |
| CS2 data folder (logs) | Override with `GAMECTL_CS2_DATA_DIR`. |
| Repo root (screenshots) | Override with `GAMECTL_REPO_ROOT`. |

## Tests

```bash
cd tools/gamectl && npm test
```

The tests never touch the real games. `test/fake-bridge.js` implements PROTOCOL.md: `/health`, `/rpc`, token and 401/403/415, and 504 with `executed`. It works two ways:

- **As a module:** an in-process bridge.
- **As a script:** a fake game process, which writes a ready marker after a delay, starts its bridge with its own pid, and exits on `quit`.

Lifecycle tests spawn that script as the "exe", with Steam and the process list injected.
