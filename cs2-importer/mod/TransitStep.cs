// The "transit" import step: CS1 transit lines (plan.transit) → CS2 stops and lines, inside the mod's import chain.
// It runs the transit worker's own library (transit/cs2-exec/Transit.cs, compiled into the mod, not copied) in the same
// order and pacing as their verified `node transit/transit.mjs apply` (2026-11-21: 145/145 stops, 9/9 lines):
//   1. kerbside stops, 20 per frame with 10 frames between batches, then 30 frames;
//   2. each line on its own (route definitions + the forced apply frame, stepped here in ToolUpdate), resolving stops
//      at the positions step 1 created them;
//   3. CS1 names and numbers (NameSystem: needs the end-frame barrier, which ImportSystem allows before every step);
//   4. a check: lines and counts.
// With the opt-in setting "Connect train and metro stations" (off by default, DECISIONS.md #161), between 1 and 2: rail and subway
// yards at the crop edge where a line's track leaves the area (#83; interior only if the plan's depotMode says so),
// placed with the buildings worker's placer, then the transit worker's `tracklink` (station platform ends and yards
// joined to the imported track with smooth curves, `tangentLinks`, DECISIONS-transit #16; yards in their own track
// prefab; never across a road at grade, `avoidRoads`, so imported roads aren't split into level crossings; and
// `guardRoads`: imported two-edge road nodes near the links are kept through the apply, which would otherwise merge them). Train and metro lines are
// created after that, so they pathfind over the joined track; no repath is needed.
// The jobs come from Cs2Importer.Core.TransitPlan, a C# port of transit/lib/plan.mjs transitJobs, kept in step by a
// golden test (tests/CoreTests/TransitPlanTests.cs).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Cs1ConvTransit;
using GameBridge;

namespace Cs2ImporterMod
{
    public static class TransitStep
    {
        public static IEnumerator Run(Dictionary<string, object> args)
        {
            var root = MiniJson.Parse(File.ReadAllText(Cs2Importer.Entry.PlanFile(args), Encoding.UTF8)) as Dictionary<string, object>;
            var transit = P.Obj(root, "transit");
            var planLines = P.List(transit, "lines");
            if (planLines == null || planLines.Count == 0) { yield return Skipped("no transit lines in the plan"); yield break; }
            double offX = P.Num(args, "offsetX", 0), offZ = P.Num(args, "offsetZ", 0);
            // stop prefabs follow the zoning theme of the plan (EU by default, as the transit CLI)
            string theme = P.Str(args, "transit.theme", null) ?? P.Str(P.Obj(root, "zoning"), "theme", "EU");
            var jobs = Cs2Importer.Core.TransitPlan.BuildJobs(root, theme, offX, offZ);
            var report = new Dictionary<string, object> { { "plan", jobs.Stats }, { "planSkipped", jobs.Skipped }, { "theme", theme } };

            // 1. stops
            var created = new List<object>();
            for (int i = 0; i < jobs.Stops.Count; i += 20)
            {
                var batch = jobs.Stops.GetRange(i, Math.Min(20, jobs.Stops.Count - i));
                var r = (Dictionary<string, object>)Ops.Run(new Dictionary<string, object> { { "op", "stops.create" }, { "stops", new List<object>(batch) } });
                var results = P.List(r, "results");
                if (results != null) created.AddRange(results);
                for (int f = 0; f < 10; f++) yield return null;
            }
            for (int f = 0; f < 30; f++) yield return null;
            // the stops' roads re-select their composition now (the bus bay), not only on the next load: otherwise the road
            // changes on reload and lane connections written for the old lanes break (DECISIONS-importer.md #64)
            if (created.Count > 0)
            {
                try { report["stopsTouch"] = Ops.Run(new Dictionary<string, object> { { "op", "stops.touch" } }); }
                catch (Exception e) { report["stopsTouch"] = new Dictionary<string, object> { { "error", e.Message } }; }
                for (int f = 0; f < 30; f++) yield return null;
            }
            var errors = new List<object>();
            var posByKey = new Dictionary<string, List<object>>();
            foreach (var o in created)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                if (d.ContainsKey("error")) { errors.Add(d); continue; }
                var key = P.Str(d, "key", null);
                var pos = P.List(d, "pos");
                if (key != null && pos != null && pos.Count >= 3) posByKey[key] = pos;
            }
            report["stops"] = new Dictionary<string, object> { { "requested", jobs.Stops.Count }, { "defined", created.Count - errors.Count }, { "errors", errors } };
            foreach (var l in jobs.Lines)
                foreach (var so in (List<object>)l["stops"])
                {
                    var s = (Dictionary<string, object>)so;
                    List<object> p;
                    var key = P.Str(s, "key", null);
                    if (key != null && posByKey.TryGetValue(key, out p)) { s["x"] = p[0]; s["z"] = p[2]; }
                }

            // 1b. train and metro: yards at the crop edge + station and yard links (before the lines, so they pathfind)
            if (P.Bool(args, "transit.rail", false))
            {
                var rail = Rail(root, planLines, offX, offZ, args);
                object last = null;
                while (rail.MoveNext()) { if (rail.Current != null) last = rail.Current; yield return null; }
                var rr = last as Dictionary<string, object>;
                if (rr != null)
                {
                    report["rail"] = rr;
                    object w;
                    if (rr.TryGetValue("warnings", out w)) report["warnings"] = w;
                }
            }
            else report["rail"] = Skipped("setting off");

            // 2. lines, one at a time
            int linesCreated = 0, linesSkipped = 0, tempRoutes = 0;
            bool applied = true;
            var detail = new List<object>();
            foreach (var l in jobs.Lines)
            {
                var e = (IEnumerator)Ops.Run(new Dictionary<string, object> { { "op", "lines.create" }, { "lines", new List<object> { l } }, { "frames", 40.0 }, { "radius", 4.0 } });
                object last = null;
                while (e.MoveNext()) { if (e.Current != null) last = e.Current; yield return null; }
                var r = last as Dictionary<string, object>;
                if (r == null) continue;
                int c = (int)P.Num(r, "created", 0);
                linesCreated += c;
                linesSkipped += (int)P.Num(r, "skipped", 0);
                tempRoutes += (int)P.Num(r, "tempRoutes", 0);
                if (c > 0) applied &= P.Bool(r, "applied", false);
                var rl = P.List(r, "lines");
                if (rl != null) detail.AddRange(rl);
            }
            var linesReport = new Dictionary<string, object> { { "created", linesCreated }, { "skipped", linesSkipped }, { "tempRoutes", tempRoutes }, { "applied", applied }, { "detail", detail } };
            report["lines"] = linesReport;

            // 3. names and numbers
            var byKey = new Dictionary<string, Dictionary<string, object>>();
            foreach (var l in jobs.Lines) byKey[(string)l["key"]] = l;
            var routes = new List<object>();
            foreach (var o in detail)
            {
                var d = o as Dictionary<string, object>;
                var route = P.Str(d, "route", null);
                Dictionary<string, object> pl;
                if (route == null || !byKey.TryGetValue(P.Str(d, "key", ""), out pl)) continue;
                routes.Add(new Dictionary<string, object> { { "route", route }, { "name", pl["name"] }, { "number", pl["number"] } });
            }
            try
            {
                var named = (Dictionary<string, object>)Ops.Run(new Dictionary<string, object> { { "op", "lines.name" }, { "routes", routes } });
                linesReport["named"] = P.Num(named, "named", 0);
            }
            catch (Exception ex) { linesReport["named_error"] = ex.Message; }

            // 4. check after the game has settled
            for (int f = 0; f < 60; f++) yield return null;
            report["check"] = new Dictionary<string, object> { { "lines", Ops.Run(new Dictionary<string, object> { { "op", "lines.list" } }) }, { "counts", Ops.Run(new Dictionary<string, object> { { "op", "counts" } }) } };
            report["items"] = jobs.Lines.Count;
            report["placed"] = linesCreated;
            yield return report;
        }

        /// <summary>The rail part of `node transit/transit.mjs depots` (yards.plan, the placer, tracklink), in the mod.
        /// Yields per frame; the last value is the report: {types, yards: {items, placed, failed}, links: {stations,
        /// linked, tangent, noTarget}, yardLinks, warnings: [text]}.</summary>
        static IEnumerator Rail(Dictionary<string, object> root, List<object> planLines, double offX, double offZ, Dictionary<string, object> args)
        {
            var warnings = new List<object>();
            var r = new Dictionary<string, object>();
            // the lines with a rail type, their stops moved like the import
            var lines = new List<object>();
            foreach (var o in planLines)
            {
                var l = o as Dictionary<string, object>;
                string t = P.Str(l, "cs1Type", null);
                if (t != "Train" && t != "Metro") continue;
                var stops = new List<object>();
                var ls = P.List(l, "stops");
                if (ls != null) foreach (var so in ls) { var sd = so as Dictionary<string, object>; stops.Add(new Dictionary<string, object> { { "x", P.Num(sd, "x", 0) + offX }, { "z", P.Num(sd, "z", 0) + offZ } }); }
                lines.Add(new Dictionary<string, object> { { "name", P.Str(l, "name", "?") }, { "cs1Type", t }, { "stops", stops } });
            }
            if (lines.Count == 0) { r["skipped"] = "no train or metro lines"; yield return r; yield break; }
            var b = P.Obj(root, "bounds");
            if (b == null) { r["skipped"] = "the plan has no bounds"; yield return r; yield break; }
            var bounds = new Dictionary<string, object> { { "minX", P.Num(b, "minX", 0) + offX }, { "maxX", P.Num(b, "maxX", 0) + offX }, { "minZ", P.Num(b, "minZ", 0) + offZ }, { "maxZ", P.Num(b, "maxZ", 0) + offZ } };
            var depotMode = P.Obj(P.Obj(root, "transit"), "depotMode");

            // yards where a line's track leaves the area (#83), else report entries
            var yp = (Dictionary<string, object>)Ops.Run(new Dictionary<string, object> { { "op", "yards.plan" }, { "bounds", bounds }, { "lines", lines }, { "depotMode", (object)depotMode }, { "edgeMargin", 80.0 } });
            r["types"] = yp["types"];
            var planReport = P.List(yp, "report");
            if (planReport != null) foreach (var o in planReport) { var m = P.Str(o as Dictionary<string, object>, "message", null); if (m != null) warnings.Add(m); }
            var items = P.List(P.Obj(yp, "buildings"), "items") ?? new List<object>();
            int placed = 0;
            if (items.Count > 0)
            {
                string file = Path.Combine(Path.GetTempPath(), "cs1conv-rail-yards.json");
                File.WriteAllText(file, MiniJson.Write(new Dictionary<string, object> { { "buildings", new Dictionary<string, object> { { "items", items } } } }), Encoding.UTF8);
                var place = (IEnumerator)Cs2Buildings.Placement.Place(new Dictionary<string, object> { { "plan", file }, { "offsetX", 0.0 }, { "offsetZ", 0.0 }, { "fallback", "nearby" }, { "nearbyRadius", 600.0 } });
                object last = null;
                while (place.MoveNext()) { if (place.Current != null) last = place.Current; yield return null; }
                var pr = last as Dictionary<string, object>;
                placed = (int)P.Num(pr, "placed", 0);
                r["yards"] = new Dictionary<string, object> { { "items", items.Count }, { "placed", placed }, { "failed", P.Num(pr, "failed", 0) } };
                // a yard that found no free land with road access: its lines need a depot placed by hand
                var sample = P.List(pr, "sample");
                if (sample != null)
                    foreach (var o in sample)
                    {
                        var sd = o as Dictionary<string, object>;
                        string st = P.Str(sd, "status", "");
                        if (st == "placed" || st == "alreadyPlaced") continue;
                        foreach (var io in items)
                        {
                            var it = io as Dictionary<string, object>;
                            if (P.Num(it, "id", 0) != P.Num(sd, "id", 1)) continue;
                            string kind = (P.Str(it, "prefab", "") ?? "").Contains("Subway") ? "subway yard" : "rail yard";
                            var ln = P.List(it, "lines");
                            if (ln != null) foreach (var n in ln) warnings.Add(n + ": no free land with road access for a " + kind + " where its track leaves the converted area, so no depot was placed: place a " + kind + " yourself and connect its track to the line.");
                        }
                    }
                for (int f = 0; f < 60; f++) yield return null;
            }

            // station platform ends and yards joined to the imported track (smooth curves; touchEnds stays off)
            var link = (IEnumerator)Ops.Run(new Dictionary<string, object> { { "op", "tracklink" }, { "maxDistance", 2500.0 }, { "tangentLinks", true }, { "yardLinkPrefab", "yard" }, { "avoidRoads", true }, { "guardRoads", true }, { "bridgeYard", true }, { "frontOnly", true } });
            object lr = null;
            while (link.MoveNext()) { if (link.Current != null) lr = link.Current; yield return null; }
            var ld = lr as Dictionary<string, object>;
            int stations = 0, linked = 0, tangent = 0, noTarget = 0;
            var sl = P.List(ld, "stations");
            if (sl != null)
                foreach (var o in sl)
                {
                    stations++;
                    var links = P.List(o as Dictionary<string, object>, "links");
                    if (links == null) continue;
                    foreach (var lo in links)
                    {
                        var li = lo as Dictionary<string, object>;
                        if (P.Str(li, "status", "") == "linked") linked++; else noTarget++;
                        var lk = P.Obj(li, "link");
                        if (lk != null && P.Bool(lk, "tangent", false)) tangent++;
                    }
                }
            r["links"] = new Dictionary<string, object> { { "stations", stations }, { "linked", linked }, { "tangent", tangent }, { "noTarget", noTarget }, { "bridges", P.List(ld, "bridges")?.Count ?? 0 }, { "trimmedEdges", P.Num(ld, "trimmedEdges", 0) }, { "applied", P.Bool(ld, "applied", false) } };
            var yl = P.List(ld, "yards");
            var yardLinks = new List<object>();
            if (yl != null) foreach (var o in yl) { var y = o as Dictionary<string, object>; yardLinks.Add(new Dictionary<string, object> { { "prefab", P.Str(y, "prefab", "") }, { "status", P.Str(y, "status", "") } }); if (P.Str(y, "status", "") == "blocked" || P.Str(y, "status", "") == "too-far" || P.Str(y, "status", "") == "no-network") warnings.Add(P.Str(y, "prefab", "yard") + ": its track could not be joined to the line (" + P.Str(y, "status", "") + "); connect it yourself."); }
            r["yardLinks"] = yardLinks;
            // every link as tracklink built it (opt-in rail only; for diagnosis)
            r["detail"] = new Dictionary<string, object> { { "stations", sl }, { "yards", yl }, { "bridges", P.List(ld, "bridges") } };
            for (int f = 0; f < 60; f++) yield return null;
            r["warnings"] = warnings;
            yield return r;
        }

        static Dictionary<string, object> Skipped(string why) { return new Dictionary<string, object> { { "skipped", why } }; }
    }
}
