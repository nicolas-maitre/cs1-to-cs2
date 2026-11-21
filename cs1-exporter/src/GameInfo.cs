using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework;
using ColossalFramework.Plugins;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>City/game metadata, enabled mods, detection of the mods the converter cares about, unlocked tiles.</summary>
    public static class GameInfo
    {
        public sealed class ModEntry
        {
            public string Name;      // assembly name (stable; what the converter matches on)
            public string Title;     // IUserMod.Name as shown in Content Manager
            public string Version;   // assembly version
            public ulong WorkshopId; // 0 = local mod
        }

        /// <summary>Converter-relevant mods, keyed by short id: imt, tmpe, nodeController, eightyOneTiles.</summary>
        public sealed class Known
        {
            public string Imt, Tmpe, NodeController, EightyOneTiles;
        }

        public static string CityName()
        {
            try { return Singleton<SimulationManager>.instance.m_metaData.m_CityName; } catch { return null; }
        }

        /// <summary>The save the city was loaded from: SavePanel.m_LastSaveName (private static), as the CS1 bridge reads it.</summary>
        public static string SaveName()
        {
            try
            {
                var f = typeof(SavePanel).GetField("m_LastSaveName", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                var n = f != null ? f.GetValue(null) as string : null;
                return string.IsNullOrEmpty(n) ? null : n;
            }
            catch { return null; }
        }

        public static string GameVersion()
        {
            try { return BuildConfig.applicationVersion; } catch { return null; }
        }

        public static bool CityLoaded()
        {
            try
            {
                var lm = Singleton<LoadingManager>.instance;
                if (lm == null || !lm.m_loadingComplete) return false;
                var mode = Singleton<ToolManager>.instance.m_properties.m_mode;
                return (mode & ItemClass.Availability.Game) != 0;
            }
            catch { return false; }
        }

        public static List<ModEntry> EnabledMods(Report report)
        {
            var list = new List<ModEntry>();
            foreach (var p in Singleton<PluginManager>.instance.GetPluginsInfo())
            {
                try
                {
                    if (p == null || !p.isEnabled) continue;
                    var e = new ModEntry();
                    object inst = null;
                    try { inst = p.userModInstance; } catch { }
                    var um = inst as ICities.IUserMod;
                    if (um != null)
                    {
                        try { e.Title = um.Name; } catch { }
                        var an = inst.GetType().Assembly.GetName();
                        e.Name = an.Name;
                        e.Version = an.Version.ToString();
                    }
                    if (e.Name == null) e.Name = p.name;
                    try
                    {
                        var id = p.publishedFileID;
                        if (id != ColossalFramework.PlatformServices.PublishedFileId.invalid) e.WorkshopId = id.AsUInt64;
                    }
                    catch { }
                    list.Add(e);
                }
                catch (Exception ex)
                {
                    report.Warn("mods", "could not read a plugin: " + Report.Describe(ex));
                }
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static Known Detect(List<ModEntry> mods)
        {
            var k = new Known();
            foreach (var m in mods)
            {
                string n = (m.Name ?? "") + " | " + (m.Title ?? "");
                string v = m.Version ?? "?";
                if (Has(n, "IntersectionMarkingTool") || Has(n, "Intersection Marking Tool") || Has(n, "NodeMarkup")) k.Imt = k.Imt ?? v;
                else if (Has(n, "TrafficManager") || Has(n, "TM:PE")) k.Tmpe = k.Tmpe ?? v;
                else if (Has(n, "NodeController") || Has(n, "Node Controller")) k.NodeController = k.NodeController ?? v;
                else if (Has(n, "EightyOne") || Has(n, "81 Tiles")) k.EightyOneTiles = k.EightyOneTiles ?? v;
            }
            return k;
        }

        private static bool Has(string s, string what) { return s.IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0; }

        /// <summary>
        /// The full map is a 9 × 9 grid of 1920 m tiles (81 Tiles; vanilla uses the centre 5 × 5). A tile counts as
        /// unlocked when GameAreaManager.PointOutOfArea is false at its centre. 81 Tiles 2 patches that method, so this
        /// works with and without the mod. Returns [col, row] pairs, col 0 = minimum x, row 0 = minimum z.
        /// </summary>
        public static List<int[]> UnlockedTiles()
        {
            var list = new List<int[]>();
            var gam = Singleton<GameAreaManager>.instance;
            const float tile = 1920f, half = 8640f;
            for (int row = 0; row < 9; row++)
                for (int col = 0; col < 9; col++)
                {
                    var c = new Vector3(-half + (col + 0.5f) * tile, 0, -half + (row + 0.5f) * tile);
                    if (!gam.PointOutOfArea(c)) list.Add(new[] { col, row });
                }
            return list;
        }
    }
}
