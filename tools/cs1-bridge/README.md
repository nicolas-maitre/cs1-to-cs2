# Cs1Bridge

Cities: Skylines 1 bridge mod. It implements [`tools/PROTOCOL.md`](../PROTOCOL.md) on `127.0.0.1:17761`.

| File | What it does |
| --- | --- |
| `Mod.cs` | `IUserMod` "CS1→CS2 Bridge". `OnEnabled` starts `BridgeServer` and `BridgeCore`, plus a `DontDestroyOnLoad` runner whose `Update()` calls `Tick()`. `OnDisabled` stops them. |
| `Cs1Methods.cs` | The CS1 methods: `state`, `saves.list`, `save.load`, `menu`, `quit`, `camera.get`, `camera.set`, `sim.set`. |
| `../bridge-common/*.cs` | The shared server and dispatcher, compiled in. |
| `enable-mod.ps1` | Enables the mod in `userGameState.cgs` on a fresh install, without opening Content Manager. |
| `examples/HelloExec/` | An example `exec` library. It is not deployed. |

## Build and install

1. Build and deploy:

   ```bash
   dotnet build -c Release
   ```

   This copies `Cs1Bridge.dll` into `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\Cs1Bridge`. If the game is running, CS1 picks up the changed DLL and reloads the mod. Each build gets a new version number, which is what makes this reload work.

2. Once per install, with the game closed, enable the mod:

   ```bash
   powershell -ExecutionPolicy Bypass -File enable-mod.ps1 -Write
   ```

   CS1 keeps new local mods disabled. The script backs up `userGameState.cgs` the first time it writes.

## Logs

Log lines start with `[Cs1Bridge]` and go to `<CS1>\Cities_Data\output_log.txt`.

## Example exec call

```bash
cd examples/HelloExec && dotnet build
```

```json
{"method":"exec","params":{"dll":"C:/…/HelloExec/bin/Debug/net35/HelloExec.dll","args":{"note":"hi"}}}
```

Add `"method":"RunLater"` to `params` to run the coroutine variant. It waits 3 frames before it replies.

Results from the Skibidi.02 save:

```json
{"greeting":"hello v1","note":"hi","city":"Skibidi","segments":7745,"nodes":7009}
```
