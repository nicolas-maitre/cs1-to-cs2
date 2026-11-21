// The "transit" import step: CS1 transit lines (plan.transit) → CS2 stops and lines, inside the mod's import chain.
// It runs the transit worker's own library (transit/cs2-exec/Transit.cs, compiled into the mod, not copied) in the same
// order and pacing as their verified `node transit/transit.mjs apply` (2026-11-21: 145/145 stops, 9/9 lines):
//   1. kerbside stops, 20 per frame with 10 frames between batches, then 30 frames;
//   2. each line on its own (route definitions + the forced apply frame, stepped here in ToolUpdate), resolving stops
//      at the positions step 1 created them;
//   3. CS1 names and numbers (NameSystem: needs the end-frame barrier, which ImportSystem allows before every step);
//   4. a check: lines and counts.
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

        static Dictionary<string, object> Skipped(string why) { return new Dictionary<string, object> { { "skipped", why } }; }
    }
}
