using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ColossalFramework;
using ColossalFramework.IO;

namespace Cs1Exporter
{
    public static class ModInfo
    {
        /// <summary>Release version shown in the mod name and written to manifest.exporter.version.</summary>
        public const string Version = "1.2.1";
        public const string Name = "CS1 → CS2 Exporter";
    }

    /// <summary>What to export. Core sections (network, netinfos, imt, terrain) are always written.</summary>
    public sealed class ExportOptions
    {
        public static readonly string[] Optional =
            { "zones", "water", "districts", "transit", "tmpe", "nodecontroller", "buildings", "assets", "props", "trees" };

        public readonly HashSet<string> Excluded = new HashSet<string>();
        /// <summary>Test hook: treat these mods as not installed (imt, tmpe, nodecontroller, eightyOneTiles).</summary>
        public readonly HashSet<string> SimulateMissing = new HashSet<string>();
        /// <summary>Test hook: make these sections throw, to exercise the failure path.</summary>
        public readonly HashSet<string> SimulateFailure = new HashSet<string>();
        /// <summary>Pause the simulation while exporting (UI path); the exec path runs in a single frame and doesn't.</summary>
        public bool PauseSimulation;

        public bool Includes(string section) { return !Excluded.Contains(section); }

        public static ExportOptions FromArgs(Dictionary<string, object> args)
        {
            var o = new ExportOptions();
            if (args == null) return o;
            Fill(o.Excluded, args, "exclude");
            Fill(o.SimulateMissing, args, "simulateMissing");
            Fill(o.SimulateFailure, args, "simulateFailure");
            return o;
        }

        private static void Fill(HashSet<string> set, Dictionary<string, object> args, string key)
        {
            object v;
            if (!args.TryGetValue(key, out v) || v == null) return;
            var list = v as IList;
            if (list != null) { foreach (var x in list) if (x != null) set.Add(x.ToString()); }
            else foreach (var x in v.ToString().Split(',')) if (x.Trim().Length > 0) set.Add(x.Trim());
        }
    }

    /// <summary>Progress of a running export, for the UI.</summary>
    public sealed class ExportProgress
    {
        public int Step, Total;
        public string Label = "";
        public Exporter.Result Result;
    }

    /// <summary>
    /// Writes a .cs1x (schema/README.md §1).
    ///  - Every section streams its JSON straight into its zip entry (no whole-file strings in memory) inside a
    ///    try/catch: a failure drops the half-written entry, becomes a report item and a "failed" section, and never
    ///    aborts the export or crashes the game.
    ///  - ExportRoutine yields between sections so the UI can show progress; Export drains it in one go.
    ///
    /// Entry points: the Options panel (Mod.cs) and Exporter.Run(args) for tools/gamectl:
    ///   gamectl cs1 exec Cs1Exporter.dll --type Cs1Exporter.Exporter --args '{"path":"%LOCALAPPDATA%/…/x.cs1x"}'
    /// Optional args: exclude, simulateMissing, simulateFailure (arrays of names).
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
            public Dictionary<string, int> Counts = new Dictionary<string, int>();
            public Report Report;

            public string Summary()
            {
                var sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "{0} nodes, {1} segments, {2} NetInfos, {3} IMT fillers; {4:F1} MB in {5:F1} s",
                    Nodes, Segments, NetInfos, ImtFillers < 0 ? 0 : ImtFillers, Bytes / 1048576.0, Seconds);
                foreach (var kv in Counts) sb.Append(", ").Append(kv.Key).Append(' ').Append(kv.Value);
                sb.Append("\nSections:");
                foreach (var kv in Sections) sb.Append(' ').Append(kv.Key).Append('=').Append(kv.Value);
                sb.AppendFormat("\nReport: {0} errors, {1} warnings, {2} info", Report.Count("error"), Report.Count("warning"), Report.Count("info"));
                return sb.ToString();
            }

            public int Failed
            {
                get { int n = 0; foreach (var v in Sections.Values) if (v == "failed") n++; return n; }
            }
        }

        /// <summary>gamectl exec entry point. args: { path?, exclude?, simulateMissing?, simulateFailure? }.</summary>
        public static object Run(Dictionary<string, object> args)
        {
            object p = null;
            if (args != null) args.TryGetValue("path", out p);
            var path = p as string;
            if (string.IsNullOrEmpty(path)) path = DefaultPath(DefaultOutputDir);
            else path = Environment.ExpandEnvironmentVariables(path); // %LOCALAPPDATA%/... works from any shell
            if (!GameInfo.CityLoaded()) throw new InvalidOperationException("no city is loaded");

            var r = Export(path, ExportOptions.FromArgs(args));
            var report = new List<object>();
            foreach (var i in r.Report.Items)
                report.Add(new Dictionary<string, object> { { "level", i.Level }, { "section", i.Section }, { "message", i.Message } });
            var sections = new Dictionary<string, object>();
            foreach (var kv in r.Sections) sections[kv.Key] = kv.Value;
            var counts = new Dictionary<string, object>();
            foreach (var kv in r.Counts) counts[kv.Key] = kv.Value;
            return new Dictionary<string, object>
            {
                { "path", r.Path },
                { "bytes", r.Bytes },
                { "seconds", Math.Round(r.Seconds, 2) },
                { "nodes", r.Nodes },
                { "segments", r.Segments },
                { "netInfos", r.NetInfos },
                { "imtFillers", r.ImtFillers },
                { "counts", counts },
                { "sections", sections },
                { "report", report },
            };
        }

        public static string DefaultPath(string dir)
        {
            var city = GameInfo.CityName();
            if (string.IsNullOrEmpty(city)) city = "city";
            var safe = new StringBuilder();
            foreach (var ch in city) safe.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0 ? '_' : ch);
            return Path.Combine(dir, safe.ToString().Trim() + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".cs1x");
        }

        /// <summary>Runs the whole export in one call (exec path).</summary>
        public static Result Export(string path, ExportOptions options = null)
        {
            var progress = new ExportProgress();
            var routine = ExportRoutine(path, options ?? new ExportOptions(), progress);
            while (routine.MoveNext()) { }
            return progress.Result;
        }

        /// <summary>
        /// The export as steps; the caller advances it once per frame (UI) or in a loop (exec). Throws only for
        /// problems that make an export impossible (no city, unwritable folder); section problems are reported.
        /// </summary>
        public static IEnumerator ExportRoutine(string path, ExportOptions opt, ExportProgress progress)
        {
            var ctx = new Ctx(path, opt, progress);
            bool resumeSimulation = false;
            try
            {
                if (opt.PauseSimulation && !Singleton<SimulationManager>.instance.SimulationPaused)
                {
                    // Reading while the simulation thread writes could see half-updated objects; pause it and give it
                    // two frames to finish its current step. The previous state is restored at the end.
                    Singleton<SimulationManager>.instance.SimulationPaused = true;
                    resumeSimulation = true;
                    progress.Label = "pausing the simulation";
                    yield return null;
                    yield return null;
                }
                ctx.Open();
                foreach (var step in ctx.Steps())
                {
                    progress.Step++;
                    progress.Label = step;
                    yield return null;
                }
                ctx.Close();
                progress.Result = ctx.Result;
            }
            finally
            {
                ctx.Abort();
                if (resumeSimulation) Singleton<SimulationManager>.instance.SimulationPaused = false;
            }
        }

        // ------------------------------------------------------------------ one export

        private sealed class Ctx
        {
            private readonly string _path, _tmp;
            private readonly ExportOptions _opt;
            private readonly ExportProgress _progress;
            private readonly Stopwatch _sw = Stopwatch.StartNew();
            private readonly Report _report = new Report();
            public readonly Result Result;
            private FileStream _fs;
            private ZipWriter _zip;
            private bool _closed;
            private GameInfo.Known _known;
            private List<GameInfo.ModEntry> _mods;

            public Ctx(string path, ExportOptions opt, ExportProgress progress)
            {
                _path = Path.GetFullPath(path);
                _tmp = _path + ".tmp";
                _opt = opt;
                _progress = progress;
                Result = new Result { Path = _path, Report = _report };
            }

            public void Open()
            {
                if (!GameInfo.CityLoaded()) throw new InvalidOperationException("No city is loaded. Load a city, then export.");
                var dir = Path.GetDirectoryName(_path);
                try { Directory.CreateDirectory(dir); }
                catch (Exception e) { throw new IOException("Can't create the export folder " + dir + ": " + e.Message, e); }
                try { _fs = new FileStream(_tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 16); }
                catch (Exception e) { throw new IOException("Can't write to " + dir + ": " + e.Message, e); }
                _zip = new ZipWriter(_fs);
                if (!_zip.UseDeflate) _report.Warn("zip", "deflate unavailable, entries are stored uncompressed: " + _zip.DeflateError);
                Refl.HiddenNamespaces.Clear();
                foreach (var s in _opt.SimulateMissing)
                {
                    _report.Warn("test", "simulating that mod '" + s + "' is not installed");
                    if (s == "imt") Refl.HiddenNamespaces.Add("IMT.");
                    if (s == "tmpe") { Refl.HiddenNamespaces.Add("TrafficManager."); Refl.HiddenNamespaces.Add("TMPE."); }
                    if (s == "nodecontroller") Refl.HiddenNamespaces.Add("NodeController.");
                }
                foreach (var s in _opt.SimulateFailure) _report.Warn("test", "simulating a failure in section '" + s + "'");
                try
                {
                    _mods = GameInfo.EnabledMods(_report);
                    _known = GameInfo.Detect(_mods);
                    if (_opt.SimulateMissing.Contains("imt")) _known.Imt = null;
                    if (_opt.SimulateMissing.Contains("tmpe")) _known.Tmpe = null;
                    if (_opt.SimulateMissing.Contains("nodecontroller")) _known.NodeController = null;
                    if (_opt.SimulateMissing.Contains("eightyOneTiles")) _known.EightyOneTiles = null;
                }
                catch (Exception e) { _report.Error("mods", "mod detection failed: " + Report.Describe(e)); }
                _progress.Total = 15;
            }

            /// <summary>Each yielded string is the label of the section just written.</summary>
            public IEnumerable<string> Steps()
            {
                var net = new NetworkExporter(_report);
                Json("network", "network.json", w => { net.WriteNetwork(w); return "ok"; });
                Result.Nodes = net.NodeCount;
                Result.Segments = net.SegmentCount;
                yield return "road network";

                Json("netinfos", "netinfos.json", w => { net.WriteNetInfos(w); return "ok"; });
                Result.NetInfos = net.NetInfoCount;
                yield return "network types";

                Json("imt", "imt.json", w =>
                {
                    int n = ImtReader.WriteFillers(w, _report);
                    Result.ImtFillers = n;
                    return n < 0 ? "absent" : "ok";
                });
                yield return "IMT markings";

                var terrain = new TerrainExporter(_report);
                Multi("terrain", () =>
                {
                    terrain.Capture();
                    try { terrain.VerifyAgainstNodes(); }
                    catch (Exception e) { _report.Warn("terrain", "node height check failed: " + Report.Describe(e)); }
                    WriteJson("terrain.json", terrain.WriteJson);
                    _zip.Add("terrain.r16", terrain.R16);
                    terrain.R16Release();
                });
                yield return "terrain";

                var zones = new ZonesExporter(_report);
                Json("zones", "zones.json", w => { zones.Write(w); Count("zoneBlocks", zones.Blocks); Count("zonedCells", zones.ZonedCells); return "ok"; });
                yield return "zoning";

                var water = new WaterExporter(_report);
                Multi("water", () =>
                {
                    WriteJson("water.json", w => water.Write(w, terrain.Resolution > 0 ? terrain : null));
                    _zip.Add("water.r16", water.R16);
                    water.R16 = null;
                    Count("waterSources", water.Sources);
                });
                yield return "water";

                var districts = new DistrictsExporter(_report);
                Multi("districts", () =>
                {
                    districts.Capture();
                    WriteJson("districts.json", districts.WriteJson);
                    _zip.Add("districts.u8", districts.DistrictRaster);
                    _zip.Add("parks.u8", districts.ParkRaster);
                    Count("districts", districts.Districts);
                    Count("parkAreas", districts.Parks);
                });
                yield return "districts";

                LaneMap lanes = null;
                Json("transit", "transit.json", w =>
                {
                    lanes = lanes ?? new LaneMap();
                    var t = new TransitExporter(_report);
                    t.Write(w, lanes);
                    Count("transitLines", t.Lines);
                    Count("transitStops", t.Stops);
                    return "ok";
                });
                yield return "transit lines";

                var tmpe = new TmpeExporter(_report);
                Json("tmpe", "tmpe.json", w =>
                {
                    if (!tmpe.Available()) { _report.Info("tmpe", "TM:PE is not loaded"); return "absent"; }
                    tmpe.Write(w);
                    foreach (var kv in tmpe.Counts) Count("tmpe." + kv.Key, kv.Value);
                    return "ok";
                });
                yield return "TM:PE";

                var nc = new NodeControllerExporter(_report);
                Json("nodecontroller", "nodecontroller.json", w =>
                {
                    if (!nc.Available()) { _report.Info("nodecontroller", "Node Controller is not loaded"); return "absent"; }
                    nc.Write(w);
                    Count("ncNodes", nc.Nodes);
                    return "ok";
                });
                yield return "Node Controller";

                var bx = new BuildingsExporter(_report);
                Json("buildings", "buildings.json", w => { bx.WriteBuildings(w); Count("buildings", bx.Buildings); return "ok"; });
                yield return "buildings";

                Json("props", "props.json", w => { bx.WriteProps(w); Count("props", bx.Props); return "ok"; });
                Json("trees", "trees.json", w => { bx.WriteTrees(w); Count("trees", bx.Trees); return "ok"; });
                yield return "props and trees";

                var assets = new AssetsExporter(_report);
                Json("assets", "assets.json", w => { assets.Write(w); Count("assetTypes", assets.BuildingTypes + assets.PropTypes + assets.TreeTypes + assets.NetworkTypes); return "ok"; });
                yield return "asset catalogue";

                if (_known != null)
                {
                    if (_known.EightyOneTiles != null) _report.Info("manifest", "81 Tiles " + _known.EightyOneTiles + " is enabled");
                    if (_known.Imt != null && Result.ImtFillers < 0) _report.Warn("imt", "IMT " + _known.Imt + " is enabled but its fillers could not be read");
                    if (_known.Tmpe != null && Result.Sections["tmpe"] == "absent" && _opt.Includes("tmpe")) _report.Warn("tmpe", "TM:PE " + _known.Tmpe + " is enabled but its managers were not found");
                    if (_known.NodeController != null && Result.Sections["nodecontroller"] == "absent" && _opt.Includes("nodecontroller")) _report.Warn("nodecontroller", "Node Controller " + _known.NodeController + " is enabled but its manager was not found");
                }
                yield return "finishing";
            }

            private void Count(string key, int n) { Result.Counts[key] = n; }

            private bool Skip(string section)
            {
                if (Array.IndexOf(ExportOptions.Optional, section) >= 0 && !_opt.Includes(section))
                {
                    Result.Sections[section] = "absent";
                    _report.Info(section, "not exported (turned off in the options)");
                    return true;
                }
                return false;
            }

            private void Fail(string section, Exception e)
            {
                UnityEngine.Debug.LogException(e);
                _report.Error(section, "section failed: " + Report.Describe(e));
                Result.Sections[section] = "failed";
            }

            /// <summary>A one-entry JSON section, streamed into the zip. The body returns "ok" or "absent".</summary>
            private void Json(string section, string file, Func<JsonWriter, string> body)
            {
                if (Skip(section)) return;
                bool open = false;
                try
                {
                    if (_opt.SimulateFailure.Contains(section)) throw new InvalidOperationException("simulated failure (test)");
                    var s = _zip.BeginEntry(file);
                    open = true;
                    var tw = new StreamWriter(s, new UTF8Encoding(false), 1 << 16);
                    var w = new JsonWriter(tw);
                    string status = body(w);
                    w.Dispose(); // flush
                    _zip.EndEntry(status == "ok");
                    open = false;
                    Result.Sections[section] = status;
                }
                catch (Exception e)
                {
                    if (open) { try { _zip.EndEntry(false); } catch { } }
                    Fail(section, e);
                }
            }

            /// <summary>A section with several entries; entries written before a failure stay (status "failed").</summary>
            private void Multi(string section, Action body)
            {
                if (Skip(section)) return;
                try
                {
                    if (_opt.SimulateFailure.Contains(section)) throw new InvalidOperationException("simulated failure (test)");
                    body();
                    Result.Sections[section] = "ok";
                }
                catch (Exception e)
                {
                    try { _zip.EndEntry(false); } catch { }
                    Fail(section, e);
                }
            }

            private void WriteJson(string file, Action<JsonWriter> write)
            {
                var s = _zip.BeginEntry(file);
                var tw = new StreamWriter(s, new UTF8Encoding(false), 1 << 16);
                var w = new JsonWriter(tw);
                write(w);
                w.Dispose();
                _zip.EndEntry(true);
            }

            /// <summary>manifest.json and report.json are always written; then the temp file replaces the target.</summary>
            public void Close()
            {
                try
                {
                    WriteJson("manifest.json", w => WriteManifest(w, Result.Sections, _mods, _known, _report, _opt));
                }
                catch (Exception e)
                {
                    try { _zip.EndEntry(false); } catch { }
                    _report.Error("manifest", "manifest failed, writing a minimal one: " + Report.Describe(e));
                    WriteJson("manifest.json", w => WriteManifest(w, Result.Sections, null, null, _report, _opt));
                }
                Result.Seconds = _sw.Elapsed.TotalSeconds;
                _report.Info("zip", string.Format(CultureInfo.InvariantCulture, "export took {0:F1} s", Result.Seconds));
                WriteJson("report.json", _report.Write);
                _zip.Finish();
                _fs.Close();
                _fs = null;
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(_tmp, _path);
                _closed = true;
                Result.Bytes = new FileInfo(_path).Length;
                UnityEngine.Debug.Log("[Cs1Exporter] wrote " + _path + ": " + Result.Summary());
            }

            /// <summary>Cleans up after a fatal error or a cancelled routine: no half-written .tmp is left behind.</summary>
            public void Abort()
            {
                Refl.HiddenNamespaces.Clear();
                if (_closed) return;
                try { if (_fs != null) _fs.Close(); } catch { }
                _fs = null;
                try { if (File.Exists(_tmp)) File.Delete(_tmp); } catch { }
            }
        }

        private static void WriteManifest(JsonWriter w, Dictionary<string, string> sections, List<GameInfo.ModEntry> mods,
            GameInfo.Known known, Report report, ExportOptions opt)
        {
            w.BeginObject();
            w.Prop("format", FormatName);
            w.Prop("formatVersion", FormatVersion);
            w.Name("exporter").BeginObject();
            w.Prop("name", "Cs1Exporter");
            w.Prop("version", ModInfo.Version);
            w.Prop("build", typeof(Exporter).Assembly.GetName().Version.ToString());
            w.EndObject();
            w.Prop("exportedAt", DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture));
            w.Name("city").BeginObject();
            w.Prop("name", GameInfo.CityName());
            w.Prop("saveName", GameInfo.SaveName());
            w.EndObject();
            // Traffic handedness: a left-hand-traffic city (CS1 "invert traffic") has its lanes mirrored relative to the
            // segment direction; the importer needs it to map lane indices (QA, CacaoCity: 0 of 265 lane connections).
            w.Name("traffic").BeginObject();
            bool leftHand = false;
            try { leftHand = Singleton<SimulationManager>.instance.m_metaData.m_invertTraffic == SimulationMetaData.MetaBool.True; } catch { }
            w.Prop("leftHand", leftHand);
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

            w.Name("excludedSections").BeginArray();
            foreach (var s in ExportOptions.Optional) if (!opt.Includes(s)) w.Value(s);
            w.EndArray();
            if (opt.SimulateMissing.Count > 0 || opt.SimulateFailure.Count > 0)
            {
                w.Name("test").BeginObject();
                w.Name("simulateMissing").BeginArray();
                foreach (var s in opt.SimulateMissing) w.Value(s);
                w.EndArray();
                w.Name("simulateFailure").BeginArray();
                foreach (var s in opt.SimulateFailure) w.Value(s);
                w.EndArray();
                w.EndObject();
            }

            w.Name("sections").BeginObject();
            foreach (var kv in sections) w.Prop(kv.Key, kv.Value);
            w.EndObject();
            w.EndObject();
        }
    }
}
