// The "transit" import step: CS1 transit lines (plan.transit) → CS2 stops and lines, inside the mod's import chain.
// It runs the transit worker's own library (transit/cs2-exec/Transit.cs, compiled into the mod, not copied) in the same
// order and pacing as their verified `node transit/transit.mjs apply` (2026-11-21: 145/145 stops, 9/9 lines):
//   1. kerbside stops, 20 per frame with 10 frames between batches, then 30 frames;
//   2. each line on its own (route definitions + the forced apply frame, stepped here in ToolUpdate), resolving stops
//      at the positions step 1 created them;
//   3. CS1 names and numbers (NameSystem: needs the end-frame barrier, which ImportSystem allows before every step);
//   4. a check: lines and counts.
// BuildJobs is a C# port of transit/lib/plan.mjs transitJobs (keep the two in step).
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
        static readonly Dictionary<string, Dictionary<string, string>> StopPrefabs = new Dictionary<string, Dictionary<string, string>>
        {
            { "EU", new Dictionary<string, string> { { "Bus Stop", "EU_BusStop01" }, { "Tram Stop", "EU_TramStop01" } } },
            { "NA", new Dictionary<string, string> { { "Bus Stop", "NA_BusStop01" }, { "Tram Stop", "NA_TramStop01" } } },
        };

        public static IEnumerator Run(Dictionary<string, object> args)
        {
            var root = MiniJson.Parse(File.ReadAllText(Cs2Importer.Entry.PlanFile(args), Encoding.UTF8)) as Dictionary<string, object>;
            var transit = P.Obj(root, "transit");
            var planLines = P.List(transit, "lines");
            if (planLines == null || planLines.Count == 0) { yield return Skipped("no transit lines in the plan"); yield break; }
            double offX = P.Num(args, "offsetX", 0), offZ = P.Num(args, "offsetZ", 0);
            // stop prefabs follow the zoning theme of the plan (EU by default, as the transit CLI)
            string theme = P.Str(args, "transit.theme", null) ?? P.Str(P.Obj(root, "zoning"), "theme", "EU");
            var jobs = BuildJobs(root, theme, offX, offZ);
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

        sealed class Jobs
        {
            public readonly List<Dictionary<string, object>> Stops = new List<Dictionary<string, object>>();
            public readonly List<Dictionary<string, object>> Lines = new List<Dictionary<string, object>>();
            public readonly List<object> Skipped = new List<object>();
            public readonly Dictionary<string, object> Stats = new Dictionary<string, object> { { "lines", 0 }, { "bus", 0 }, { "station", 0 }, { "kerbStops", 0 }, { "sharedStops", 0 } };
            public void Inc(string k) { Stats[k] = (int)Stats[k] + 1; }
        }

        /// <summary>C# port of transit/lib/plan.mjs transitJobs (offset added for the importer's test placement).</summary>
        static Jobs BuildJobs(Dictionary<string, object> plan, string theme, double offX, double offZ)
        {
            var j = new Jobs();
            Dictionary<string, string> stopNames;
            if (!StopPrefabs.TryGetValue(theme ?? "EU", out stopNames)) stopNames = StopPrefabs["EU"];
            var segs = new Dictionary<long, List<object>>();
            var planSegs = P.List(plan, "segments");
            if (planSegs != null)
                foreach (var o in planSegs)
                {
                    var s = o as Dictionary<string, object>;
                    var bz = P.List(s, "bezier");
                    if (s != null && bz != null && bz.Count == 4) segs[(long)P.Num(s, "id", -1)] = bz;
                }
            var byKey = new Dictionary<string, Dictionary<string, object>>();
            foreach (var lo in P.List(P.Obj(plan, "transit"), "lines"))
            {
                var l = lo as Dictionary<string, object>;
                if (l == null) continue;
                j.Inc("lines");
                var lineStops = new List<object>();
                var line = new Dictionary<string, object>
                {
                    { "key", "cs1-" + Convert.ToString(P.Num(l, "cs1Id", 0), CultureInfo.InvariantCulture) }, { "name", P.Str(l, "name", "") },
                    { "number", P.Has(l, "number") ? (object)P.Num(l, "number", 0) : null }, { "prefab", P.Str(l, "line", null) },
                    { "color", P.Str(l, "color", null) }, { "requiresStations", P.Bool(l, "requiresStations", false) },
                    { "vehicles", P.Has(l, "vehicles") ? (object)P.Num(l, "vehicles", 0) : null }, { "truncated", P.Bool(l, "truncated", false) },
                    { "stops", lineStops },
                };
                var stops = P.List(l, "stops") ?? new List<object>();
                if (P.Bool(l, "requiresStations", false))
                {
                    j.Inc("station");
                    foreach (var so in stops)
                    {
                        var s = (Dictionary<string, object>)so;
                        lineStops.Add(new Dictionary<string, object> { { "x", P.Num(s, "x", 0) + offX }, { "z", P.Num(s, "z", 0) + offZ }, { "cs1Building", P.Num(s, "cs1Building", 0) } });
                    }
                }
                else
                {
                    j.Inc("bus");
                    foreach (var so in stops)
                    {
                        var s = (Dictionary<string, object>)so;
                        long segId = (long)P.Num(s, "segment", -1);
                        List<object> bz;
                        if (!segs.TryGetValue(segId, out bz))
                        {
                            j.Skipped.Add(new Dictionary<string, object> { { "line", line["name"] }, { "stop", P.Num(s, "cs1Node", 0) }, { "reason", "segment not in plan" } });
                            continue;
                        }
                        double t = P.Num(s, "t", 0.5);
                        string side = P.Str(s, "side", "right");
                        string key = segId + ":" + side + ":" + Convert.ToString(t, CultureInfo.InvariantCulture);
                        Dictionary<string, object> stop;
                        if (!byKey.TryGetValue(key, out stop))
                        {
                            double px, pz, dx, dz;
                            BezierAt(bz, t, out px, out pz, out dx, out dz);
                            double len = Math.Sqrt(dx * dx + dz * dz);
                            if (len == 0) len = 1;
                            string generic = P.Str(l, "stopPrefab", null) ?? "Bus Stop";
                            string prefab;
                            if (!stopNames.TryGetValue(generic, out prefab)) prefab = generic;
                            stop = new Dictionary<string, object>
                            {
                                { "key", key }, { "x", Math.Round(px + offX, 2) }, { "z", Math.Round(pz + offZ, 2) },
                                { "tx", Math.Round(dx / len, 4) }, { "tz", Math.Round(dz / len, 4) }, { "side", side }, { "prefab", prefab },
                                { "segment", (double)segId }, { "cs1Node", P.Num(s, "cs1Node", 0) },
                            };
                            byKey[key] = stop;
                            j.Stops.Add(stop);
                            j.Inc("kerbStops");
                        }
                        else j.Inc("sharedStops");
                        lineStops.Add(new Dictionary<string, object> { { "x", stop["x"] }, { "z", stop["z"] }, { "key", key } });
                    }
                }
                j.Lines.Add(line);
            }
            return j;
        }

        static void BezierAt(List<object> b, double t, out double px, out double pz, out double dx, out double dz)
        {
            double u = 1 - t;
            Func<int, int, double> c = (i, k) => Convert.ToDouble(((List<object>)b[i])[k], CultureInfo.InvariantCulture);
            px = u * u * u * c(0, 0) + 3 * u * u * t * c(1, 0) + 3 * u * t * t * c(2, 0) + t * t * t * c(3, 0);
            pz = u * u * u * c(0, 2) + 3 * u * u * t * c(1, 2) + 3 * u * t * t * c(2, 2) + t * t * t * c(3, 2);
            dx = 3 * u * u * (c(1, 0) - c(0, 0)) + 6 * u * t * (c(2, 0) - c(1, 0)) + 3 * t * t * (c(3, 0) - c(2, 0));
            dz = 3 * u * u * (c(1, 2) - c(0, 2)) + 6 * u * t * (c(2, 2) - c(1, 2)) + 3 * t * t * (c(3, 2) - c(2, 2));
        }
    }
}
