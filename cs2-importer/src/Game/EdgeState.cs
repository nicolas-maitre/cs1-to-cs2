// Op "edgestate": the state of plan segments' CS2 edges, to compare a city before saving with the same city after
// reloading (DECISIONS-importer.md #64): prefab, composition (entity, flags, lane count), Upgraded flags, and every
// road sub-lane with the components that decide whether lane checks count it (MasterLane, SlaveLane, SecondaryLane).
//   args: plan, segments (ids; default all), lanes (true: list sub-lanes)
using System;
using System.Collections.Generic;
using System.Linq;
using Game.Net;
using PrefabRef = Game.Prefabs.PrefabRef;
using NetCompositionData = Game.Prefabs.NetCompositionData;
using NetCompositionLane = Game.Prefabs.NetCompositionLane;
using GameBridge;
using Unity.Entities;

namespace Cs2Importer
{
    public static class EdgeState
    {
        public static object Dump(Dictionary<string, object> a)
        {
            var job = Entry.LoadJob(a, System.IO.File.ReadAllText(Entry.PlanFile(a), System.Text.Encoding.UTF8));
            var w = new GameWorld();
            var res = M2Ops.Resolve(w, job);
            var em = w.Em;
            var want = P.List(a, "segments");
            var ids = want != null ? new HashSet<long>(want.Select(x => Convert.ToInt64(x))) : null;
            bool withLanes = P.Bool(a, "lanes", true);
            var rows = new Dictionary<string, object>();
            int missing = 0;
            foreach (var s in job.Segments)
            {
                if (ids != null && !ids.Contains(s.Id)) continue;
                if (rows.ContainsKey(s.Id.ToString())) continue;
                List<Entity> path;
                if (!res.Edges.TryGetValue(s.Id, out path) || path.Count == 0) { missing++; continue; }
                var edges = new List<object>();
                foreach (var e in path)
                {
                    var row = new Dictionary<string, object>
                    {
                        { "edge", e.Index }, { "prefab", w.Prefabs.GetPrefabName(em.GetComponentData<PrefabRef>(e).m_Prefab) },
                        { "length", Math.Round(em.GetComponentData<Curve>(e).m_Length, 2) },
                    };
                    if (em.HasComponent<Composition>(e))
                    {
                        var c = em.GetComponentData<Composition>(e).m_Edge;
                        row["composition"] = c.Index;
                        if (c != Entity.Null && em.HasComponent<NetCompositionData>(c))
                        {
                            var cd = em.GetComponentData<NetCompositionData>(c);
                            row["comp_flags"] = cd.m_Flags.m_General + " | L " + cd.m_Flags.m_Left + " | R " + cd.m_Flags.m_Right;
                            row["comp_width"] = Math.Round(cd.m_Width, 2);
                        }
                        if (c != Entity.Null && em.HasBuffer<NetCompositionLane>(c)) row["comp_lanes"] = em.GetBuffer<NetCompositionLane>(c, true).Length;
                    }
                    if (em.HasComponent<Upgraded>(e))
                    {
                        var u = em.GetComponentData<Upgraded>(e).m_Flags;
                        row["upgraded"] = u.m_General + " | L " + u.m_Left + " | R " + u.m_Right;
                    }
                    if (em.HasComponent<Game.Net.Elevation>(e)) row["elevation"] = em.GetComponentData<Game.Net.Elevation>(e).m_Elevation.ToString();
                    if (withLanes && em.HasBuffer<SubLane>(e))
                    {
                        var lanes = new List<object>();
                        var subs = em.GetBuffer<SubLane>(e, true);
                        for (int i = 0; i < subs.Length; i++)
                        {
                            var le = subs[i].m_SubLane;
                            if ((subs[i].m_PathMethods & Game.Pathfind.PathMethod.Road) == 0 || !em.HasComponent<CarLane>(le)) continue;
                            var tags = new List<string>();
                            if (em.HasComponent<MasterLane>(le)) tags.Add("master");
                            if (em.HasComponent<SlaveLane>(le)) tags.Add("slave");
                            if (em.HasComponent<SecondaryLane>(le)) tags.Add("secondary");
                            if (em.HasComponent<EdgeLane>(le)) { var dl = em.GetComponentData<EdgeLane>(le).m_EdgeDelta; tags.Add("delta " + Math.Round(dl.x, 2) + ">" + Math.Round(dl.y, 2)); }
                            var lane = em.GetComponentData<Lane>(le);
                            tags.Add("idx " + (lane.m_StartNode.GetLaneIndex() & 0xff) + "/" + (lane.m_EndNode.GetLaneIndex() & 0xff));
                            tags.Add(em.GetComponentData<CarLane>(le).m_Flags.ToString());
                            tags.Add("prefab " + w.Prefabs.GetPrefabName(em.GetComponentData<PrefabRef>(le).m_Prefab));
                            lanes.Add(string.Join("; ", tags.ToArray()));
                        }
                        row["car_lanes"] = lanes;
                    }
                    edges.Add(row);
                }
                rows[s.Id.ToString()] = edges;
            }
            return new Dictionary<string, object> { { "op", "edgestate" }, { "segments", rows.Count }, { "missing", missing }, { "edges", rows } };
        }
    }
}
