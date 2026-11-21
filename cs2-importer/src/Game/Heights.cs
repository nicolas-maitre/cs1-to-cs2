// Op "heights": for every plan segment, the CS2 road's height along its course next to CS1's (DECISIONS-importer.md #57):
// the CS2 edge(s) found for the segment are sampled at five points of the whole course (t = 0, .25, .5, .75, 1 from the
// plan start to the plan end), with the CS2 terrain height under each point and the CS1 height there (the plan
// bezier's y, absolute: on converted terrain CS1 and CS2 heights share a datum). Read-only.
//   args: plan (+ offsets), out (file; the result is returned too when small)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Colossal.Mathematics;
using Cs2Importer.Core;
using Game.Net;
using GameBridge;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Importer
{
    public static class Heights
    {
        public static object Dump(Dictionary<string, object> a)
        {
            var json = File.ReadAllText(Entry.PlanFile(a), System.Text.Encoding.UTF8);
            var job = Entry.LoadJob(a, json);
            var w = new GameWorld();
            w.RefreshTerrain();
            var res = M2Ops.Resolve(w, job);
            var em = w.Em;
            var rows = new List<object>();
            int missing = 0;
            foreach (var s in job.Segments)
            {
                List<Entity> path;
                if (!res.Edges.TryGetValue(s.Id, out path) || path.Count == 0) { missing++; continue; }
                // the course as polyline points from the plan start to the plan end
                var pts = new List<float3>();
                Entity cur;
                if (!res.Node.TryGetValue(s.Start.Id, out cur)) { missing++; continue; }
                foreach (var e in path)
                {
                    var ed = em.GetComponentData<Edge>(e);
                    var c = em.GetComponentData<Curve>(e).m_Bezier;
                    bool fwd = ed.m_Start == cur;
                    for (int i = 0; i <= 16; i++)
                    {
                        float t = i / 16f;
                        pts.Add(MathUtils.Position(c, fwd ? t : 1 - t));
                    }
                    cur = fwd ? ed.m_End : ed.m_Start;
                }
                // arc-length samples
                var cum = new List<float> { 0 };
                for (int i = 1; i < pts.Count; i++) cum.Add(cum[i - 1] + math.distance(pts[i - 1].xz, pts[i].xz));
                float total = cum[cum.Count - 1];
                var b = s.Bezier;
                var samples = new List<object>();
                foreach (var f in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                {
                    float target = f * total;
                    int k = cum.FindIndex(x => x >= target);
                    if (k <= 0) k = 1;
                    float u = cum[k] - cum[k - 1] > 1e-4f ? (target - cum[k - 1]) / (cum[k] - cum[k - 1]) : 0;
                    var p = math.lerp(pts[k - 1], pts[k], math.saturate(u));
                    double cs1 = b != null ? Cubic(b[0].Y, b[1].Y, b[2].Y, b[3].Y, f) : s.Start.Cs1Y + (s.End.Cs1Y - s.Start.Cs1Y) * f;
                    samples.Add(new List<object> { Math.Round(p.y, 2), Math.Round(w.TerrainHeight(p.x, p.z), 2), Math.Round(cs1, 2) });
                }
                var se = path[0];
                rows.Add(new Dictionary<string, object>
                {
                    { "id", s.Id }, { "kind", s.Source.Kind }, { "prefab", s.PrefabKey }, { "edges", path.Count }, { "length", Math.Round(total, 1) },
                    { "elev", new List<object> { s.Start.Elevation, s.End.Elevation } }, { "hat", new List<object> { s.Start.Cs1HeightAboveTerrain, s.End.Cs1HeightAboveTerrain } },
                    { "samples", samples }, // [cs2 y, cs2 terrain, cs1 y] at t = 0 .25 .5 .75 1
                    { "elevated", em.HasComponent<Game.Net.Elevation>(se) },
                });
            }
            var doc = new Dictionary<string, object> { { "op", "heights" }, { "segments", rows.Count }, { "missing", missing }, { "rows", rows } };
            // CS2 street names (they're CS2's own, generated per road aggregate): where are the roads the user names?
            if (P.Has(a, "names")) doc["named"] = Named(w, new System.Text.RegularExpressions.Regex(P.Str(a, "names", ""), System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            var outFile = P.Str(a, "out", null);
            if (outFile != null)
            {
                File.WriteAllText(outFile, MiniJson.Write(doc), new System.Text.UTF8Encoding(false));
                return new Dictionary<string, object> { { "op", "heights" }, { "segments", rows.Count }, { "missing", missing }, { "out", outFile }, { "named", doc.ContainsKey("named") ? doc["named"] : null } };
            }
            return doc;
        }

        static List<object> Named(GameWorld w, System.Text.RegularExpressions.Regex re)
        {
            var em = w.Em;
            var names = w.World.GetOrCreateSystemManaged<Game.UI.NameSystem>();
            var found = new Dictionary<string, List<float3>>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Game.Net.Aggregated>(), ComponentType.ReadOnly<Curve>()))
            using (var es = q.ToEntityArray(Unity.Collections.Allocator.Temp))
                foreach (var e in es)
                {
                    var agg = em.GetComponentData<Game.Net.Aggregated>(e).m_Aggregate;
                    if (agg == Entity.Null) continue;
                    string n;
                    try { n = names.GetRenderedLabelName(agg); } catch { continue; }
                    if (string.IsNullOrEmpty(n) || !re.IsMatch(n)) continue;
                    List<float3> l;
                    if (!found.TryGetValue(n, out l)) found[n] = l = new List<float3>();
                    l.Add(MathUtils.Position(em.GetComponentData<Curve>(e).m_Bezier, 0.5f));
                }
            return found.Select(kv => (object)new Dictionary<string, object>
            {
                { "name", kv.Key }, { "edges", kv.Value.Count },
                { "x", Math.Round(kv.Value.Average(p => p.x)) }, { "z", Math.Round(kv.Value.Average(p => p.z)) },
                { "minY", Math.Round(kv.Value.Min(p => p.y), 1) }, { "maxY", Math.Round(kv.Value.Max(p => p.y), 1) },
            }).ToList();
        }

        static double Cubic(double a, double b, double c, double d, double t)
        {
            double u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }
    }
}
