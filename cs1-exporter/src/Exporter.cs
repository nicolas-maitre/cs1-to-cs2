using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ColossalFramework.IO;

namespace Cs1Exporter
{
    /// <summary>
    /// Writes a .cs1x (schema/README.md §1). Every section renders into its own buffer inside a try/catch, so a
    /// failure becomes a report item plus a "failed" section and never aborts the export or crashes the game.
    ///
    /// Entry points:
    ///  - the Options → Mod Settings button (Mod.cs);
    ///  - Exporter.Run(args) for tools/gamectl:  gamectl cs1 exec Cs1Exporter.dll --type Cs1Exporter.Exporter --args '{"path":"C:\\...\\x.cs1x"}'
    /// </summary>
    public static class Exporter
    {
        public const string FormatName = "cs1x";
        public const int FormatVersion = 1;

        public static string DefaultOutputDir => Path.Combine(DataLocation.localApplicationData, "Cs1Exporter");

        public sealed class Result
        {
            public string Path;
            public long Bytes;
            public double Seconds;
            public int Nodes, Segments, NetInfos, ImtFillers = -1;
            public Dictionary<string, string> Sections = new Dictionary<string, string>();
            public Report Report;

            public string Summary()
            {
                var sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0} nodes, {1} segments, {2} NetInfos, {3} IMT fillers; {4:F1} MB in {5:F1} s",
                    Nodes, Segments, NetInfos, ImtFillers < 0 ? 0 : ImtFillers, Bytes / 1048576.0, Seconds);
                sb.Append("\nSections:");
                foreach (var kv in Sections) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
                sb.AppendFormat("\nReport: {0} errors, {1} warnings, {2} info", Report.Count("error"), Report.Count("warning"), Report.Count("info"));
                return sb.ToString();
            }
        }

        /// <summary>gamectl exec entry point. args: { path?: absolute .cs1x path }. Returns a JSON-serialisable summary.</summary>
        public static object Run(Dictionary<string, object> args)
        {
            object p = null;
            if (args != null) args.TryGetValue("path", out p);
            var path = p as string;
            if (string.IsNullOrEmpty(path)) path = DefaultPath();
            if (!GameInfo.CityLoaded()) throw new InvalidOperationException("no city is loaded");

            var r = Export(path);
            var report = new List<object>();
            foreach (var i in r.Report.Items)
                report.Add(new Dictionary<string, object> { { "level", i.Level }, { "section", i.Section }, { "message", i.Message } });
            var sections = new Dictionary<string, object>();
            foreach (var kv in r.Sections) sections[kv.Key] = kv.Value;
            return new Dictionary<string, object>
            {
                { "path", r.Path },
                { "bytes", r.Bytes },
                { "seconds", Math.Round(r.Seconds, 2) },
                { "nodes", r.Nodes },
                { "segments", r.Segments },
                { "netInfos", r.NetInfos },
                { "imtFillers", r.ImtFillers },
                { "sections", sections },
                { "report", report },
            };
        }

        public static string DefaultPath()
        {
            var city = GameInfo.CityName();
            if (string.IsNullOrEmpty(city)) city = "city";
            var safe = new StringBuilder();
            foreach (var ch in city) safe.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch);
            return Path.Combine(DefaultOutputDir, safe.ToString().Trim() + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".cs1x");
        }

        public static Result Export(string path)
        {
            var sw = Stopwatch.StartNew();
            var report = new Report();
            var result = new Result { Path = path, Report = report };
            var sections = result.Sections;
            var entries = new List<KeyValuePair<string, byte[]>>();

            // Order matters: network fills the "used NetInfos" set that netinfos.json needs.
            var net = new NetworkExporter(report);
            Section(report, sections, "network", () =>
            {
                entries.Add(Json("network.json", net.WriteNetwork));
                return "ok";
            });
            result.Nodes = net.NodeCount;
            result.Segments = net.SegmentCount;

            Section(report, sections, "netinfos", () =>
            {
                entries.Add(Json("netinfos.json", net.WriteNetInfos));
                return "ok";
            });
            result.NetInfos = net.NetInfoCount;

            Section(report, sections, "imt", () =>
            {
                int count = -1;
                var bytes = Json("imt.json", w => count = ImtReader.WriteFillers(w, report));
                result.ImtFillers = count;
                if (count < 0) return "absent";
                entries.Add(bytes);
                return "ok";
            });

            var terrain = new TerrainExporter(report);
            Section(report, sections, "terrain", () =>
            {
                terrain.Capture();
                try { terrain.VerifyAgainstNodes(); }
                catch (Exception e) { report.Warn("terrain", "node height check failed: " + Report.Describe(e)); }
                entries.Add(Json("terrain.json", terrain.WriteJson));
                entries.Add(new KeyValuePair<string, byte[]>("terrain.r16", terrain.R16));
                return "ok";
            });

            // M2: TM:PE and Node Controller data. Listed so readers see they were not exported on purpose.
            List<GameInfo.ModEntry> mods = null;
            GameInfo.Known known = null;
            try
            {
                mods = GameInfo.EnabledMods(report);
                known = GameInfo.Detect(mods);
            }
            catch (Exception e)
            {
                report.Error("mods", "mod detection failed: " + Report.Describe(e));
            }
            sections["tmpe"] = "absent";
            sections["nodecontroller"] = "absent";
            if (known != null)
            {
                if (known.Tmpe != null) report.Info("tmpe", "TM:PE " + known.Tmpe + " is enabled; its data (lane arrows, priority signs, ...) is exported from M2 on");
                if (known.NodeController != null) report.Info("nodecontroller", "Node Controller " + known.NodeController + " is enabled; its node data is exported from M2 on");
                if (known.EightyOneTiles != null) report.Info("manifest", "81 Tiles " + known.EightyOneTiles + " is enabled");
                if (known.Imt != null && result.ImtFillers < 0) report.Warn("imt", "IMT " + known.Imt + " is enabled but its fillers could not be read");
            }

            // manifest.json and report.json are always written; they never depend on the sections above succeeding.
            byte[] manifest;
            try
            {
                manifest = Json("manifest.json", w => WriteManifest(w, sections, mods, known, report)).Value;
            }
            catch (Exception e)
            {
                report.Error("manifest", "manifest failed, writing a minimal one: " + Report.Describe(e));
                manifest = Json("manifest.json", w => WriteManifest(w, sections, null, null, report)).Value;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (var zip = new ZipWriter(fs))
            {
                zip.Add("manifest.json", manifest);
                foreach (var e in entries) zip.Add(e.Key, e.Value);
                if (zip.DeflateUnavailable) report.Warn("zip", "deflate failed, entries are stored uncompressed: " + zip.DeflateError);
                result.Seconds = sw.Elapsed.TotalSeconds;
                report.Info("zip", string.Format(CultureInfo.InvariantCulture, "export took {0:F1} s", result.Seconds));
                zip.Add("report.json", Json("report.json", report.Write).Value);
                zip.Finish();
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            result.Bytes = new FileInfo(path).Length;
            UnityEngine.Debug.Log("[Cs1Exporter] wrote " + path + ": " + result.Summary());
            return result;
        }

        private static void Section(Report report, Dictionary<string, string> sections, string name, Func<string> body)
        {
            try
            {
                sections[name] = body();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                report.Error(name, "section failed: " + Report.Describe(e));
                sections[name] = "failed";
            }
        }

        private static KeyValuePair<string, byte[]> Json(string name, Action<JsonWriter> write)
        {
            var ms = new MemoryStream(1 << 16);
            var tw = new StreamWriter(ms, new UTF8Encoding(false), 1 << 16);
            var w = new JsonWriter(tw);
            write(w);
            w.Dispose(); // flushes
            return new KeyValuePair<string, byte[]>(name, ms.ToArray());
        }

        private static void WriteManifest(JsonWriter w, Dictionary<string, string> sections, List<GameInfo.ModEntry> mods, GameInfo.Known known, Report report)
        {
            w.BeginObject();
            w.Prop("format", FormatName);
            w.Prop("formatVersion", FormatVersion);
            w.Name("exporter").BeginObject();
            w.Prop("name", "Cs1Exporter");
            w.Prop("version", typeof(Exporter).Assembly.GetName().Version.ToString());
            w.EndObject();
            w.Prop("exportedAt", DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
            w.Name("city").BeginObject();
            w.Prop("name", GameInfo.CityName());
            w.Prop("saveName", GameInfo.SaveName());
            w.EndObject();
            w.Name("game").BeginObject();
            w.Prop("version", GameInfo.GameVersion());
            w.EndObject();

            w.Name("mods").BeginArray();
            if (mods != null)
                foreach (var m in mods)
                {
                    w.BeginObject();
                    w.Prop("name", m.Name);
                    w.Prop("version", m.Version);
                    w.Prop("title", m.Title);
                    if (m.WorkshopId != 0) w.Prop("workshopId", m.WorkshopId.ToString(CultureInfo.InvariantCulture));
                    w.EndObject();
                }
            w.EndArray();
            w.Name("detectedMods").BeginObject();
            if (known != null)
            {
                w.Prop("imt", known.Imt);
                w.Prop("tmpe", known.Tmpe);
                w.Prop("nodeController", known.NodeController);
                w.Prop("eightyOneTiles", known.EightyOneTiles);
            }
            w.EndObject();

            w.Prop("coordinateSystem", "CS1 world space, metres, Y up, origin at map centre");
            w.Prop("mapSize", 17280);
            try
            {
                var tiles = GameInfo.UnlockedTiles();
                w.Name("tiles").BeginObject();
                w.Prop("size", 1920);
                w.Prop("grid", 9);
                w.Prop("note", "unlocked [col,row] in a 9x9 grid of 1920 m tiles; col 0 = minimum x, row 0 = minimum z");
                w.Name("unlocked").BeginArray();
                foreach (var t in tiles) w.BeginArray().Value(t[0]).Value(t[1]).EndArray();
                w.EndArray();
                w.EndObject();
            }
            catch (Exception e)
            {
                report.Warn("manifest", "unlocked tiles not exported: " + Report.Describe(e));
            }

            w.Name("sections").BeginObject();
            foreach (var kv in sections) w.Prop(kv.Key, kv.Value);
            w.EndObject();
            w.EndObject();
        }
    }
}
