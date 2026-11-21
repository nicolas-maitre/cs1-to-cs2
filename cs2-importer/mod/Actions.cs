// What the Options page buttons do. Kept apart from Setting so scripts and the UI share one code path.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cs2Importer;
using Game;
using Game.SceneFlow;
using GameBridge;

namespace Cs2ImporterMod
{
    public static class Actions
    {
        public static string LocalRoot
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "cs1-to-cs2"); }
        }

        public static string RoadBuilderFolder
        {
            get { return Path.Combine(UnityEngine.Application.persistentDataPath, "ModsData", "RoadBuilder"); }
        }

        /// <summary>Folders with a cs1conv-plan.json under %LOCALAPPDATA%\cs1-to-cs2 (3 levels deep), newest first.</summary>
        public static List<string> FindPackages()
        {
            var found = new List<string>();
            try { Walk(LocalRoot, 0, found); } catch (Exception e) { Mod.Log.Warn("package search failed: " + e.Message); }
            found.Sort((a, b) => File.GetLastWriteTimeUtc(Path.Combine(b, "cs1conv-plan.json")).CompareTo(File.GetLastWriteTimeUtc(Path.Combine(a, "cs1conv-plan.json"))));
            return found;
        }

        static void Walk(string dir, int depth, List<string> found)
        {
            if (!Directory.Exists(dir) || depth > 3) return;
            if (File.Exists(Path.Combine(dir, "cs1conv-plan.json"))) { found.Add(dir); return; }
            foreach (var d in Directory.GetDirectories(dir)) Walk(d, depth + 1, found);
        }

        public static string Describe(string dir)
        {
            var name = Path.GetFileName(dir.TrimEnd('\\', '/'));
            return name + "  (" + File.GetLastWriteTime(Path.Combine(dir, "cs1conv-plan.json")).ToString("yyyy-MM-dd HH:mm") + ")";
        }

        static string PlanFile(Setting s)
        {
            var dir = (s.PackageFolder ?? "").Trim().Trim('"');
            if (dir.Length == 0) throw new InvalidOperationException("Enter the package folder first (the folder with cs1conv-plan.json).");
            var plan = Directory.Exists(dir) ? Path.Combine(dir, "cs1conv-plan.json") : dir;
            if (!File.Exists(plan)) throw new InvalidOperationException("No cs1conv-plan.json in " + dir + ".");
            return plan;
        }

        static Dictionary<string, object> Args(Setting s, string op)
        {
            return new Dictionary<string, object>
            {
                { "op", op }, { "plan", PlanFile(s) }, { "offsetX", (double)s.OffsetX }, { "offsetZ", (double)s.OffsetZ },
                { "heights", "flat" }, { "clear", s.ClearArea },
            };
        }

        // ---------------------------------------------------------------- check

        /// <summary>Plan check plus the Road Builder state: missing config files vs. files copied after CS2 started.</summary>
        public static Dictionary<string, object> Check(Setting s)
        {
            try
            {
                var r = (Dictionary<string, object>)Entry.Run(Args(s, "check"));
                var rb = RoadBuilderState(s, r);
                r["roadbuilder"] = rb;
                Mod.Instance.SetStatus(CheckText(r, rb));
                return r;
            }
            catch (Exception e)
            {
                Mod.Instance.SetStatus("Check failed: " + e.Message);
                return new Dictionary<string, object> { { "error", e.Message } };
            }
        }

        static Dictionary<string, object> RoadBuilderState(Setting s, Dictionary<string, object> check)
        {
            var pkg = Path.GetDirectoryName(PlanFile(s));
            int missingFile = 0, notLoaded = 0, packaged = 0;
            var pkgRb = Path.Combine(pkg, "roadbuilder");
            if (Directory.Exists(pkgRb)) packaged = Directory.GetFiles(pkgRb, "*-cs1conv.json").Length;
            foreach (var o in (List<object>)check["prefabs"])
            {
                var p = (Dictionary<string, object>)o;
                if ((string)p["type"] != "RoadBuilderPrefab" || (bool)p["found"]) continue;
                if (File.Exists(Path.Combine(RoadBuilderFolder, (string)p["name"] + ".json"))) notLoaded++;
                else missingFile++;
            }
            return new Dictionary<string, object>
            {
                { "folder", RoadBuilderFolder }, { "configs_in_package", packaged },
                { "missing_files", missingFile }, { "copied_but_not_loaded", notLoaded },
            };
        }

        static string CheckText(Dictionary<string, object> r, Dictionary<string, object> rb)
        {
            var plan = (Dictionary<string, object>)r["plan"];
            var sb = new StringBuilder();
            sb.Append("Package: ").Append(plan["source"]).Append(", ").Append(plan["segments_to_build"]).Append(" roads to build");
            if (Convert.ToInt32(plan["segments_skipped"]) > 0) sb.Append(" (").Append(plan["segments_skipped"]).Append(" skipped)");
            sb.Append(".\n");
            int missing = Convert.ToInt32(r["prefabs_missing"]);
            int missingFiles = Convert.ToInt32(rb["missing_files"]), notLoaded = Convert.ToInt32(rb["copied_but_not_loaded"]);
            if (missing == 0) sb.Append("All ").Append(((List<object>)r["prefabs"]).Count).Append(" road types are available.\n");
            else
            {
                sb.Append(missing).Append(" road types are missing. ");
                if (missingFiles > 0) sb.Append(missingFiles).Append(" Road Builder configs aren't installed: press \"Install Road Builder configs\", then restart Cities: Skylines II (Road Builder only reads its configs at startup). ");
                if (notLoaded > 0) sb.Append(notLoaded).Append(" Road Builder configs are installed but not loaded yet: restart Cities: Skylines II. ");
                if (missing > missingFiles + notLoaded) sb.Append("Other missing types are listed in the log. ");
                sb.Append("Roads of a missing type are skipped.\n");
            }
            int existing = r.ContainsKey("existing_edges_in_area") ? Convert.ToInt32(r["existing_edges_in_area"]) : 0;
            if (existing > 0) sb.Append(existing).Append(" roads already in the area: keep \"Clear the area first\" on.\n");
            var b = (Dictionary<string, object>)r["bounds"];
            if (!(bool)b["inside_cs2_map"]) sb.Append("The crop doesn't fit the CS2 map.\n");
            return sb.ToString().TrimEnd();
        }

        // ---------------------------------------------------------------- Road Builder configs

        public static Dictionary<string, object> InstallRoadBuilderConfigs(Setting s)
        {
            try
            {
                var src = Path.Combine(Path.GetDirectoryName(PlanFile(s)), "roadbuilder");
                if (!Directory.Exists(src)) throw new InvalidOperationException("The package has no roadbuilder folder.");
                Directory.CreateDirectory(RoadBuilderFolder);
                int n = 0;
                foreach (var f in Directory.GetFiles(src, "*-cs1conv.json")) // only ever our own generated configs
                {
                    File.Copy(f, Path.Combine(RoadBuilderFolder, Path.GetFileName(f)), true);
                    n++;
                }
                Mod.Instance.SetStatus("Copied " + n + " Road Builder configs. Restart Cities: Skylines II so Road Builder loads them, then import.");
                return new Dictionary<string, object> { { "copied", n }, { "to", RoadBuilderFolder } };
            }
            catch (Exception e)
            {
                Mod.Instance.SetStatus("Installing the Road Builder configs failed: " + e.Message);
                return new Dictionary<string, object> { { "error", e.Message } };
            }
        }

        // ---------------------------------------------------------------- import

        public static bool Import(Setting s)
        {
            try
            {
                var gm = GameManager.instance;
                if (gm == null || gm.isGameLoading || !gm.gameMode.IsGame()) throw new InvalidOperationException("Load or start a city first.");
                if (ImportSystem.Busy) throw new InvalidOperationException("An import is already running.");
                var args = Args(s, "import");
                var report = Path.Combine(LocalRoot, "import-reports", "import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
                args["_progress"] = (Action<string>)(t => Mod.Instance.SetStatus("Importing… " + t));
                Mod.Instance.SetStatus("Importing…");
                ImportSystem.Start(Chain(s, args, report), (result, error) =>
                {
                    if (error != null) { Mod.Instance.SetStatus("Import failed: " + error.Message); return; }
                    s.LastReportPath = report;
                    Mod.Instance.SetStatus(ResultText(result as Dictionary<string, object>, report));
                });
                return true;
            }
            catch (Exception e)
            {
                Mod.Instance.SetStatus("Import not started: " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// The whole import as one coroutine: the roads, then each enabled step (zoning, traffic settings, Traffic mod,
        /// then steps other mods registered in ImportSteps), one after the other. Writes one combined report.
        /// </summary>
        static IEnumerator Chain(Setting s, Dictionary<string, object> args, string report)
        {
            var all = new Dictionary<string, object>();
            object roads = null;
            var job = (IEnumerator)Entry.Run(args);
            while (job.MoveNext()) { if (job.Current != null) roads = job.Current; yield return null; }
            all["import"] = roads;
            var steps = new List<KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>>>();
            // zoning last: the traffic steps mark edges Updated, and the blocks must be final when they're painted
            if (s.ApplyTrafficSettings) steps.Add(Op("traffic"));
            if (s.ApplyTrafficMod) steps.Add(Op("trafficmod"));
            if (s.ApplyZoning) steps.Add(Op("zoning"));
            steps.AddRange(ImportSteps.Registered);
            foreach (var st in steps)
            {
                Mod.Instance.SetStatus("Importing… " + st.Key);
                var stepArgs = new Dictionary<string, object>(args);
                stepArgs.Remove("_progress");
                stepArgs["op"] = st.Key;
                object last = null;
                IEnumerator e = null;
                try { e = st.Value(stepArgs); }
                catch (Exception ex) { all[st.Key] = new Dictionary<string, object> { { "error", ex.Message } }; continue; }
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
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(report));
                File.WriteAllText(report, MiniJson.Write(all), new UTF8Encoding(false));
            }
            catch (Exception ex) { Mod.Log.Warn("report not written: " + ex.Message); }
            yield return all;
        }

        static KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>> Op(string op)
        {
            return new KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>>(op, a => (IEnumerator)Entry.Run(a));
        }

        static string ResultText(Dictionary<string, object> all, string report)
        {
            var r = all != null ? all["import"] as Dictionary<string, object> : null;
            if (r == null) return "Import finished without a report.";
            var res = (Dictionary<string, object>)r["result"];
            var sb = new StringBuilder();
            sb.Append("Done in ").Append(r["seconds"]).Append(" s: ").Append(res["segments_built"]).Append(" of ").Append(res["segments_planned"]).Append(" roads built");
            int failed = Convert.ToInt32(res["segments_failed"]);
            if (failed > 0) sb.Append(", ").Append(failed).Append(" failed");
            sb.Append(".\n");
            var fails = (Dictionary<string, object>)r["failures_by_reason"];
            if (fails.Count > 0) sb.Append("Problems: ").Append(string.Join(", ", fails.Select(kv => kv.Value + " " + kv.Key).ToArray())).Append(".\n");
            var plan = (Dictionary<string, object>)r["plan"];
            if (Convert.ToInt32(plan["segments_skipped"]) > 0) sb.Append(plan["segments_skipped"]).Append(" roads in the plan were skipped.\n");
            var temp = (Dictionary<string, object>)r["temp_after"];
            if (Convert.ToInt32(temp["temp_any"]) > 0) sb.Append("Warning: ").Append(temp["temp_any"]).Append(" unfinished (ghost) entities remain.\n");
            foreach (var kv in all)
            {
                if (kv.Key == "import") continue;
                sb.Append(StepLine(kv.Key, kv.Value as Dictionary<string, object>)).Append('\n');
            }
            sb.Append("Full report: ").Append(report);
            return sb.ToString();
        }

        static string StepLine(string name, Dictionary<string, object> d)
        {
            if (d == null) return name + ": no result.";
            object v;
            if (d.TryGetValue("error", out v)) return name + " failed: " + v;
            if (d.TryGetValue("skipped", out v)) return name + " skipped: " + v;
            switch (name)
            {
                case "zoning": return "Zoning: " + d["cells_matched"] + " of " + d["plan_cells"] + " cells (" + d["coverage"] + " %), " + d["cells_painted"] + " painted.";
                case "traffic": return "Traffic: " + d["lights_added"] + " lights added, " + d["lights_removed"] + " removed, " + d["all_way_stops"] + " all-way stops, " + d["turn_ban_ends"] + " turn bans, " + (Convert.ToInt32(d["crosswalks_added"]) + Convert.ToInt32(d["crosswalks_removed"])) + " crosswalk changes.";
                case "trafficmod": return "Traffic mod: " + d["priority_signs"] + " priority signs on " + d["edges_with_priorities"] + " roads.";
                case "transit":
                {
                    var st = d.TryGetValue("stops", out v) ? v as Dictionary<string, object> : null;
                    return "Transit: " + d["placed"] + " of " + d["items"] + " lines" + (st != null ? ", " + st["defined"] + " of " + st["requested"] + " stops" : "") + ".";
                }
                default:
                    // buildings/ worker's steps: buildings {items, placed, alreadyPlaced, failed}, objects {items, created},
                    // districts / surfaces {items, placed, failed}
                    if (d.TryGetValue("items", out v))
                    {
                        object done;
                        if (!d.TryGetValue("placed", out done)) d.TryGetValue("created", out done);
                        object already, failed;
                        d.TryGetValue("alreadyPlaced", out already);
                        d.TryGetValue("failed", out failed);
                        return char.ToUpper(name[0]) + name.Substring(1) + ": " + (done ?? 0) + " of " + v + (already != null && Convert.ToInt32(already) > 0 ? " (" + already + " already there)" : "") + (failed != null && Convert.ToInt32(failed) > 0 ? ", " + failed + " failed" : "") + ".";
                    }
                    return name + ": done.";
            }
        }
    }
}
