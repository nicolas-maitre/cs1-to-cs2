// CS2 bridge methods for converter test cities: maps.list, game.new (new city from a map) and save.write (cs1conv-* only).
// Recipes read from the game's IL (Game.dll, CS2 1.6.0f1):
//   game.new   = Game.UI.Menu.MenuUISystem.NewGame (<NewGame>d__64), minus ApplyOptions' write to the user's settings
//   save.write = Game.AutoSaveSystem.AutoSave (<AutoSave>d__13): preview render target, delete the old asset, GameManager.Save
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Colossal;
using Colossal.IO.AssetDatabase;
using Colossal.PSI.Common;
using Colossal.Serialization.Entities;
using Game;
using Game.Assets;
using Game.City;
using Game.Prefabs.Modes;
using Game.SceneFlow;
using Game.Simulation;
using Game.UI;
using Game.UI.Menu;
using GameBridge;
using UnityEngine;

namespace Cs2Bridge
{
    internal sealed partial class Cs2Methods
    {
        /// <summary>save.write refuses every other name, so a script can never overwrite the user's own saves.</summary>
        public const string SavePrefix = "cs1conv-";

        void RegisterCityMethods()
        {
            m_Core.Register("maps.list", "List playable maps: {limit}=100. Each {id, name, theme, area, buildable_land, starting_year, ...}.", MapsList, true);
            m_Core.Register("game.new", "Start a new city: {map (id or name), name?, theme?, options?{unlockAll, unlimitedMoney, unlockMapTiles, leftHandTraffic, naturalDisasters}}; replies once the city is loaded.", GameNew);
            m_Core.Register("save.write", "Save the loaded city as {name}; the name must start with \"" + SavePrefix + "\" (an existing save of that name is overwritten).", SaveWrite);
        }

        // ------------------------------------------------------------------ maps

        static List<MapMetadata> AllMaps()
        {
            var list = new List<MapMetadata>();
            foreach (var m in AssetDatabase.global.GetAssets(default(SearchFilter<MapMetadata>)))
            {
                try { if (m != null && m.target != null) list.Add(m); }
                catch (Exception e) { Mod.Log.Warn("skipping map " + m + ": " + e.Message); }
            }
            list.Sort((a, b) => string.Compare(a.target.displayName, b.target.displayName, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>The menu's rule (MenuUISystem.FilterMaps): a built-in map is playable when its content
        /// prerequisites are met (its isPlayable flag is false); any other map uses isPlayable.</summary>
        static bool Playable(MapMetadata m)
        {
            try { return m.database == AssetDatabase.game ? GameManager.instance.ArePrerequisitesMet(m) : m.target.isPlayable; }
            catch { return false; }
        }

        object MapsList(Command cmd)
        {
            int limit = Math.Max(1, (int)P.Num(cmd.Params, "limit", 100));
            var maps = new List<object>();
            foreach (var m in AllMaps())
            {
                if (maps.Count >= limit) break;
                var t = m.target;
                maps.Add(new Dictionary<string, object>
                {
                    { "id", t.id },
                    { "name", t.displayName },
                    { "asset", m.name },
                    { "theme", t.theme },
                    { "area", (double)t.area },
                    { "buildable_land", (double)t.buildableLand },
                    { "starting_year", t.startingYear },
                    { "playable", Playable(m) },
                    { "readonly", t.isReadonly },
                    { "cloud", m.database != AssetDatabase.game && m.database != AssetDatabase.user },
                });
            }
            return new Dictionary<string, object> { { "maps", maps } };
        }

        static MapMetadata FindMap(string key)
        {
            var all = AllMaps();
            foreach (var m in all) if (string.Equals(m.target.id, key, StringComparison.OrdinalIgnoreCase)) return m;
            foreach (var m in all) if (string.Equals(m.target.displayName, key, StringComparison.OrdinalIgnoreCase)) return m;
            foreach (var m in all) if (string.Equals(m.name, key, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        // ------------------------------------------------------------------ new city

        object GameNew(Command cmd)
        {
            var key = P.Required(cmd.Params, "map");
            var meta = FindMap(key);
            if (meta == null) throw new ArgumentException("no map '" + key + "' (see maps.list)");
            if (!Playable(meta)) throw new InvalidOperationException("map '" + meta.target.displayName + "' is not playable (missing content?)");
            var map = meta.target;
            string cityName = P.Str(cmd.Params, "name", null) ?? (SavePrefix + "city");
            string theme = P.Str(cmd.Params, "theme", null) ?? map.theme;
            var opts = P.Obj(cmd.Params, "options");
            var world = World;

            // Same steps as MenuUISystem.NewGame. The options go to CityConfigurationSystem's overrides only; the menu
            // also writes them to the user's settings (UserState.ApplyAndSave), which a script must not do.
            var mapMeta = world.GetOrCreateSystemManaged<MapMetadataSystem>();
            mapMeta.mapName = map.displayName;
            mapMeta.prefabReferences = map.prefabReferences;
            var config = m_CityConfig;
            config.overrideLoadedOptions = true;
            config.overrideThemeName = theme;
            config.overrideCityName = cityName;
            config.overrideLeftHandTraffic = P.Bool(opts, "leftHandTraffic", false);
            config.overrideNaturalDisasters = P.Bool(opts, "naturalDisasters", false);
            config.overrideUnlockAll = P.Bool(opts, "unlockAll", true);
            config.overrideUnlimitedMoney = P.Bool(opts, "unlimitedMoney", true);
            config.overrideUnlockMapTiles = P.Bool(opts, "unlockMapTiles", true);
            world.GetOrCreateSystemManaged<GameModeSystem>().overrideMode = ""; // normal game mode
            world.GetOrCreateSystemManaged<TimeSystem>().startingYear = map.startingYear == -1 ? DateTime.Now.Year : map.startingYear;
            PlatformManager.instance.achievementsEnabled = false;

            // A script-started city must not autosave over the user's autosaves (see AutosaveGuard).
            AutosaveGuard.Request(true, "city started through the bridge");
            AutosaveGuard.Apply(world);
            m_SaveName = null;

            Task<bool> load = null;
            Exception error = null;
            TaskManager.instance.EnqueueTask("NewGame", async () =>
            {
                try { load = GameManager.instance.Load(GameMode.Game, Purpose.NewGame, meta); await load; }
                catch (Exception e) { error = e; }
            }, 1);

            int settle = 0;
            m_Core.AddPump(delegate
            {
                if (cmd.Abandoned) return true;
                if (error != null) { cmd.Fail("new game failed: " + error.GetType().Name + ": " + error.Message); return true; }
                if (load == null || !load.IsCompleted) return false;
                if (load.IsFaulted) { cmd.Fail("new game failed: " + (load.Exception.InnerException ?? load.Exception).Message); return true; }
                if (load.IsCanceled) { cmd.Fail("new game was cancelled"); return true; }
                if (!load.Result) { cmd.Fail("the game reported the new game as failed"); return true; }
                if (!CityLoaded()) return false;
                if (++settle < 10) return false;
                cmd.Complete(new Dictionary<string, object>
                {
                    { "map", map.displayName }, { "map_id", map.id }, { "city", m_CityConfig.cityName }, { "theme", theme },
                    { "unlock_all", config.unlockAll }, { "unlimited_money", config.unlimitedMoney }, { "unlock_map_tiles", config.unlockMapTiles },
                });
                return true;
            });
            return BridgeCore.Pending;
        }

        // ------------------------------------------------------------------ save

        static string CheckSaveName(string name)
        {
            name = name.Trim();
            if (!name.StartsWith(SavePrefix, StringComparison.Ordinal))
                throw new ArgumentException("save.write only writes saves whose name starts with \"" + SavePrefix + "\" (got '" + name + "')");
            if (name.Length == SavePrefix.Length || name.Length > 100) throw new ArgumentException("save name must be " + (SavePrefix.Length + 1) + "-100 characters");
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
                    throw new ArgumentException("save name may only contain letters, digits, '-', '_' and '.'");
            return name;
        }

        object SaveWrite(Command cmd)
        {
            var name = CheckSaveName(P.Required(cmd.Params, "name"));
            RequireCity();
            if (!GameManager.instance.gameMode.IsGame()) throw new InvalidOperationException("save.write needs a city in game mode (not the editor)");
            var world = World;
            var menu = world.GetOrCreateSystemManaged<MenuUISystem>();
            ILocalAssetDatabase db = AssetDatabase.user;

            bool overwritten = false;
            PackageAsset existing;
            if (db.Exists<PackageAsset>(SaveHelpers.GetAssetDataPath<SaveGameMetadata>(db, name), out existing))
            {
                db.DeleteAsset(existing);
                overwritten = true;
            }

            // Preview: exactly what AutoSaveSystem renders (680×383, GraphicsFormat 8 = R8G8B8A8_SRGB).
            var preview = ScreenCaptureHelper.CreateRenderTarget("PreviewSaveGame-Bridge", 680, 383, (UnityEngine.Experimental.Rendering.GraphicsFormat)8);
            ScreenCaptureHelper.CaptureScreenshot(Camera.main, preview, new MenuHelpers.SaveGamePreviewSettings());
            SaveInfo info = menu.GetSaveInfo(false);

            Task<bool> save = null;
            Exception error = null;
            var started = DateTime.UtcNow;
            TaskManager.instance.EnqueueTask("SaveGame", async () =>
            {
                try { save = GameManager.instance.Save(name, info, db, preview); await save; }
                catch (Exception e) { error = e; }
            }, 1);

            m_Core.AddPump(delegate
            {
                // Keep waiting even when the caller gave up: the preview must outlive the save.
                if (error == null && (save == null || !save.IsCompleted)) return false;
                Cleanup(preview);
                if (error != null) { cmd.Fail("save failed: " + error.GetType().Name + ": " + error.Message); return true; }
                if (save.IsFaulted) { cmd.Fail("save failed: " + (save.Exception.InnerException ?? save.Exception).Message); return true; }
                if (save.IsCanceled || !save.Result) { cmd.Fail("the game reported the save as failed"); return true; }
                m_SaveName = name;
                cmd.Complete(new Dictionary<string, object>
                {
                    { "saved", name }, { "city", m_CityConfig.cityName }, { "overwritten", overwritten },
                    { "seconds", Math.Round((DateTime.UtcNow - started).TotalSeconds, 2) },
                });
                return true;
            });
            return BridgeCore.Pending;
        }

        static void Cleanup(RenderTexture preview)
        {
            if (preview != null) UnityEngine.Object.Destroy(preview);
        }
    }
}
