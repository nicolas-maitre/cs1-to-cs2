// What the Options page buttons do. Kept apart from Setting so scripts and the UI share one code path. Every text the
// player sees goes through T.S (Strings.cs: English and French).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Cs2Importer;
using Game;
using Game.SceneFlow;
using GameBridge;

namespace Cs2ImporterMod
{
    public static class Actions
    {
        public static string LocalRoot { get { return Packages.LocalRoot; } }

        /// <summary>Test hook (scripts, op "mod" setStatic): stands in for Road Builder's configs folder.</summary>
        public static string TestRoadBuilderFolder;

        public static string RoadBuilderFolder
        {
            get { return TestRoadBuilderFolder ?? Path.Combine(UnityEngine.Application.persistentDataPath, "ModsData", "RoadBuilder"); }
        }

        /// <summary>Is a mod's assembly loaded (Road Builder: "RoadBuilder", Traffic: "Traffic")?</summary>
        public static bool ModLoaded(string assembly)
        {
            try { return AppDomain.CurrentDomain.GetAssemblies().Any(a => string.Equals(a.GetName().Name, assembly, StringComparison.OrdinalIgnoreCase)); }
            catch { return false; }
        }

        public static List<string> FindPackages() { return Packages.Found(); }
        public static string Describe(string dir) { return Packages.Describe(dir); }

        // ---------------------------------------------------------------- package

        /// <summary>
        /// The plan file of the chosen package: resolves a pasted folder / plan / zip (a zip is unpacked and the folder
        /// replaces it in the setting), checks the plan's format, and remembers the package as recent.
        /// </summary>
        static string PlanFile(Setting s)
        {
            string unpacked;
            var plan = Packages.Resolve(s.PackageFolder, out unpacked);
            var dir = Path.GetDirectoryName(plan);
            if (unpacked != null || !string.Equals((s.PackageFolder ?? "").Trim().Trim('"'), dir, StringComparison.OrdinalIgnoreCase)) s.PackageFolder = dir;
            if (unpacked != null) s.LastUnpacked = T.S("pick.unpacked", Path.GetFileName(unpacked), dir);
            CheckFormat(plan);
            Packages.Remember(s, dir);
            return plan;
        }

        /// <summary>Format and version, read before anything else so the message says what to do.</summary>
        static void CheckFormat(string planFile)
        {
            Dictionary<string, object> root;
            try { root = MiniJson.Parse(File.ReadAllText(planFile)) as Dictionary<string, object>; }
            catch (Exception e) { throw new InvalidOperationException(T.S("err.damaged", e.Message)); }
            if (root == null) throw new InvalidOperationException(T.S("err.damaged", "not a JSON object"));
            var format = P.Str(root, "format", null);
            if (format != Cs2Importer.Core.PlanParser.Format) throw new InvalidOperationException(T.S("err.notPackage", "format \"" + format + "\""));
            int v = (int)P.Num(root, "formatVersion", 0), mine = Cs2Importer.Core.PlanParser.Version;
            if (v > mine) throw new InvalidOperationException(T.S("err.newerFormat", v, mine));
            if (v < mine) throw new InvalidOperationException(T.S("err.olderFormat", v, mine));
        }

        static Dictionary<string, object> Args(Setting s, string op)
        {
            return new Dictionary<string, object>
            {
                { "op", op }, { "plan", PlanFile(s) }, { "offsetX", (double)s.OffsetX }, { "offsetZ", (double)s.OffsetZ },
                { "heights", "flat" }, { "clear", s.ClearArea },
            };
        }

        /// <summary>Windows' file dialog for a plan file or a zip; then Check.</summary>
        public static Dictionary<string, object> Browse(Setting s)
        {
            string file;
            try { file = Packages.BrowseFile(Packages.InitialDir(s)); }
            catch (Exception e)
            {
                Mod.Instance.SetStatus(T.S("pick.unavailable", e.Message));
                return new Dictionary<string, object> { { "error", e.Message } };
            }
            if (file == null)
            {
                Mod.Instance.SetStatus(T.S("pick.none"));
                return new Dictionary<string, object> { { "chosen", null } };
            }
            s.PackageFolder = file;
            var r = Check(s);
            r["chosen"] = file;
            return r;
        }

        public static Dictionary<string, object> OpenPackagesFolder(Setting s)
        {
            try
            {
                Directory.CreateDirectory(Packages.PackagesRoot);
                System.Diagnostics.Process.Start("explorer.exe", "\"" + Packages.PackagesRoot + "\"");
                return new Dictionary<string, object> { { "opened", Packages.PackagesRoot } };
            }
            catch (Exception e) { return new Dictionary<string, object> { { "error", e.Message } }; }
        }

        // ---------------------------------------------------------------- check

        /// <summary>Plan check plus the Road Builder state, the package's map data and whether this city was prepared.</summary>
        public static Dictionary<string, object> Check(Setting s)
        {
            try
            {
                s.LastUnpacked = null;
                var planFile = PlanFile(s);
                var r = (Dictionary<string, object>)Entry.Run(Args(s, "check"));
                var rb = RoadBuilderState(s, r);
                r["roadbuilder"] = rb;
                var map = MapContents(planFile);
                r["map"] = map;
                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(s.LastUnpacked)) sb.Append(s.LastUnpacked).Append('\n');
                sb.Append(CheckText(r, rb)).Append('\n').Append(MapText(map));
                var lht = MapSteps.PlanLeftHand((Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(planFile)));
                if (lht.HasValue && InCity() && lht.Value != MapSteps.CityLeftHand()) sb.Append('\n').Append(T.S(lht.Value ? "check.sideLeft" : "check.sideRight"));
                if ((bool)map["terrain"] && InCity())
                {
                    var diff = MapSteps.TerrainMismatch(planFile);
                    r["terrain_mismatch_m"] = diff;
                    if (diff.HasValue) sb.Append('\n').Append(diff.Value <= MapSteps.PreparedTolerance ? T.S("check.cityPrepared") : T.S("check.cityNotPrepared", Math.Round(diff.Value, 1)));
                }
                Mod.Instance.SetStatus(sb.ToString());
                return r;
            }
            catch (Exception e)
            {
                Mod.Instance.SetStatus(T.S("check.failed", e.Message));
                return new Dictionary<string, object> { { "error", e.Message } };
            }
        }

        static bool InCity()
        {
            var gm = GameManager.instance;
            return gm != null && !gm.isGameLoading && gm.gameMode.IsGame();
        }

        /// <summary>What the package brings for "Prepare map" and the connections step (schema §2).</summary>
        static Dictionary<string, object> MapContents(string planFile)
        {
            var plan = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(planFile));
            var terrain = P.Obj(plan, "terrain");
            var water = P.Obj(plan, "water");
            var apply = P.Obj(water, "apply");
            var oc = P.Obj(plan, "outsideConnections");
            var conns = P.List(oc, "connections");
            var srcs = P.List(apply, "sources");
            bool terrainFiles = false;
            if (terrain != null)
            {
                var meta = Path.Combine(Path.GetDirectoryName(planFile), P.Str(terrain, "meta", "terrain/terrain.json").Replace('/', Path.DirectorySeparatorChar));
                terrainFiles = File.Exists(meta);
            }
            return new Dictionary<string, object>
            {
                { "terrain", terrainFiles }, { "world", terrain != null && P.Bool(terrain, "world", false) },
                { "water", apply != null }, { "seaLevel", apply != null ? P.Num(apply, "seaLevel", 0) : terrain != null ? P.Num(terrain, "seaLevelDry", 0) : (double?)null },
                { "waterSources", srcs != null ? srcs.Count : 0 }, { "outsideConnections", conns != null ? conns.Count : 0 },
            };
        }

        static string MapText(Dictionary<string, object> m)
        {
            var sb = new StringBuilder();
            if (!(bool)m["terrain"]) sb.Append(T.S("check.mapNone"));
            else
            {
                sb.Append(T.S("check.mapTerrain")).Append((bool)m["world"] ? T.S("check.mapWorld") : "");
                if ((bool)m["water"]) sb.Append(T.S("check.mapWater", Math.Round(Convert.ToDouble(m["seaLevel"]), 1), m["waterSources"]));
                else sb.Append(T.S("check.mapDry"));
            }
            sb.Append(T.S("check.mapConnections", m["outsideConnections"]));
            if ((bool)m["terrain"]) sb.Append(T.S("check.mapPrepare"));
            return sb.ToString();
        }

        static Dictionary<string, object> RoadBuilderState(Setting s, Dictionary<string, object> check)
        {
            var pkg = Path.GetDirectoryName(PlanFile(s));
            int missingFile = 0, notLoaded = 0, packaged = 0, needed = 0;
            var pkgRb = Path.Combine(pkg, "roadbuilder");
            if (Directory.Exists(pkgRb)) packaged = Directory.GetFiles(pkgRb, "*-cs1conv.json").Length;
            foreach (var o in (List<object>)check["prefabs"])
            {
                var p = (Dictionary<string, object>)o;
                if ((string)p["type"] != "RoadBuilderPrefab") continue;
                needed++;
                if ((bool)p["found"]) continue;
                if (File.Exists(Path.Combine(RoadBuilderFolder, (string)p["name"] + ".json"))) notLoaded++;
                else missingFile++;
            }
            return new Dictionary<string, object>
            {
                { "folder", RoadBuilderFolder }, { "folder_exists", Directory.Exists(RoadBuilderFolder) }, { "mod_loaded", ModLoaded("RoadBuilder") },
                { "types_needed", needed }, { "configs_in_package", packaged }, { "missing_files", missingFile }, { "copied_but_not_loaded", notLoaded },
            };
        }

        static string CheckText(Dictionary<string, object> r, Dictionary<string, object> rb)
        {
            var plan = (Dictionary<string, object>)r["plan"];
            var sb = new StringBuilder();
            sb.Append(T.S("check.package", plan["source"], plan["segments_to_build"]));
            if (Convert.ToInt32(plan["segments_skipped"]) > 0) sb.Append(T.S("check.skipped", plan["segments_skipped"]));
            sb.Append(".\n");
            int missing = Convert.ToInt32(r["prefabs_missing"]);
            int missingFiles = Convert.ToInt32(rb["missing_files"]), notLoaded = Convert.ToInt32(rb["copied_but_not_loaded"]);
            bool rbNeeded = Convert.ToInt32(rb["types_needed"]) > 0;
            if (rbNeeded && !(bool)rb["mod_loaded"]) sb.Append(T.S("err.rbNotLoaded")).Append('\n');
            if (missing == 0) sb.Append(T.S("check.allTypes", ((List<object>)r["prefabs"]).Count)).Append('\n');
            else
            {
                sb.Append(T.S("check.missing", missing)).Append(' ');
                if (missingFiles > 0 && !(bool)rb["folder_exists"] && (bool)rb["mod_loaded"]) sb.Append(T.S("err.rbFolderMissing", RoadBuilderFolder)).Append(' ');
                else if (missingFiles > 0 && (bool)rb["mod_loaded"]) sb.Append(T.S("check.rbMissingFiles", missingFiles)).Append(' ');
                if (notLoaded > 0 && (bool)rb["mod_loaded"]) sb.Append(T.S("check.rbNotLoaded", notLoaded)).Append(' ');
                if (missing > missingFiles + notLoaded) sb.Append(T.S("check.otherMissing")).Append(' ');
                sb.Append(T.S("check.missingSkipped")).Append('\n');
            }
            int existing = r.ContainsKey("existing_edges_in_area") ? Convert.ToInt32(r["existing_edges_in_area"]) : 0;
            if (existing > 0) sb.Append(T.S("check.existing", existing)).Append('\n');
            var b = (Dictionary<string, object>)r["bounds"];
            if (!(bool)b["inside_cs2_map"]) sb.Append(T.S("check.outside")).Append('\n');
            return sb.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- Road Builder configs

        public static Dictionary<string, object> InstallRoadBuilderConfigs(Setting s)
        {
            try
            {
                var src = Path.Combine(Path.GetDirectoryName(PlanFile(s)), "roadbuilder");
                if (!Directory.Exists(src)) throw new InvalidOperationException(T.S("rb.noFolder"));
                Directory.CreateDirectory(RoadBuilderFolder);
                int n = 0;
                foreach (var f in Directory.GetFiles(src, "*-cs1conv.json")) // only ever our own generated configs
                {
                    File.Copy(f, Path.Combine(RoadBuilderFolder, Path.GetFileName(f)), true);
                    n++;
                }
                Mod.Instance.SetStatus(T.S("rb.copied", n) + (ModLoaded("RoadBuilder") ? "" : "\n" + T.S("err.rbNotLoaded")));
                return new Dictionary<string, object> { { "copied", n }, { "to", RoadBuilderFolder } };
            }
            catch (Exception e)
            {
                Mod.Instance.SetStatus(T.S("rb.failed", e.Message));
                return new Dictionary<string, object> { { "error", e.Message } };
            }
        }

        // ---------------------------------------------------------------- prepare map

        public static bool PrepareMap(Setting s)
        {
            try
            {
                if (!InCity()) throw new InvalidOperationException(T.S("err.noNewCity"));
                if (ImportSystem.Busy) throw new InvalidOperationException(T.S("err.busy"));
                var plan = PlanFile(s);
                var report = Path.Combine(LocalRoot, "import-reports", "prepare-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                Mod.Instance.SetStatus(T.S("prep.running"));
                ImportSystem.Start(MapSteps.PrepareMap(plan, k => Mod.Instance.SetStatus(T.S("prep.progress", T.S(k))), () => ImportSystem.CancelRequested), (result, error) =>
                {
                    if (error != null) { Mod.Instance.SetStatus(T.S("prep.failed", error.Message)); return; }
                    var r = result as Dictionary<string, object>;
                    WriteReport(s, report, r);
                    object at;
                    if (r != null && r.TryGetValue("cancelled", out at)) { Mod.Instance.SetStatus(T.S("prep.cancelled", T.S((string)at))); return; }
                    Mod.Instance.SetStatus(PrepareText(r, report));
                });
                return true;
            }
            catch (Exception e)
            {
                Mod.Instance.SetStatus(T.S("prep.notStarted", e.Message));
                return false;
            }
        }

        static void WriteReport(Setting s, string report, object r)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report));
                File.WriteAllText(report, MiniJson.Write(r), new UTF8Encoding(false));
                s.LastReportPath = report;
            }
            catch (Exception ex) { Mod.Log.Warn("report not written: " + ex.Message); }
        }

        /// <summary>Not a button: scripts (op "mod", press "WaterCheck") read CS2's water at the package's CS1 checkpoints,
        /// e.g. after letting the water settle. The same check "Prepare map" reports right away.</summary>
        public static Dictionary<string, object> WaterCheck(Setting s)
        {
            try { return MapSteps.WaterCheck((Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(PlanFile(s)))); }
            catch (Exception e) { return new Dictionary<string, object> { { "error", e.Message } }; }
        }

        static string PrepareText(Dictionary<string, object> r, string report)
        {
            if (r == null) return T.S("prep.noReport");
            var sb = new StringBuilder(T.S("prep.done")).Append('\n');
            object v;
            var c = r.TryGetValue("clear", out v) ? v as Dictionary<string, object> : null;
            if (c != null) sb.Append(T.S("prep.removed", c.TryGetValue("edgesDeleted", out v) ? v : 0, c.TryGetValue("buildingsDeleted", out v) ? v : 0)).Append('\n');
            var tr = r.TryGetValue("traffic", out v) ? v as Dictionary<string, object> : null;
            if (tr != null) sb.Append(T.S((bool)tr["leftHand"] ? "prep.lht" : "prep.rht")).Append('\n');
            var t = r.TryGetValue("terrain", out v) ? v as Dictionary<string, object> : null;
            if (t != null && t.ContainsKey("skipped")) sb.Append(T.S("prep.terrainSkipped")).Append('\n');
            else if (t != null) sb.Append(T.S("prep.terrain", (bool)t["worldMap"] ? T.S("check.mapWorld") : "", t["p95Error"], t["points"])).Append('\n');
            var w = r.TryGetValue("water", out v) ? v as Dictionary<string, object> : null;
            if (w != null && w.ContainsKey("skipped")) sb.Append(T.S("prep.waterSkipped")).Append('\n');
            else if (w != null && (string)w["mode"] == "cs1") sb.Append(T.S("prep.water", Math.Round(Convert.ToDouble(w["seaLevel"]), 1), w["sources"])).Append('\n');
            else if (w != null) sb.Append(T.S("prep.dry")).Append('\n');
            sb.Append(T.S("prep.next")).Append('\n');
            sb.Append(T.S("report", report));
            return sb.ToString();
        }

        // ---------------------------------------------------------------- import

        public static bool Import(Setting s)
        {
            try
            {
                if (!InCity()) throw new InvalidOperationException(T.S("err.noCity"));
                if (ImportSystem.Busy) throw new InvalidOperationException(T.S("err.busy"));
                var args = Args(s, "import");
                // #63: a left-hand plan never goes into a right-hand city (or the other way round) unless asked for
                var planLht = MapSteps.PlanLeftHand((Dictionary<string, object>)MiniJson.Parse(File.ReadAllText((string)args["plan"])));
                var side = Cs2Importer.Core.DrivingSide.Mismatch(planLht, MapSteps.CityLeftHand());
                if (side != null && !s.AllowOtherDrivingSide) throw new InvalidOperationException(T.S(planLht == true ? "err.sideLeft" : "err.sideRight"));
                if (side != null) args["allowDrivingSideMismatch"] = true;
                if (!s.KeepMapTerrain && (bool)MapContents((string)args["plan"])["terrain"])
                {
                    var diff = MapSteps.TerrainMismatch((string)args["plan"]);
                    if (diff.HasValue && diff.Value > MapSteps.PreparedTolerance) throw new InvalidOperationException(T.S("err.notPrepared", Math.Round(diff.Value, 1)));
                }
                var report = Path.Combine(LocalRoot, "import-reports", "import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                args["_progress"] = (Action<string>)(t => Mod.Instance.SetStatus(T.S("imp.progress", TranslateProgress(t))));
                args["_cancel"] = (Func<bool>)(() => ImportSystem.CancelRequested);
                Mod.Instance.SetStatus(T.S("imp.running"));
                ImportSystem.Start(Chain(s, args, report), (result, error) =>
                {
                    if (error != null) { Mod.Instance.SetStatus(T.S("imp.failed", error.Message)); return; }
                    s.LastReportPath = report;
                    Mod.Instance.SetStatus(ResultText(result as Dictionary<string, object>, report));
                });
                return true;
            }
            catch (Exception e)
            {
                Mod.Instance.SetStatus(T.S("imp.notStarted", e.Message));
                return false;
            }
        }

        static readonly Regex BatchProgress = new Regex(@"^batch (\d+) of (\d+): (\d+) of (\d+) segments placed$");

        /// <summary>The road import reports its progress in English; the status line shows it in the game's language.</summary>
        static string TranslateProgress(string t)
        {
            var m = BatchProgress.Match(t ?? "");
            if (m.Success) return T.S("imp.batch", m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
            if (t == "checking what was built") return T.S("imp.verify");
            return t;
        }

        /// <summary>
        /// The whole import as one coroutine: a snapshot for "Remove imported", the roads, then each enabled step (traffic
        /// settings, Traffic mod, zoning, the steps other code registered in ImportSteps, outside connections), one after
        /// the other, then the record of what was created and one combined report. Cancel stops between steps (and the
        /// roads between batches); what's built so far is recorded too.
        /// </summary>
        static IEnumerator Chain(Setting s, Dictionary<string, object> args, string report)
        {
            var all = new Dictionary<string, object>();
            Undo.Snapshot before = null;
            try { before = Undo.Take(); }
            catch (Exception ex) { Mod.Log.Warn("undo snapshot failed: " + ex); all["undo"] = new Dictionary<string, object> { { "error", ex.Message } }; }

            var steps = new List<KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>>>();
            steps.Add(new KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>>("import", a => (IEnumerator)Entry.Run(a)));
            // zoning after the traffic steps: they mark edges Updated, and the blocks must be final when they're painted
            if (s.ApplyTrafficSettings) steps.Add(Op("traffic"));
            if (s.ApplyTrafficMod) steps.Add(Op("trafficmod"));
            if (s.ApplyLaneConnections)
            {
                // right-hand cities only for now: in left-hand ones too few CS1 lanes resolve (DECISIONS-importer.md #54/#55)
                if (MapSteps.CityLeftHand() && !LeftHandLanesReady) steps.Add(new KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>>("lanes", a => SkippedWith(T.S("line.lanesLeftHand"))));
                else steps.Add(Op("lanes"));
            }
            if (s.ApplyZoning) steps.Add(Op("zoning"));
            steps.AddRange(ImportSteps.Registered);
            // last: the connections own the imported end nodes, and nothing may rebuild those nodes afterwards
            if (s.ApplyConnections) steps.Add(new KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>>("connections", MapSteps.Connections));
            foreach (var st in steps)
            {
                if (ImportSystem.CancelRequested) { all["cancelled"] = st.Key; break; }
                var stepArgs = new Dictionary<string, object>(args);
                if (st.Key != "import") { stepArgs.Remove("_progress"); stepArgs.Remove("_cancel"); stepArgs["op"] = st.Key; Mod.Instance.SetStatus(T.S("imp.progress", T.Step(st.Key))); }
                object last = null;
                IEnumerator e = null;
                try { e = st.Value(stepArgs); }
                catch (Exception ex) { all[st.Key] = new Dictionary<string, object> { { "error", ex.Message } }; Mod.Log.Warn(st.Key + " failed: " + ex); continue; }
                while (true)
                {
                    bool more;
                    try { more = e.MoveNext(); }
                    catch (Exception ex) { last = new Dictionary<string, object> { { "error", ex.Message } }; Mod.Log.Warn(st.Key + " failed: " + ex); break; }
                    if (!more) break;
                    if (e.Current != null) last = e.Current;
                    yield return null;
                }
                all[st.Key] = last;
                var lr = last as Dictionary<string, object>;
                if (st.Key == "import" && lr != null && lr.ContainsKey("cancelled_after_batch")) { all["cancelled"] = "import"; break; }
            }
            if (before != null)
            {
                try { all["undo"] = Undo.Record(before, (string)args["plan"]); }
                catch (Exception ex) { Mod.Log.Warn("undo record failed: " + ex); all["undo"] = new Dictionary<string, object> { { "error", ex.Message } }; }
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report));
                File.WriteAllText(report, MiniJson.Write(all), new UTF8Encoding(false));
            }
            catch (Exception ex) { Mod.Log.Warn("report not written: " + ex.Message); }
            yield return all;
        }

        /// <summary>Lane connections in left-hand cities: on since the left-hand roads match CS1 (DECISIONS-importer.md #55).
        /// A switch kept for a quick rollback; tests can set it through op mod setStatic.</summary>
        public static bool LeftHandLanesReady = true;

        static IEnumerator SkippedWith(string why)
        {
            yield return new Dictionary<string, object> { { "skipped", why } };
        }

        static KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>> Op(string op)
        {
            return new KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>>(op, a => (IEnumerator)Entry.Run(a));
        }

        static string ResultText(Dictionary<string, object> all, string report)
        {
            var r = all != null && all.ContainsKey("import") ? all["import"] as Dictionary<string, object> : null;
            if (r == null || !r.ContainsKey("result")) return all != null && all.ContainsKey("import") && all["import"] is Dictionary<string, object> d && d.ContainsKey("error") ? T.S("imp.failed", d["error"]) : T.S("imp.noReport");
            var res = (Dictionary<string, object>)r["result"];
            var sb = new StringBuilder();
            object c;
            if (all.TryGetValue("cancelled", out c)) sb.Append(T.S("imp.cancelled", T.Step(c as string == "import" ? "roads" : (string)c))).Append('\n');
            sb.Append(T.S("imp.done", r["seconds"], res["segments_built"], res["segments_planned"]));
            int failed = Convert.ToInt32(res["segments_failed"]);
            if (failed > 0) sb.Append(T.S("imp.failedN", failed));
            sb.Append(".\n");
            var fails = (Dictionary<string, object>)r["failures_by_reason"];
            if (fails.Count > 0) sb.Append(T.S("imp.problems", string.Join(", ", fails.Select(kv => kv.Value + " " + kv.Key).ToArray()))).Append('\n');
            object tun;
            if (r.TryGetValue("tunnels", out tun) && tun is Dictionary<string, object> td && Convert.ToInt32(td["deepened"]) > 0)
                sb.Append(T.S("imp.tunnelsDeepened", td["deepened"], td["cs1_tunnel_segments"])).Append('\n');
            var plan = (Dictionary<string, object>)r["plan"];
            if (Convert.ToInt32(plan["segments_skipped"]) > 0) sb.Append(T.S("imp.planSkipped", plan["segments_skipped"])).Append('\n');
            var temp = (Dictionary<string, object>)r["temp_after"];
            if (Convert.ToInt32(temp["temp_any"]) > 0) sb.Append(T.S("imp.ghosts", temp["temp_any"])).Append('\n');
            foreach (var kv in all)
            {
                if (kv.Key == "import" || kv.Key == "cancelled" || kv.Key == "undo") continue;
                sb.Append(StepLine(kv.Key, kv.Value as Dictionary<string, object>)).Append('\n');
            }
            object u;
            if (all.TryGetValue("undo", out u) && u is Dictionary<string, object> ud && ud.ContainsKey("total")) sb.Append(T.S("imp.recorded", ud["total"])).Append('\n');
            sb.Append(T.S("report", report));
            return sb.ToString();
        }

        static string StepLine(string name, Dictionary<string, object> d)
        {
            string title = T.Step(name);
            title = char.ToUpper(title[0]) + title.Substring(1);
            if (d == null) return T.S("line.noResult", title);
            object v;
            if (d.TryGetValue("error", out v)) return T.S("line.failed", title, v);
            if (d.TryGetValue("skipped", out v)) return T.S("line.skipped", title, v);
            switch (name)
            {
                case "zoning": return T.S("line.zoning", d["cells_matched"], d["plan_cells"], d["coverage"], d["cells_painted"]);
                case "traffic": return T.S("line.traffic", d["lights_added"], d["lights_removed"], d["all_way_stops"], d["turn_ban_ends"], Convert.ToInt32(d["crosswalks_added"]) + Convert.ToInt32(d["crosswalks_removed"]));
                case "trafficmod": return T.S("line.trafficmod", d["priority_signs"], d["edges_with_priorities"]);
                case "lanes":
                    {
                        string unsure = "";
                        object ac;
                        int left = 0;
                        if (d.TryGetValue("arm_check", out ac) && ac is Dictionary<string, object> acd) left += Convert.ToInt32(acd["junctions_left_to_cs2"]);
                        // junctions whose written lanes failed the turn check were reverted to CS2's lanes (#126)
                        if (d.TryGetValue("turn_check", out ac) && ac is Dictionary<string, object> tcd && tcd.ContainsKey("junctions_reverted_to_cs2")) left += Convert.ToInt32(tcd["junctions_reverted_to_cs2"]);
                        if (left > 0) unsure = T.S("line.lanesArmCheck", left);
                        return T.S("line.lanes", d["connections"], d["nodes_done"], d["nodes_planned"], d["lane_refs_matched"], d["lane_refs"], ((string)d["handedness"]).StartsWith("mirrored") ? T.S("line.lanesMirrored") : "") + unsure;
                    }
                case "connections": return T.S("line.connections", d["placed"], d["items"], d["road"], d["train"], Convert.ToInt32(d["failed"]) > 0 ? T.S("line.connectionsFailed", d["failed"]) : "");
                case "transit":
                {
                    var st = d.TryGetValue("stops", out v) ? v as Dictionary<string, object> : null;
                    return T.S("line.transit", d["placed"], d["items"], st != null ? T.S("line.transitStops", st["defined"], st["requested"]) : "");
                }
                default:
                    // buildings/ worker's steps: buildings {items, placed, alreadyPlaced, failed}, objects {items, created},
                    // districts / surfaces {items, placed, failed}
                    if (d.TryGetValue("items", out v))
                    {
                        object done;
                        if (!d.TryGetValue("placed", out done)) d.TryGetValue("created", out done);
                        object already, failedN;
                        d.TryGetValue("alreadyPlaced", out already);
                        d.TryGetValue("failed", out failedN);
                        var line = T.S("line.generic", title, done ?? 0, v,
                            already != null && Convert.ToInt32(already) > 0 ? T.S("line.already", already) : "",
                            failedN != null && Convert.ToInt32(failedN) > 0 ? T.S("line.failedN", failedN) : "");
                        // buildings (#128): buildings to place by hand, and warnings such as "no power plant placed"
                        object py, warns;
                        if (d.TryGetValue("placeYourself", out py) && py is System.Collections.ICollection pyc && pyc.Count > 0)
                            line += T.S("line.placeYourself", pyc.Count);
                        if (d.TryGetValue("warnings", out warns) && warns is System.Collections.IEnumerable we && !(warns is string))
                            foreach (var x in we)
                                if (x is string ws && ws.Length > 0) line += "\n" + T.S("line.stepWarning", ws);
                        return line;
                    }
                    return T.S("line.done", title);
            }
        }

        // ---------------------------------------------------------------- cancel + remove imported

        public static bool Cancel(Setting s)
        {
            bool ok = ImportSystem.Cancel();
            Mod.Instance.SetStatus(T.S(ok ? "cancel.requested" : "cancel.none"));
            return ok;
        }

        public static bool RemoveImported(Setting s)
        {
            try
            {
                if (!InCity()) throw new InvalidOperationException(T.S("err.noCity"));
                if (ImportSystem.Busy) throw new InvalidOperationException(T.S("err.busy"));
                if (Undo.Recorded() == 0) { Mod.Instance.SetStatus(T.S("undo.nothing")); return false; }
                Mod.Instance.SetStatus(T.S("undo.running"));
                ImportSystem.Start(Undo.Remove(() => ImportSystem.CancelRequested, k => Mod.Instance.SetStatus(T.S("undo.progress", k))), (result, error) =>
                {
                    if (error != null) { Mod.Instance.SetStatus(T.S("undo.failed", error.Message)); return; }
                    var r = result as Dictionary<string, object>;
                    if (r == null || r.ContainsKey("nothing")) { Mod.Instance.SetStatus(T.S("undo.nothing")); return; }
                    if (r.ContainsKey("cancelled")) { Mod.Instance.SetStatus(T.S("undo.cancelled")); return; }
                    var n = (Dictionary<string, object>)r["removed"];
                    Func<string, int> c = k => Convert.ToInt32(n[k]);
                    int gone = Convert.ToInt32(r["gone"]);
                    Mod.Instance.SetStatus(T.S("undo.done", c("edge"), c("building"), c("object"), c("area"), c("route")) + (gone > 0 ? T.S("undo.gone", gone) : ""));
                    s.LastUndo = MiniJson.Write(r);
                });
                return true;
            }
            catch (Exception e)
            {
                Mod.Instance.SetStatus(T.S("undo.notStarted", e.Message));
                return false;
            }
        }
    }
}
