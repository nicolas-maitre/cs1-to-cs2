// Op "lanes": TM:PE lane connections and lane arrows → the Traffic mod's lane connections (Paradox Mods 80095, 0.2.x), by
// reflection, in the mod's own data model (read from its IL, DECISIONS-importer.md #52):
//   node:  ModifiedConnections (tag) + buffer ModifiedLaneConnections { laneIndex, carriagewayAndGroup, lanePosition,
//          edgeEntity (source edge), modifiedConnections (entity) }, one element per source lane;
//   that entity: DataOwner { entity = node }, PrefabRef (ModDefaultsSystem.FakePrefabRef), buffer GeneratedConnection
//          { sourceEntity, targetEntity, laneIndexMap, carriagewayAndGroupIndexMap, lanePositionMap, method, isUnsafe };
//   then Updated on the node and its edges, and the mod generates the lanes.
// This is the recipe of the mod's own TLEDataMigrationSystem (which turns lane directions into connections). Positions
// and carriageway/group come from the mod's NetUtils.GetAdditionalLaneDetails.
//
// Plan lanes (schema §2 "Lane references") are matched by ordinal among the CS2 edge's car lanes, left → right in the
// plan segment's direction; the count must match. Arrows become connections: the junction's existing CS2 lanes from
// that source whose turn (CarLane turn flags) the arrows allow. Explicit connections win over arrows (TM:PE).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Colossal.Mathematics;
using Cs2Importer.Core;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using GameBridge;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Importer
{
    public static class LaneOps
    {
        sealed class ModTypes
        {
            public Type ModifiedConnections, ModifiedLaneConnections, GeneratedConnection, DataOwner;
            public MethodInfo GetAdditionalLaneDetails;
            public Entity FakePrefab;
            public string Version;

            public static string Missing;

            public static ModTypes Find()
            {
                Missing = null;
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Traffic");
                if (asm == null) return null;
                var t = new ModTypes
                {
                    ModifiedConnections = asm.GetType("Traffic.Components.ModifiedConnections"),
                    ModifiedLaneConnections = asm.GetType("Traffic.Components.LaneConnections.ModifiedLaneConnections"),
                    GeneratedConnection = asm.GetType("Traffic.Components.LaneConnections.GeneratedConnection"),
                    DataOwner = asm.GetType("Traffic.Components.DataOwner"),
                    GetAdditionalLaneDetails = asm.GetType("Traffic.Systems.Helpers.NetUtils")?.GetMethod("GetAdditionalLaneDetails", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
                    Version = asm.GetName().Version.ToString(),
                };
                var fake = asm.GetType("Traffic.Systems.ModDefaultsSystem")?.GetField("FakePrefabRef", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var missing = new List<string>();
                if (t.ModifiedConnections == null) missing.Add("ModifiedConnections");
                if (t.ModifiedLaneConnections == null) missing.Add("ModifiedLaneConnections");
                if (t.GeneratedConnection == null) missing.Add("GeneratedConnection");
                if (t.DataOwner == null) missing.Add("DataOwner");
                if (t.GetAdditionalLaneDetails == null) missing.Add("NetUtils.GetAdditionalLaneDetails");
                if (fake == null) missing.Add("ModDefaultsSystem.FakePrefabRef");
                if (missing.Count > 0) { Missing = string.Join(", ", missing.ToArray()); return null; }
                t.FakePrefab = (Entity)fake.GetValue(null);
                return t;
            }
        }

        struct LaneRef { public long Segment; public int Ordinal, Count; public string Dir; }

        sealed class Source
        {
            public Entity Node, Edge;
            public bool IsEnd;
            public int LaneIndex;
            public readonly List<KeyValuePair<Entity, int>> Targets = new List<KeyValuePair<Entity, int>>(); // explicit connections
            public HashSet<string> Arrows;                                                                  // or arrows
        }

        public static IEnumerator Apply(Dictionary<string, object> a)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var json = System.IO.File.ReadAllText(Entry.PlanFile(a), System.Text.Encoding.UTF8);
            var root = (Dictionary<string, object>)MiniJson.Parse(json);
            var traffic = P.Obj(root, "traffic");
            var conns = P.List(traffic, "laneConnections") ?? new List<object>();
            var arrows = P.List(traffic, "laneArrows") ?? new List<object>();
            if (conns.Count == 0 && arrows.Count == 0) { yield return new Dictionary<string, object> { { "op", "lanes" }, { "skipped", "the plan has no lane connections or lane arrows" } }; yield break; }
            var mod = ModTypes.Find();
            if (mod == null) { yield return new Dictionary<string, object> { { "op", "lanes" }, { "skipped", ModTypes.Missing == null ? "the Traffic mod (Paradox Mods 80095) is not loaded" : "the Traffic mod's lane connection types changed (missing: " + ModTypes.Missing + ")" } }; yield break; }

            var w = new GameWorld();
            var job = Entry.LoadJob(a, json);
            var res = M2Ops.Resolve(w, job);
            var em = w.Em;
            var problems = new Dictionary<string, int>();
            Action<string> bad = k => { int n; problems.TryGetValue(k, out n); problems[k] = n + 1; };

            // plan lane → (edge, lane index at that node)
            var sources = new Dictionary<string, Source>();
            Func<long, Dictionary<string, object>, bool, Source> resolve = (node, lr, inbound) =>
            {
                var r = new LaneRef { Segment = (long)P.Num(lr, "segment", -1), Ordinal = (int)P.Num(lr, "ordinal", -1), Count = (int)P.Num(lr, "count", -1), Dir = P.Str(lr, "dir", "forward") };
                bool isEnd;
                var edge = res.EdgeAt(w, r.Segment, node, out isEnd);
                if (edge == Entity.Null) { bad("segment_or_node_not_found"); return null; }
                int idx = LaneIndex(w, res, job, edge, isEnd, node, r, inbound, bad);
                if (idx < 0) return null;
                return new Source { Node = res.Node[node], Edge = edge, IsEnd = isEnd, LaneIndex = idx };
            };
            foreach (var o in conns)
            {
                var c = (Dictionary<string, object>)o;
                long node = (long)P.Num(c, "node", -1);
                var src = resolve(node, P.Obj(c, "from"), true);
                var tgt = resolve(node, P.Obj(c, "to"), false);
                if (src == null || tgt == null) continue;
                var key = src.Node.Index + ":" + src.Edge.Index + ":" + src.LaneIndex;
                Source s;
                if (!sources.TryGetValue(key, out s)) sources[key] = s = src;
                s.Targets.Add(new KeyValuePair<Entity, int>(tgt.Edge, tgt.LaneIndex));
            }
            int arrowsOverridden = 0;
            foreach (var o in arrows)
            {
                var c = (Dictionary<string, object>)o;
                long node = (long)P.Num(c, "node", -1);
                var src = resolve(node, P.Obj(c, "lane"), true);
                if (src == null) continue;
                var key = src.Node.Index + ":" + src.Edge.Index + ":" + src.LaneIndex;
                Source s;
                if (sources.TryGetValue(key, out s)) { if (s.Targets.Count > 0) { arrowsOverridden++; continue; } }
                else sources[key] = s = src;
                s.Arrows = new HashSet<string>((P.List(c, "arrows") ?? new List<object>()).Cast<string>());
            }

            // per node: the GeneratedConnection rows of each source lane
            // lookups from any managed system (EntityManager has no lookup getters in this Entities version)
            var sys = w.World.GetOrCreateSystemManaged<Game.Tools.ToolSystem>();
            var compositionData = sys.GetComponentLookup<Composition>(true);
            var compositionLanes = sys.GetBufferLookup<NetCompositionLane>(true);
            object boxedComp = compositionData, boxedLanes = compositionLanes;
            var byNode = sources.Values.GroupBy(s => s.Node).ToList();
            int nodesDone = 0, nodesSkipped = 0, connections = 0, sourcesDone = 0, sourcesEmpty = 0;
            var written = new List<Entity>();
            foreach (var g in byNode)
            {
                var node = g.Key;
                if (!w.IsReal(node)) { nodesSkipped++; continue; }
                if (HasComp(em, node, mod.ModifiedConnections)) { nodesSkipped++; bad("node_already_has_lane_connections"); continue; }
                var nodeLanes = NodeCarLanes(w, node);
                var elements = new List<object>();
                foreach (var s in g)
                {
                    var rows = new List<object>();
                    if (s.Targets.Count > 0)
                    {
                        var method = nodeLanes.Where(l => l.SrcEdge == s.Edge && l.SrcIndex == s.LaneIndex).Select(l => l.Methods).FirstOrDefault();
                        if (method == 0) method = Game.Pathfind.PathMethod.Road;
                        foreach (var t in s.Targets)
                        {
                            bool tgtIsEnd = em.GetComponentData<Edge>(t.Key).m_End == node;
                            var row = Row(mod, s.Edge, t.Key, s.LaneIndex, t.Value, s.IsEnd, tgtIsEnd, method, false, ref boxedComp, ref boxedLanes);
                            if (row != null) rows.Add(row); else bad("lane_details_not_found");
                        }
                    }
                    else if (s.Arrows != null)
                    {
                        foreach (var l in nodeLanes.Where(l => l.SrcEdge == s.Edge && l.SrcIndex == s.LaneIndex && Allowed(l.Flags, s.Arrows)))
                        {
                            bool tgtIsEnd = em.GetComponentData<Edge>(l.TgtEdge).m_End == node;
                            var row = Row(mod, s.Edge, l.TgtEdge, s.LaneIndex, l.TgtIndex, s.IsEnd, tgtIsEnd, l.Methods, (l.Flags & CarLaneFlags.Unsafe) != 0, ref boxedComp, ref boxedLanes);
                            if (row != null) rows.Add(row);
                        }
                    }
                    if (rows.Count == 0) { sourcesEmpty++; continue; }
                    // the connections entity (DataOwner + PrefabRef + GeneratedConnection buffer)
                    var ce = em.CreateEntity();
                    var owner = Activator.CreateInstance(mod.DataOwner);
                    mod.DataOwner.GetField("entity").SetValue(owner, node);
                    AddComponentData(em, ce, mod.DataOwner, owner);
                    em.AddComponentData(ce, new PrefabRef { m_Prefab = mod.FakePrefab });
                    var buf = AddBuffer(em, ce, mod.GeneratedConnection);
                    foreach (var r in rows) buf.GetType().GetMethod("Add").Invoke(buf, new[] { r });
                    written.Add(ce);
                    var first = rows[0];
                    var el = Activator.CreateInstance(mod.ModifiedLaneConnections);
                    Set(el, "laneIndex", s.LaneIndex);
                    Set(el, "carriagewayAndGroup", ((int4)Get(first, "carriagewayAndGroupIndexMap")).xy);
                    Set(el, "lanePosition", ((float3x2)Get(first, "lanePositionMap")).c0);
                    Set(el, "edgeEntity", s.Edge);
                    Set(el, "modifiedConnections", ce);
                    elements.Add(el);
                    connections += rows.Count;
                    sourcesDone++;
                }
                if (elements.Count == 0) continue;
                AddComponent(em, node, mod.ModifiedConnections);
                var nb = AddBuffer(em, node, mod.ModifiedLaneConnections);
                foreach (var el in elements) nb.GetType().GetMethod("Add").Invoke(nb, new[] { el });
                if (!em.HasComponent<Updated>(node)) em.AddComponent<Updated>(node);
                foreach (var e in w.EdgesOf(node)) if (w.IsReal(e) && !em.HasComponent<Updated>(e)) em.AddComponent<Updated>(e);
                nodesDone++;
            }
            for (int f = 0; f < 30; f++) yield return null;

            // check: after the mod regenerated the junction lanes, each source lane leads exactly to its connections
            int sourcesMatching = 0, sourcesChecked = 0;
            foreach (var g in byNode)
            {
                if (!w.IsReal(g.Key) || !HasComp(em, g.Key, mod.ModifiedConnections)) continue;
                var lanes = NodeCarLanes(w, g.Key);
                foreach (var s in g)
                {
                    if (s.Targets.Count == 0) continue;
                    sourcesChecked++;
                    var got = new HashSet<string>(lanes.Where(l => l.SrcEdge == s.Edge && l.SrcIndex == s.LaneIndex).Select(l => l.TgtEdge.Index + ":" + l.TgtIndex));
                    var want = new HashSet<string>(s.Targets.Select(t => t.Key.Index + ":" + t.Value));
                    if (got.SetEquals(want)) sourcesMatching++;
                }
            }
            yield return new Dictionary<string, object>
            {
                { "op", "lanes" }, { "traffic_mod", mod.Version },
                { "plan_connections", conns.Count }, { "plan_arrows", arrows.Count }, { "arrows_overridden_by_connections", arrowsOverridden },
                { "nodes_planned", byNode.Count }, { "nodes_done", nodesDone }, { "nodes_skipped", nodesSkipped },
                { "source_lanes", sourcesDone }, { "source_lanes_without_connections", sourcesEmpty }, { "connections", connections },
                { "explicit_sources_checked", sourcesChecked }, { "explicit_sources_matching", sourcesMatching },
                { "problems", problems.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "seconds", Entry.R(sw.Elapsed.TotalSeconds) },
            };
        }

        static bool Allowed(CarLaneFlags f, HashSet<string> arrows)
        {
            bool left = (f & (CarLaneFlags.TurnLeft | CarLaneFlags.GentleTurnLeft | CarLaneFlags.UTurnLeft)) != 0;
            bool right = (f & (CarLaneFlags.TurnRight | CarLaneFlags.GentleTurnRight | CarLaneFlags.UTurnRight)) != 0;
            if (left) return arrows.Contains("left");
            if (right) return arrows.Contains("right");
            return arrows.Contains("straight");
        }

        sealed class NodeLane { public Entity SrcEdge, TgtEdge; public int SrcIndex, TgtIndex; public CarLaneFlags Flags; public Game.Pathfind.PathMethod Methods; }

        /// <summary>The junction's car lanes (no master lanes): source/target edge and lane index, turn flags.</summary>
        static List<NodeLane> NodeCarLanes(GameWorld w, Entity node)
        {
            var em = w.Em;
            var r = new List<NodeLane>();
            if (!em.HasBuffer<Game.Net.SubLane>(node)) return r;
            var edges = w.EdgesOf(node);
            var subs = em.GetBuffer<Game.Net.SubLane>(node, true);
            for (int i = 0; i < subs.Length; i++)
            {
                var l = subs[i].m_SubLane;
                if (!em.HasComponent<Game.Net.CarLane>(l) || em.HasComponent<MasterLane>(l) || !em.HasComponent<Lane>(l)) continue;
                var lane = em.GetComponentData<Lane>(l);
                Entity src = Entity.Null, tgt = Entity.Null;
                foreach (var e in edges)
                {
                    if (lane.m_StartNode.OwnerEquals(new Game.Pathfind.PathNode(e, 0))) src = e;
                    if (lane.m_EndNode.OwnerEquals(new Game.Pathfind.PathNode(e, 0))) tgt = e;
                }
                if (src == Entity.Null || tgt == Entity.Null) continue;
                var cl = em.GetComponentData<Game.Net.CarLane>(l);
                if ((cl.m_Flags & CarLaneFlags.Forbidden) != 0) continue;
                r.Add(new NodeLane { SrcEdge = src, TgtEdge = tgt, SrcIndex = lane.m_StartNode.GetLaneIndex() & 0xff, TgtIndex = lane.m_EndNode.GetLaneIndex() & 0xff, Flags = cl.m_Flags, Methods = subs[i].m_PathMethods });
            }
            return r;
        }

        /// <summary>
        /// The CS2 lane index (at the node end) of a plan lane reference: the edge's car lanes sorted left → right in the
        /// plan segment's direction, the ordinal-th one; inbound lanes end at the node, outbound ones start there.
        /// </summary>
        static int LaneIndex(GameWorld w, M2Ops.Resolved res, ImportJob job, Entity edge, bool isEnd, long planNode, LaneRef r, bool inbound, Action<string> bad)
        {
            var em = w.Em;
            var seg = job.Segments.FirstOrDefault(s => s.Id == r.Segment);
            if (seg == null || !em.HasBuffer<Game.Net.SubLane>(edge)) { bad("segment_not_in_job"); return -1; }
            // plan forward = edge forward when the node is the plan segment's end and the edge's end (or start and start)
            bool sameDir = (planNode == seg.End.Id) == isEnd;
            var curve = em.GetComponentData<Curve>(edge).m_Bezier;
            float t = isEnd ? 1f : 0f;
            var at = MathUtils.Position(curve, t);
            var tan = MathUtils.Tangent(curve, t);
            if (!sameDir) tan = -tan;
            var right = math.normalizesafe(new float2(tan.z, -tan.x));
            var lanes = new List<KeyValuePair<float, int>>();
            var subs = em.GetBuffer<Game.Net.SubLane>(edge, true);
            float target = isEnd ? 1f : 0f;
            int inboundMatches = 0;
            for (int i = 0; i < subs.Length; i++)
            {
                var sl = subs[i];
                if ((sl.m_PathMethods & Game.Pathfind.PathMethod.Road) == 0) continue;
                var le = sl.m_SubLane;
                if (!em.HasComponent<Game.Net.CarLane>(le) || !em.HasComponent<EdgeLane>(le) || !em.HasComponent<Lane>(le) || em.HasComponent<Game.Net.SecondaryLane>(le) || em.HasComponent<MasterLane>(le)) continue;
                var delta = em.GetComponentData<EdgeLane>(le).m_EdgeDelta;
                bool endsHere = math.abs(delta.y - target) < 0.001f, startsHere = math.abs(delta.x - target) < 0.001f;
                if (!endsHere && !startsHere) continue;
                var lane = em.GetComponentData<Lane>(le);
                var lc = em.GetComponentData<Curve>(le).m_Bezier;
                var p = endsHere ? lc.d : lc.a;
                float off = math.dot((p - at).xz, right);
                int idx = (endsHere ? lane.m_EndNode : lane.m_StartNode).GetLaneIndex() & 0xff;
                lanes.Add(new KeyValuePair<float, int>(off, idx * 2 + (endsHere ? 1 : 0)));
                if (endsHere) inboundMatches++;
            }
            lanes.Sort((x, y) => x.Key.CompareTo(y.Key));
            if (lanes.Count != r.Count) { bad("lane_count_differs"); return -1; }
            if (r.Ordinal < 0 || r.Ordinal >= lanes.Count) { bad("ordinal_out_of_range"); return -1; }
            var hit = lanes[r.Ordinal].Value;
            bool hitInbound = (hit & 1) == 1;
            if (hitInbound != inbound) { bad(inbound ? "source_lane_leaves_the_node" : "target_lane_enters_the_node"); return -1; }
            return hit >> 1;
        }

        static object Row(ModTypes mod, Entity src, Entity tgt, int srcIdx, int tgtIdx, bool srcIsEnd, bool tgtIsEnd, Game.Pathfind.PathMethod method, bool unsafeLane, ref object comp, ref object lanes)
        {
            var args = new object[] { src, tgt, new int2(srcIdx, tgtIdx), new bool2(srcIsEnd, tgtIsEnd), comp, lanes, default(float3x2), default(int4) };
            if (!(bool)mod.GetAdditionalLaneDetails.Invoke(null, args)) return null;
            comp = args[4]; lanes = args[5];
            var row = Activator.CreateInstance(mod.GeneratedConnection);
            Set(row, "sourceEntity", src);
            Set(row, "targetEntity", tgt);
            Set(row, "laneIndexMap", new int2(srcIdx, tgtIdx));
            Set(row, "carriagewayAndGroupIndexMap", (int4)args[7]);
            Set(row, "lanePositionMap", (float3x2)args[6]);
            Set(row, "method", method);
            Set(row, "isUnsafe", unsafeLane);
            return row;
        }

        static void Set(object boxed, string field, object v) { boxed.GetType().GetField(field).SetValue(boxed, v); }
        static object Get(object boxed, string field) { return boxed.GetType().GetField(field).GetValue(boxed); }

        static bool HasComp(EntityManager em, Entity e, Type t)
        {
            var m = typeof(EntityManager).GetMethods().First(x => x.Name == "HasComponent" && x.IsGenericMethodDefinition && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(Entity)).MakeGenericMethod(t);
            return (bool)m.Invoke(em, new object[] { e });
        }

        static void AddComponent(EntityManager em, Entity e, Type t)
        {
            var m = typeof(EntityManager).GetMethods().First(x => x.Name == "AddComponent" && x.IsGenericMethodDefinition && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(Entity)).MakeGenericMethod(t);
            m.Invoke(em, new object[] { e });
        }

        static void AddComponentData(EntityManager em, Entity e, Type t, object value)
        {
            var m = typeof(EntityManager).GetMethods().First(x => x.Name == "AddComponentData" && x.IsGenericMethodDefinition && x.GetParameters().Length == 2 && x.GetParameters()[0].ParameterType == typeof(Entity)).MakeGenericMethod(t);
            m.Invoke(em, new object[] { e, value });
        }

        static object AddBuffer(EntityManager em, Entity e, Type t)
        {
            var m = typeof(EntityManager).GetMethods().First(x => x.Name == "AddBuffer" && x.IsGenericMethodDefinition && x.GetParameters().Length == 1).MakeGenericMethod(t);
            return m.Invoke(em, new object[] { e });
        }
    }
}
