// Guard for the steps that build utility nets (DECISIONS.md #145): a snapshot of every road edge's start and end node
// before the step, compared after it. A utility line must never create a node at a road node or take over a road edge
// (the old line-end snapping split junctions: two nodes, traffic cut).
using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace Cs2Buildings
{
    public sealed class RoadGuard
    {
        readonly Dictionary<Entity, Edge> before = new Dictionary<Entity, Edge>();

        public static RoadGuard Take(EntityManager em)
        {
            var g = new RoadGuard();
            foreach (var kv in RoadEdges(em)) g.before[kv.Key] = kv.Value;
            return g;
        }

        public int Count { get { return before.Count; } }

        /// <summary>Road edges that are gone or whose start / end node changed since Take, as report lines (max 20 samples).</summary>
        public Dictionary<string, object> Check(EntityManager em, out int changed)
        {
            var now = RoadEdges(em);
            changed = 0;
            int gone = 0, rewired = 0;
            var sample = new List<object>();
            foreach (var kv in before)
            {
                Edge e;
                if (!now.TryGetValue(kv.Key, out e)) { gone++; changed++; if (sample.Count < 20) sample.Add("road edge " + kv.Key.Index + " gone"); continue; }
                if (e.m_Start != kv.Value.m_Start || e.m_End != kv.Value.m_End)
                {
                    rewired++; changed++;
                    if (sample.Count < 20) sample.Add("road edge " + kv.Key.Index + " nodes " + kv.Value.m_Start.Index + "-" + kv.Value.m_End.Index + " → " + e.m_Start.Index + "-" + e.m_End.Index);
                }
            }
            return new Dictionary<string, object> { { "roadEdges", before.Count }, { "after", now.Count }, { "gone", gone }, { "rewired", rewired }, { "ok", changed == 0 }, { "sample", sample } };
        }

        /// <summary>
        /// Marks Updated every road edge with a utility node (one of `utilityPrefabs`) connected in its middle
        /// (ConnectedNode: a CS2 local connection), so the session regenerates its lanes the way a reload does
        /// (worker-3: a road's lanes were split at the connection in session and whole after reload). Returns the count.
        /// </summary>
        public static int TouchLocallyConnected(EntityManager em, HashSet<Entity> utilityPrefabs)
        {
            int n = 0;
            foreach (var kv in RoadEdges(em))
            {
                var e = kv.Key;
                if (!em.HasBuffer<ConnectedNode>(e) || em.HasComponent<Updated>(e)) continue;
                var cn = em.GetBuffer<ConnectedNode>(e, true);
                bool hit = false;
                for (int i = 0; i < cn.Length && !hit; i++)
                    hit = em.HasComponent<PrefabRef>(cn[i].m_Node) && utilityPrefabs.Contains(em.GetComponentData<PrefabRef>(cn[i].m_Node).m_Prefab);
                if (!hit) continue;
                em.AddComponent<Updated>(e);
                n++;
            }
            return n;
        }

        static Dictionary<Entity, Edge> RoadEdges(EntityManager em)
        {
            var d = new Dictionary<Entity, Edge>();
            using (var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<Owner>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
                foreach (var e in es)
                {
                    var pr = em.GetComponentData<PrefabRef>(e).m_Prefab;
                    if (em.HasComponent<NetData>(pr) && (em.GetComponentData<NetData>(pr).m_RequiredLayers & Layer.Road) != 0) d[e] = em.GetComponentData<Edge>(e);
                }
            return d;
        }
    }
}
