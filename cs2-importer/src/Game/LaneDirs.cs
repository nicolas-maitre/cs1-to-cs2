// Op "lanedirs": for every plan segment, the direction of each CS2 car lane, left → right in the plan segment's frame
// ("F" = travels start → end of the plan segment, "B" = end → start), with the edge's prefab. Compared offline with the
// CS1 lanes (netinfos.json m_direction, inverted in left-hand cities) to check that roads run the way they did in CS1
// (DECISIONS-importer.md #55).
using System;
using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Cs2Importer.Core;
using Game.Net;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Importer
{
    public static class LaneDirs
    {
        public static object Dump(Dictionary<string, object> a)
        {
            var json = System.IO.File.ReadAllText(Entry.PlanFile(a), System.Text.Encoding.UTF8);
            var job = Entry.LoadJob(a, json);
            var w = new GameWorld();
            var res = M2Ops.Resolve(w, job);
            var em = w.Em;
            var rows = new Dictionary<string, object>();
            int segs = 0, missing = 0;
            foreach (var s in job.Segments)
            {
                List<Entity> path;
                if (!res.Edges.TryGetValue(s.Id, out path) || path.Count == 0) { missing++; continue; }
                var e = path[path.Count / 2];
                var curve = em.GetComponentData<Curve>(e).m_Bezier;
                var mid = MathUtils.Position(curve, 0.5f);
                var tan = MathUtils.Tangent(curve, 0.5f);
                // the plan direction: from the plan segment's start node to its end node
                var dir = new float2((float)(s.End.X - s.Start.X), (float)(s.End.Z - s.Start.Z));
                bool sameDir = math.dot(tan.xz, dir) >= 0;
                var fwd = sameDir ? tan.xz : -tan.xz;
                var right = math.normalizesafe(new float2(fwd.y, -fwd.x));
                var lanes = new List<KeyValuePair<float, char>>();
                if (em.HasBuffer<Game.Net.SubLane>(e))
                {
                    var subs = em.GetBuffer<Game.Net.SubLane>(e, true);
                    for (int i = 0; i < subs.Length; i++)
                    {
                        var le = subs[i].m_SubLane;
                        if ((subs[i].m_PathMethods & Game.Pathfind.PathMethod.Road) == 0) continue;
                        if (!em.HasComponent<Game.Net.CarLane>(le) || !em.HasComponent<EdgeLane>(le) || em.HasComponent<Game.Net.SecondaryLane>(le) || em.HasComponent<MasterLane>(le)) continue;
                        // a bus stop's bay ("Car Bay Lane"): not a driving lane of CS1's road (#64; worker-5's IsBayLane rule)
                        if (w.Prefabs.GetPrefabName(em.GetComponentData<Game.Prefabs.PrefabRef>(le).m_Prefab).Contains("Bay Lane")) continue;
                        var delta = em.GetComponentData<EdgeLane>(le).m_EdgeDelta;
                        bool alongEdge = delta.y > delta.x;
                        var lc = em.GetComponentData<Curve>(le).m_Bezier;
                        float off = math.dot(MathUtils.Position(lc, 0.5f).xz - mid.xz, right);
                        lanes.Add(new KeyValuePair<float, char>(off, alongEdge == sameDir ? 'F' : 'B'));
                    }
                }
                lanes.Sort((x, y) => x.Key.CompareTo(y.Key));
                rows[s.Id.ToString()] = new string(lanes.Select(x => x.Value).ToArray()) + " " + w.Prefabs.GetPrefabName(em.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab);
                segs++;
            }
            bool? lht = null;
            try { lht = w.World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>().leftHandTraffic; } catch { }
            return new Dictionary<string, object> { { "op", "lanedirs" }, { "segments", segs }, { "missing", missing }, { "cs2_left_hand_traffic", lht }, { "lanes", rows } };
        }
    }
}
