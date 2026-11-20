// CS2 implementations of the game-specific bridge methods (tools/PROTOCOL.md): state, saves, load/menu/quit,
// camera, simulation speed, screenshots and the exec "tool" phase.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.Serialization.Entities;
using Colossal.PSI.Common;
using Game;
using Game.Assets;
using Game.City;
using Game.Prefabs.Modes;
using Game.UI;
using Game.SceneFlow;
using Game.Simulation;
using GameBridge;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Cs2Bridge
{
    internal sealed class Cs2Methods
    {
        // CS2's speed buttons (1x / 2x / 3x) set SimulationSystem.selectedSpeed to 1 / 2 / 4; 0 means paused.
        static readonly float[] SpeedLevels = { 1f, 2f, 4f };

        readonly GameSystemBase m_System;
        readonly BridgeCore m_Core;
        CitySystem m_City;
        CityConfigurationSystem m_CityConfig;
        SimulationSystem m_Simulation;
        string m_SaveName;      // save loaded through save.load (null when unknown)
        int m_LastSpeedLevel = 1; // speed level to report / restore while paused

        public Cs2Methods(GameSystemBase system, BridgeCore core)
        {
            m_System = system;
            m_Core = core;
        }

        World World { get { return m_System.World; } }
        EntityManager EntityManager { get { return m_System.EntityManager; } }

        public void Register()
        {
            m_City = World.GetOrCreateSystemManaged<CitySystem>();
            m_CityConfig = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            m_Simulation = World.GetOrCreateSystemManaged<SimulationSystem>();

            m_Core.Status = State;
            m_Core.IsLoading = IsLoading;
            m_Core.CaptureScreenshot = (path, supersize) => ScreenCapture.CaptureScreenshot(path, supersize);
            m_Core.ExecRunner = RunExec;

            m_Core.Register("state", "Game state (same object as ping).", c => State(), true);
            m_Core.Register("saves.list", "List saves, newest first: {limit}=50.", SavesList, true);
            m_Core.Register("save.load", "Load the save {name} (save name, or city name as fallback); replies once the city is loaded.", SaveLoad);
            m_Core.Register("menu", "Return to the main menu (unsaved progress is lost); replies once there.", Menu);
            m_Core.Register("quit", "Quit the game; replies {quitting:true} first.", Quit, true);
            m_Core.Register("camera.get", "Camera {x, y, z, distance, yaw, pitch} (pivot, zoom distance, degrees).", c => CameraGet());
            m_Core.Register("camera.set", "Set any of {x, y, z, distance, yaw, pitch}; returns the new camera.", CameraSet);
            m_Core.Register("autosave.set", "{enabled: bool}. false = block the game's autosaves (in memory only, never saved to settings); true = give them back.", AutosaveSet, true);
            m_Core.Register("sim.set", "Set {paused?, speed? 1-3}; returns {paused, speed}.", SimSet);
        }

        // ------------------------------------------------------------------ state

        bool IsLoading()
        {
            var gm = GameManager.instance;
            return gm == null || gm.isGameLoading;
        }

        bool CityLoaded()
        {
            var gm = GameManager.instance;
            if (gm == null || gm.isGameLoading || !gm.gameMode.IsGameOrEditor()) return false;
            var city = m_City.City;
            return city != Entity.Null && EntityManager.Exists(city);
        }

        string Mode()
        {
            var gm = GameManager.instance;
            if (gm == null || gm.isGameLoading) return "loading";
            var m = gm.gameMode;
            if (m.IsGame()) return "game";
            if (m.IsEditor()) return "editor";
            if (m == GameMode.MainMenu) return "menu";
            return "other";
        }

        object AutosaveSet(Command cmd)
        {
            if (!P.Has(cmd.Params, "enabled")) throw new ArgumentException("missing required param 'enabled' (bool)");
            bool enabled = P.Bool(cmd.Params, "enabled", true);
            AutosaveGuard.Request(!enabled, "autosave.set");
            AutosaveGuard.Apply(World);
            return new Dictionary<string, object> { { "autosave_blocked", AutosaveGuard.Active(World) }, { "reason", AutosaveGuard.Reason } };
        }

        Dictionary<string, object> State()
        {
            bool loaded = CityLoaded();
            int level = SpeedLevel(m_Simulation.selectedSpeed);
            if (level > 0) m_LastSpeedLevel = level;
            return new Dictionary<string, object>
            {
                { "game", "cs2" },
                { "mode", Mode() },
                { "loading", IsLoading() },
                { "city_loaded", loaded },
                { "city_name", loaded ? m_CityConfig.cityName : null },
                { "save_name", loaded ? m_SaveName : null },
                { "paused", loaded ? (object)(m_Simulation.selectedSpeed <= 0f) : null },
                { "speed", loaded ? (object)m_LastSpeedLevel : null },
                { "autosave_blocked", AutosaveGuard.Active(World) },
                { "frame", m_Core.Frame },
                { "bridge_version", Mod.Version },
            };
        }

        static int SpeedLevel(float selectedSpeed)
        {
            if (selectedSpeed <= 0f) return 0;
            for (int i = SpeedLevels.Length - 1; i >= 0; i--) if (selectedSpeed >= SpeedLevels[i] - 0.01f) return i + 1;
            return 1;
        }

        void RequireCity()
        {
            if (!CityLoaded()) throw new InvalidOperationException("no city is loaded");
        }

        // ------------------------------------------------------------------ saves

        static List<SaveGameMetadata> AllSaves()
        {
            var list = new List<SaveGameMetadata>();
            foreach (var m in AssetDatabase.global.GetAssets(default(SearchFilter<SaveGameMetadata>)))
            {
                try { if (m != null && m.isValidSaveGame && m.target != null) list.Add(m); }
                catch (Exception e) { Mod.Log.Warn("skipping save " + m + ": " + e.Message); }
            }
            list.Sort((a, b) => b.target.lastModified.CompareTo(a.target.lastModified));
            return list;
        }

        object SavesList(Command cmd)
        {
            int limit = Math.Max(1, (int)P.Num(cmd.Params, "limit", 50));
            var saves = new List<object>();
            foreach (var m in AllSaves())
            {
                if (saves.Count >= limit) break;
                var t = m.target;
                saves.Add(new Dictionary<string, object>
                {
                    { "name", m.name },
                    { "city", t.cityName },
                    { "modified", t.lastModified.ToUniversalTime() },
                    { "cloud", m.database != AssetDatabase.user },
                    { "autosave", t.autoSave },
                    { "display_name", t.displayName },
                });
            }
            return new Dictionary<string, object> { { "saves", saves } };
        }

        static SaveGameMetadata FindSave(string name)
        {
            var all = AllSaves();
            foreach (var m in all) if (string.Equals(m.name, name, StringComparison.OrdinalIgnoreCase)) return m;
            foreach (var m in all) if (string.Equals(m.target.displayName, name, StringComparison.OrdinalIgnoreCase)) return m;
            foreach (var m in all) if (string.Equals(m.target.cityName, name, StringComparison.OrdinalIgnoreCase)) return m; // newest first
            return null;
        }

        object SaveLoad(Command cmd)
        {
            var name = P.Required(cmd.Params, "name");
            var save = FindSave(name);
            if (save == null) throw new ArgumentException("no save named '" + name + "' (see saves.list)");
            var t = save.target;
            var world = World;
            // A city loaded by a script must not autosave over the user's autosaves (see AutosaveGuard).
            AutosaveGuard.Request(true, "save loaded through the bridge");
            AutosaveGuard.Apply(world);
            Task<bool> load = null;
            Exception error = null;
            m_SaveName = null;

            TaskManager.instance.EnqueueTask("SaveLoadGame", async () =>
            {
                try
                {
                    var map = world.GetOrCreateSystemManaged<MapMetadataSystem>();
                    map.mapName = t.mapName;
                    map.prefabReferences = t.prefabReferences;
                    world.GetOrCreateSystemManaged<GameModeSystem>().overrideMode = t.gameMode;
                    PlatformManager.instance.achievementsEnabled = !t.isReadonly;
                    load = GameManager.instance.Load(GameMode.Game, Purpose.LoadGame, save);
                    await load;
                }
                catch (Exception e) { error = e; }
            }, 1);

            int settle = 0;
            m_Core.AddPump(delegate
            {
                if (cmd.Abandoned) return true;
                if (error != null) { cmd.Fail("load failed: " + error.GetType().Name + ": " + error.Message); return true; }
                if (load == null || !load.IsCompleted) return false;
                if (load.IsFaulted) { cmd.Fail("load failed: " + (load.Exception.InnerException ?? load.Exception).Message); return true; }
                if (load.IsCanceled) { cmd.Fail("load was cancelled"); return true; }
                if (!load.Result) { cmd.Fail("the game reported the load as failed"); return true; }
                if (!CityLoaded()) return false;
                if (++settle < 10) return false; // let the first simulation frames run
                m_SaveName = save.name;
                cmd.Complete(new Dictionary<string, object> { { "loaded", save.name }, { "city", m_CityConfig.cityName } });
                return true;
            });
            return BridgeCore.Pending;
        }

        object Menu(Command cmd)
        {
            Task<bool> menu = null;
            Exception error = null;
            TaskManager.instance.EnqueueTask("MainMenu", async () =>
            {
                try { menu = GameManager.instance.MainMenu(); await menu; }
                catch (Exception e) { error = e; }
            }, 1);
            m_Core.AddPump(delegate
            {
                if (cmd.Abandoned) return true;
                if (error != null) { cmd.Fail("menu failed: " + error.GetType().Name + ": " + error.Message); return true; }
                if (menu == null || !menu.IsCompleted) return false;
                if (menu.IsFaulted) { cmd.Fail("menu failed: " + (menu.Exception.InnerException ?? menu.Exception).Message); return true; }
                if (menu.IsCanceled || !menu.Result) { cmd.Fail("the game reported the return to the menu as failed"); return true; }
                var gm = GameManager.instance;
                if (gm.isGameLoading || gm.gameMode != GameMode.MainMenu) return false;
                m_SaveName = null;
                cmd.Complete(new Dictionary<string, object> { { "menu", true } });
                return true;
            });
            return BridgeCore.Pending;
        }

        object Quit(Command cmd)
        {
            // Reply first, quit a few frames later so the HTTP thread can send the response.
            cmd.Complete(new Dictionary<string, object> { { "quitting", true } });
            var at = DateTime.UtcNow.AddSeconds(0.5);
            m_Core.AddPump(delegate
            {
                if (DateTime.UtcNow < at) return false;
                Mod.Log.Info("quit requested over the bridge");
                GameManager.QuitGame();
                return true;
            });
            return BridgeCore.Pending;
        }

        // ------------------------------------------------------------------ camera

        CameraController Controller()
        {
            var c = World.GetOrCreateSystemManaged<Game.Rendering.CameraUpdateSystem>().gamePlayController;
            if (c == null) throw new InvalidOperationException("no gameplay camera (is a city loaded?)");
            return c;
        }

        object CameraGet()
        {
            RequireCity();
            var c = Controller();
            var p = c.pivot;
            var a = c.angle;
            return new Dictionary<string, object>
            {
                { "x", (double)p.x }, { "y", (double)p.y }, { "z", (double)p.z },
                { "distance", (double)c.zoom }, { "yaw", (double)a.x }, { "pitch", (double)a.y },
            };
        }

        object CameraSet(Command cmd)
        {
            RequireCity();
            var c = Controller();
            var p = c.pivot;
            var pr = cmd.Params;
            if (P.Has(pr, "x")) p.x = (float)P.Num(pr, "x", p.x);
            if (P.Has(pr, "y")) p.y = (float)P.Num(pr, "y", p.y);
            if (P.Has(pr, "z")) p.z = (float)P.Num(pr, "z", p.z);
            c.pivot = p;
            if (P.Has(pr, "distance")) c.zoom = (float)P.Num(pr, "distance", c.zoom);
            var a = c.angle;
            if (P.Has(pr, "yaw")) a.x = (float)P.Num(pr, "yaw", a.x);
            if (P.Has(pr, "pitch")) a.y = (float)P.Num(pr, "pitch", a.y);
            c.angle = a;
            return CameraGet();
        }

        // ------------------------------------------------------------------ simulation

        object SimSet(Command cmd)
        {
            RequireCity();
            var pr = cmd.Params;
            if (P.Has(pr, "speed"))
            {
                int level = (int)Math.Round(P.Num(pr, "speed", m_LastSpeedLevel));
                if (level < 1 || level > SpeedLevels.Length) throw new ArgumentException("speed must be 1, 2 or 3");
                m_LastSpeedLevel = level;
            }
            bool paused = P.Bool(pr, "paused", m_Simulation.selectedSpeed <= 0f);
            m_Simulation.selectedSpeed = paused ? 0f : SpeedLevels[m_LastSpeedLevel - 1];
            return new Dictionary<string, object>
            {
                { "paused", paused }, { "speed", m_LastSpeedLevel }, { "selected_speed", (double)m_Simulation.selectedSpeed },
            };
        }

        // ------------------------------------------------------------------ exec phases

        void RunExec(Command cmd, MethodInfo method, object[] args)
        {
            var phase = P.Str(cmd.Params, "phase", "main");
            if (phase == "main") { m_Core.RunExecNow(cmd, method, args); return; }
            if (phase != "tool") throw new ArgumentException("phase must be \"main\" or \"tool\"");
            if (!CityLoaded()) throw new InvalidOperationException("phase \"tool\" needs a loaded city (ToolUpdate does not run in the menu)");
            BridgeToolPhaseSystem.Enqueue(cmd, method, args);
            int frames = 0;
            m_Core.AddPump(delegate
            {
                if (!BridgeToolPhaseSystem.IsQueued(cmd)) return true;
                if (cmd.Abandoned || ++frames > 600)
                {
                    BridgeToolPhaseSystem.Remove(cmd);
                    cmd.Fail("the ToolUpdate phase did not run within 600 frames");
                    return true;
                }
                return false;
            });
        }
    }
}
