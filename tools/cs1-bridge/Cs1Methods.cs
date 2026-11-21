// CS1-specific bridge methods (tools/PROTOCOL.md): state, saves.list, save.load, menu, quit, camera.get/set, sim.set, autosave.set.
// Everything here runs on the game's main thread (BridgeCore.Tick from BridgeRunner.Update).
// Load / unload follow what the game's own UI does (LoadPanel.LoadRoutine, PauseMenu), verified by IL inspection.
using System;
using System.Collections.Generic;
using ColossalFramework;
using ColossalFramework.Packaging;
using ColossalFramework.PlatformServices;
using GameBridge;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cs1Bridge
{
    static class Cs1Methods
    {
        static BridgeCore s_Core;
        static string s_SaveName; // name of the save the current city was loaded from via save.load

        public static void Register(BridgeCore core)
        {
            s_Core = core;
            core.Status = State;
            core.IsLoading = IsLoading;
            core.Register("state", "Game state (same object as ping).", c => State(), true);
            core.Register("saves.list", "Saved games, newest first: {saves:[{name, city, modified, cloud, autosave}]}; {limit}=50.", SavesList, true);
            core.Register("save.load", "Load the save {name} (save name, city name as fallback); replies once the city is loaded.", SaveLoad);
            core.Register("menu", "Unload the city (unsaved progress is lost) and reply once back at the main menu.", Menu);
            core.Register("quit", "Quit the game (unsaved progress is lost).", Quit, true);
            core.Register("camera.get", "Camera {x, y, z (pivot), distance, yaw, pitch (degrees)}.", c => CameraGet());
            core.Register("camera.set", "Move the camera: any of {x, y, z, distance, yaw, pitch}, {instant}=true.", CameraSet);
            core.Register("sim.set", "Set {paused} and/or {speed} (1-3).", SimSet);
            core.Register("autosave.set", "{enabled: bool}. false = hold back the game's autosaves (in memory only, never saved to settings); true = give them back.", AutosaveSet, true);
        }

        // ------------------------------------------------------------------ state

        static LoadingManager Loading { get { return Singleton<LoadingManager>.exists ? Singleton<LoadingManager>.instance : null; } }

        static bool IsLoading()
        {
            var lm = Loading;
            return lm != null && lm.m_currentlyLoading;
        }

        // At startup the active scene is IntroScreen with MainMenu loaded additively, so look at every loaded scene.
        static bool MainMenuLoaded()
        {
            try
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    if (SceneManager.GetSceneAt(i).name == "MainMenu") return true;
            }
            catch { }
            return false;
        }

        static bool IsGameMode(SimulationManager.UpdateMode m)
        {
            switch (m)
            {
                case SimulationManager.UpdateMode.LoadGame:
                case SimulationManager.UpdateMode.NewGameFromMap:
                case SimulationManager.UpdateMode.NewGameFromScenario:
                    return true;
                default:
                    return false;
            }
        }

        static string Mode()
        {
            var lm = Loading;
            if (lm == null) return "other";
            if (lm.m_currentlyLoading) return "loading";
            if (MainMenuLoaded()) return "menu";
            // m_loadingComplete stays true after an unload, so it only counts outside the main menu.
            if (!lm.m_loadingComplete || !Singleton<SimulationManager>.exists) return "other";
            var meta = Singleton<SimulationManager>.instance.m_metaData;
            if (meta == null) return "other";
            if (IsGameMode(meta.m_updateMode)) return "game";
            return meta.m_updateMode == SimulationManager.UpdateMode.Undefined ? "other" : "editor";
        }

        static bool CityLoaded() { return Mode() == "game"; }

        // SavePanel.m_LastSaveName (private): the save the game last loaded or saved through its own UI.
        static readonly System.Reflection.FieldInfo s_LastSaveName = typeof(SavePanel).GetField("m_LastSaveName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        static string LastSaveName()
        {
            try { var n = s_LastSaveName == null ? null : s_LastSaveName.GetValue(null) as string; return string.IsNullOrEmpty(n) ? null : n; }
            catch { return null; }
        }

        static Dictionary<string, object> State()
        {
            var mode = Mode();
            bool city = mode == "game";
            object cityName = null, saveName = null, paused = null, speed = null;
            if (city && Singleton<SimulationManager>.exists)
            {
                var sm = Singleton<SimulationManager>.instance;
                if (sm.m_metaData != null) cityName = sm.m_metaData.m_CityName;
                paused = sm.SimulationPaused;
                speed = sm.SelectedSimulationSpeed;
                saveName = s_SaveName ?? LastSaveName();
            }
            return new Dictionary<string, object>
            {
                { "game", "cs1" },
                { "mode", mode },
                { "loading", mode == "loading" },
                { "city_loaded", city },
                { "city_name", cityName },
                { "save_name", saveName },
                { "paused", paused },
                { "speed", speed },
                { "frame", s_Core != null ? s_Core.Frame : 0 },
                { "bridge_version", Bridge.Version },
                { "autosave_blocked", AutosaveGuard.Active },
            };
        }

        // ------------------------------------------------------------------ saves

        sealed class SaveEntry
        {
            public Package.Asset Asset;
            public SaveGameMetaData Meta;
        }

        static List<SaveEntry> Saves()
        {
            // Same filter as LoadPanel.Refresh (includes Steam Cloud saves).
            var list = new List<SaveEntry>();
            foreach (var asset in PackageManager.FilterAssets(UserAssetType.SaveGameMetaData))
            {
                if (asset == null || !asset.isEnabled || PackageHelper.IsDemoModeSave(asset)) continue;
                SaveGameMetaData meta;
                try { meta = asset.Instantiate<SaveGameMetaData>(); }
                catch (Exception e) { Bridge.Log("save '" + asset.name + "' failed to read: " + e.Message); continue; }
                if (meta == null) continue;
                list.Add(new SaveEntry { Asset = asset, Meta = meta });
            }
            list.Sort((a, b) => b.Meta.timeStamp.CompareTo(a.Meta.timeStamp));
            return list;
        }

        static bool IsCloud(Package.Asset asset)
        {
            try { return asset.package != null && PackageManager.IsCloudPath(asset.package.packagePath); } catch { return false; }
        }

        static object SavesList(Command cmd)
        {
            int limit = (int)P.Num(cmd.Params, "limit", 50);
            var saves = new List<object>();
            foreach (var s in Saves())
            {
                if (limit > 0 && saves.Count >= limit) break;
                saves.Add(new Dictionary<string, object>
                {
                    { "name", s.Asset.name },
                    { "city", s.Meta.cityName },
                    { "modified", s.Meta.timeStamp },
                    { "cloud", IsCloud(s.Asset) },
                    { "autosave", s.Asset.name != null && s.Asset.name.StartsWith("AutoSave", StringComparison.OrdinalIgnoreCase) },
                });
            }
            return new Dictionary<string, object> { { "saves", saves } };
        }

        static SaveEntry FindSave(string name)
        {
            var saves = Saves(); // newest first, so the first city-name match is the newest save of that city
            foreach (var s in saves) if (s.Asset.name == name) return s;
            foreach (var s in saves) if (string.Equals(s.Asset.name, name, StringComparison.OrdinalIgnoreCase)) return s;
            foreach (var s in saves) if (s.Meta.cityName == name) return s;
            foreach (var s in saves) if (string.Equals(s.Meta.cityName, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        static object SaveLoad(Command cmd)
        {
            var name = P.Required(cmd.Params, "name");
            if (SavePanel.isSaving) throw new InvalidOperationException("the game is saving; retry later");
            var lm = Loading;
            if (lm == null || lm.m_currentlyLoading) throw new InvalidOperationException("the game is loading; retry when state.loading is false");
            var save = FindSave(name);
            if (save == null) throw new ArgumentException("no save named '" + name + "' (see saves.list)");

            // What LoadPanel.LoadRoutine does (minus the UI and the environment / theme override dropdowns).
            var asset = save.Asset;
            var meta = save.Meta;
            SavePanel.lastLoadedName = asset.name;
            SavePanel.lastCloudSetting = IsCloud(asset);
            var ngs = new SimulationMetaData
            {
                m_CityName = meta.cityName,
                m_updateMode = SimulationManager.UpdateMode.LoadGame,
            };
            if (asset.package != null && asset.package.GetPublishedFileID() != PublishedFileId.invalid)
                ngs.m_disableAchievements = SimulationMetaData.MetaBool.True;
            if (!string.IsNullOrEmpty(meta.mapThemeRef))
            {
                var theme = PackageManager.FindAssetByName(meta.mapThemeRef, UserAssetType.MapThemeMetaData);
                if (theme != null)
                {
                    ngs.m_MapThemeMetaData = theme.Instantiate<MapThemeMetaData>();
                    ngs.m_MapThemeMetaData.SetSelfRef(theme);
                }
            }

            // A city loaded by a script must not autosave over the user's AutoSave (see AutosaveGuard).
            AutosaveGuard.Request(true, "save loaded through the bridge");
            AutosaveGuard.Apply();
            Bridge.Log("loading save '" + asset.name + "' (city '" + meta.cityName + "')");
            s_SaveName = null;
            lm.LoadLevel(meta.assetRef, "Game", "InGame", ngs, false);
            if (!lm.m_currentlyLoading) throw new InvalidOperationException("LoadLevel did not start (game quitting or already loading?)");

            string loadedName = asset.name;
            int settle = 0;
            s_Core.AddPump(delegate
            {
                if (cmd.Abandoned) return true; // the load itself cannot be cancelled; just stop watching
                if (lm.m_currentlyLoading) { settle = 0; return false; }
                if (!CityLoaded())
                {
                    cmd.Fail("loading finished but no city is loaded (mode " + Mode() + "); see output_log.txt");
                    return true;
                }
                s_SaveName = loadedName;
                // Mods' OnLevelLoaded run in the same frame the flags flip; give the game two more frames to settle.
                if (++settle < 3) return false;
                cmd.Complete(new Dictionary<string, object> { { "loaded", loadedName }, { "city", Singleton<SimulationManager>.instance.m_metaData.m_CityName } });
                return true;
            });
            return BridgeCore.Pending;
        }

        static object AutosaveSet(Command cmd)
        {
            object v;
            if (!cmd.Params.TryGetValue("enabled", out v) || !(v is bool)) throw new ArgumentException("enabled (bool) is required");
            AutosaveGuard.Request(!(bool)v, "autosave.set");
            AutosaveGuard.Apply();
            return new Dictionary<string, object> { { "autosave_blocked", AutosaveGuard.Active }, { "reason", AutosaveGuard.Reason } };
        }

        // ------------------------------------------------------------------ menu / quit

        static object Menu(Command cmd)
        {
            var lm = Loading;
            if (lm == null) throw new InvalidOperationException("LoadingManager is not available");
            if (Mode() == "menu") return new Dictionary<string, object> { { "menu", true } };
            if (SavePanel.isSaving) throw new InvalidOperationException("the game is saving; retry later");
            Bridge.Log("unloading level (menu)");
            s_SaveName = null;
            lm.UnloadLevel(); // what PauseMenu's "Main menu" button does
            int frames = 0;
            s_Core.AddPump(delegate
            {
                if (cmd.Abandoned) return true;
                frames++;
                if (lm.m_currentlyLoading || frames < 2) return false;
                if (!MainMenuLoaded()) return false;
                cmd.Complete(new Dictionary<string, object> { { "menu", true } });
                return true;
            });
            return BridgeCore.Pending;
        }

        static object Quit(Command cmd)
        {
            Bridge.Log("quit requested");
            // Reply first, then quit a few frames later so the HTTP response gets out before the process exits.
            int frames = 0;
            s_Core.AddPump(delegate
            {
                if (++frames < 10) return false;
                Singleton<LoadingManager>.instance.QuitApplication();
                return true;
            });
            return new Dictionary<string, object> { { "quitting", true } };
        }

        // ------------------------------------------------------------------ camera

        static CameraController Camera()
        {
            var cc = ToolsModifierControl.cameraController;
            if (cc == null) throw new InvalidOperationException("no camera controller (load a city first)");
            return cc;
        }

        static Dictionary<string, object> CameraGet()
        {
            var cc = Camera();
            return new Dictionary<string, object>
            {
                { "x", cc.m_targetPosition.x }, { "y", cc.m_targetPosition.y }, { "z", cc.m_targetPosition.z },
                { "distance", cc.m_targetSize }, { "yaw", cc.m_targetAngle.x }, { "pitch", cc.m_targetAngle.y },
            };
        }

        static object CameraSet(Command cmd)
        {
            var cc = Camera();
            var p = cmd.Params;
            var pos = cc.m_targetPosition;
            bool moved = P.Has(p, "x") || P.Has(p, "z");
            pos.x = (float)P.Num(p, "x", pos.x);
            pos.z = (float)P.Num(p, "z", pos.z);
            if (P.Has(p, "y")) pos.y = (float)P.Num(p, "y", pos.y);
            else if (moved && Singleton<TerrainManager>.exists) pos.y = Singleton<TerrainManager>.instance.SampleRawHeightSmoothWithWater(pos, false, 0f);
            float size = Mathf.Clamp((float)P.Num(p, "distance", cc.m_targetSize), cc.m_minDistance, cc.m_maxDistance);
            var angle = new Vector2((float)P.Num(p, "yaw", cc.m_targetAngle.x), (float)P.Num(p, "pitch", cc.m_targetAngle.y));

            cc.m_targetPosition = pos;
            cc.m_targetSize = size;
            cc.m_targetAngle = angle;
            if (P.Bool(p, "instant", true))
            {
                cc.m_currentPosition = pos;
                cc.m_currentSize = size;
                cc.m_currentAngle = angle;
            }
            // Reply after the controller has run (and applied its own clamps) for a couple of frames.
            int frames = 0;
            s_Core.AddPump(delegate
            {
                if (cmd.Abandoned) return true;
                if (++frames < 3) return false;
                try { cmd.Complete(CameraGet()); } catch (Exception e) { cmd.Fail(e.Message); }
                return true;
            });
            return BridgeCore.Pending;
        }

        // ------------------------------------------------------------------ simulation

        static object SimSet(Command cmd)
        {
            if (!CityLoaded()) throw new InvalidOperationException("no city loaded");
            var sm = Singleton<SimulationManager>.instance;
            // Speed first: changing the speed unpauses the game (SimulationManager.set_SelectedSimulationSpeed).
            if (P.Has(cmd.Params, "speed"))
            {
                int speed = (int)P.Num(cmd.Params, "speed", sm.SelectedSimulationSpeed);
                if (speed < 1 || speed > 3) throw new ArgumentException("speed must be 1, 2 or 3");
                sm.SelectedSimulationSpeed = speed;
            }
            if (P.Has(cmd.Params, "paused")) sm.SimulationPaused = P.Bool(cmd.Params, "paused", sm.SimulationPaused);
            return new Dictionary<string, object> { { "paused", sm.SimulationPaused }, { "speed", sm.SelectedSimulationSpeed } };
        }
    }
}
