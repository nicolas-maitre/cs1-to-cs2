// plan.transit → jobs for the transit worker's exec library: a C# port of transit/lib/plan.mjs transitJobs, used by the
// mod's transit step (mod/TransitStep.cs). Game-free, so CoreTests compares it with the Node original on golden data
// (tests/fixtures/transit-jobs.json, written by make-transit-golden.mjs): drift fails TransitPlanTests.
using System;
using System.Collections.Generic;
using System.Globalization;
using GameBridge;

namespace Cs2Importer.Core
{
    public sealed class TransitJobs
    {
        public readonly List<Dictionary<string, object>> Stops = new List<Dictionary<string, object>>();
        public readonly List<Dictionary<string, object>> Lines = new List<Dictionary<string, object>>();
        public readonly List<object> Skipped = new List<object>();
        public readonly Dictionary<string, object> Stats = new Dictionary<string, object> { { "lines", 0 }, { "bus", 0 }, { "station", 0 }, { "kerbStops", 0 }, { "sharedStops", 0 } };
        internal void Inc(string k) { Stats[k] = (int)Stats[k] + 1; }
    }

    public static class TransitPlan
    {
        /// <summary>The plan names generic stop prefabs; CS2's are per theme (EU_/NA_), as STOP_PREFABS in plan.mjs.</summary>
        public static readonly Dictionary<string, Dictionary<string, string>> StopPrefabs = new Dictionary<string, Dictionary<string, string>>
        {
            { "EU", new Dictionary<string, string> { { "Bus Stop", "EU_BusStop01" }, { "Tram Stop", "EU_TramStop01" } } },
            { "NA", new Dictionary<string, string> { { "Bus Stop", "NA_BusStop01" }, { "Tram Stop", "NA_TramStop01" } } },
        };

        /// <summary>
        /// transitJobs(plan, {theme}), plus the importer's test placement offset. Kerbside stops are keyed by
        /// (segment, side, t), so lines that share a CS1 stop share one CS2 stop; station lines keep the CS1 positions.
        /// </summary>
        public static TransitJobs BuildJobs(Dictionary<string, object> plan, string theme, double offX = 0, double offZ = 0)
        {
            var j = new TransitJobs();
            Dictionary<string, string> stopNames;
            if (!StopPrefabs.TryGetValue(theme ?? "EU", out stopNames)) stopNames = StopPrefabs["EU"];
            var lines = P.List(P.Obj(plan, "transit"), "lines");
            if (lines == null || lines.Count == 0) return j;
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
            foreach (var lo in lines)
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
                        string key = segId + ":" + side + ":" + JsNumber(t);
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
                                { "key", key }, { "x", R(px, 100) + offX }, { "z", R(pz, 100) + offZ },
                                { "tx", R(dx / len, 1e4) }, { "tz", R(dz / len, 1e4) }, { "side", side }, { "prefab", prefab },
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

        /// <summary>Math.round(v * f) / f, as JavaScript rounds (half up, not banker's).</summary>
        static double R(double v, double f) { return Math.Floor(v * f + 0.5) / f; }

        /// <summary>A number as JavaScript's String(n) writes it (shortest round-trip form), for the stop keys.</summary>
        static string JsNumber(double v)
        {
            if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString(CultureInfo.InvariantCulture);
            return v.ToString("R", CultureInfo.InvariantCulture);
        }

        public static void BezierAt(List<object> b, double t, out double px, out double pz, out double dx, out double dz)
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
