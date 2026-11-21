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

        sealed class Source
        {
            public long PlanNode;
            public Entity Node, Edge;
            public bool IsEnd;
            public int LaneIndex;
            public string Ref;                                                                                     // plan reference, for the report
            public long Segment;
            public readonly List<KeyValuePair<Entity, int>> Targets = new List<KeyValuePair<Entity, int>>();      // explicit connections
            public readonly List<string> TargetRefs = new List<string>();
            public readonly List<double[][]> WantCurves = new List<double[][]>();                               // CS1 target arm curves from the node (turn check)
            public HashSet<string> Arrows;                                                                         // or arrows (all three for U-turn-only)
            public Entity NoUTurnTo = Entity.Null;                                                                 // U-turn ban: no lane back into this edge
            public readonly List<KeyValuePair<Entity, int>> Extra = new List<KeyValuePair<Entity, int>>();        // U-turn allowed: added to the arrows' lanes
            public string Key { get { return Node.Index + ":" + Edge.Index + ":" + LaneIndex; } }
        }

        static readonly string[] AllArrows = { "left", "straight", "right" };

        /// <summary>
        /// args: plan (+ offsets), uturns (default true), checkOnly (only compare the junctions' lanes with the plan, e.g.
        /// after a save/load), diagnostics (max mismatches listed, default 40).
        /// </summary>
        public static IEnumerator Apply(Dictionary<string, object> a)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var json = System.IO.File.ReadAllText(Entry.PlanFile(a), System.Text.Encoding.UTF8);
            var root = (Dictionary<string, object>)MiniJson.Parse(json);
            var traffic = P.Obj(root, "traffic");
            var conns = P.List(traffic, "laneConnections") ?? new List<object>();
            var arrows = P.List(traffic, "laneArrows") ?? new List<object>();
            bool doUturns = P.Bool(a, "uturns", true), checkOnly = P.Bool(a, "checkOnly", false);
            int maxDiag = (int)P.Num(a, "diagnostics", 40);
            var uturnEnds = (P.List(traffic, "segmentEnds") ?? new List<object>()).Cast<Dictionary<string, object>>().Where(e => doUturns && e.ContainsKey("uturn") && e["uturn"] is bool).ToList();
            if (conns.Count == 0 && arrows.Count == 0 && uturnEnds.Count == 0) { yield return new Dictionary<string, object> { { "op", "lanes" }, { "skipped", "the plan has no lane connections, lane arrows or U-turn rules" } }; yield break; }
            var mod = ModTypes.Find();
            if (mod == null) { yield return new Dictionary<string, object> { { "op", "lanes" }, { "skipped", ModTypes.Missing == null ? "the Traffic mod (Paradox Mods 80095) is not loaded" : "the Traffic mod's lane connection types changed (missing: " + ModTypes.Missing + ")" } }; yield break; }

            var w = new GameWorld();
            var job = Entry.LoadJob(a, json);
            var res = M2Ops.Resolve(w, job);
            var em = w.Em;
            var problems = new Dictionary<string, int>();
            Action<string> bad = k => { int n; problems.TryGetValue(k, out n); problems[k] = n + 1; };

            // independent checks (#60, DECISIONS.md #126), using geometry instead of our own segment → edge → lane mapping:
            // (1) arm: the CS2 edge's direction at the node must be the CS1 segment's (bezier tangent) within armTolerance;
            //     a junction with any arm off gets nothing written (a wrong connection is worse than CS2's default);
            // (2) lane position: the lane nearest the CS1 lane's position across the road should be the one the ordinal
            //     picks (diagnostic only; measured with CS1's offset as is and mirrored)
            double armTol = P.Num(a, "armTolerance", 30);
            var unsure = new Dictionary<long, string>();
            var armSeen = new HashSet<string>();
            var armDiag = new List<object>();
            int armOk = 0, armOff = 0, armUnknown = 0;
            int posChecked = 0, posAgree = 0, posAgreeMirrored = 0;
            var posDiag = new List<object>();

            // plan lane → (edge, lane index at that node). First decide, over every reference, whether the plan's lanes
            // match CS2's as they are or mirrored (the CS1 city drove on the other side: LaneMatch.Mirror)
            bool mirrored = false;
            int refsTotal = 0, refsMatched = 0, okPlain = 0, okMirror = 0;
            {
                Func<long, Dictionary<string, object>, bool, bool, bool> fits = (node, lr, inbound, mir) =>
                {
                    long segment = (long)P.Num(lr, "segment", -1);
                    bool isEnd;
                    var edge = res.EdgeAt(w, segment, node, out isEnd);
                    if (edge == Entity.Null) return false;
                    var lanes = EdgeLanes(w, job, edge, isEnd, node, segment);
                    if (lanes == null) return false;
                    int ord = (int)P.Num(lr, "ordinal", -1), count = (int)P.Num(lr, "count", -1);
                    string pr;
                    return LaneMatch.Pick(lanes, mir ? LaneMatch.Mirror(ord, count) : ord, count, inbound, out pr) != null;
                };
                foreach (var o in conns)
                {
                    var c = (Dictionary<string, object>)o;
                    long node = (long)P.Num(c, "node", -1);
                    foreach (var side in new[] { true, false })
                    {
                        var lr = P.Obj(c, side ? "from" : "to");
                        if (fits(node, lr, side, false)) okPlain++;
                        if (fits(node, lr, side, true)) okMirror++;
                    }
                }
                foreach (var o in arrows)
                {
                    var c = (Dictionary<string, object>)o;
                    long node = (long)P.Num(c, "node", -1);
                    if (fits(node, P.Obj(c, "lane"), true, false)) okPlain++;
                    if (fits(node, P.Obj(c, "lane"), true, true)) okMirror++;
                }
                mirrored = LaneMatch.ChooseMirrored(okPlain, okMirror);
            }
            // exporter 1.2.1+: the plan says which side the CS1 city drove on; compare with this city's
            var planLht = P.Obj(root, "source") != null && P.Obj(root, "source").ContainsKey("leftHandTraffic") && P.Obj(root, "source")["leftHandTraffic"] is bool ? (bool?)(bool)P.Obj(root, "source")["leftHandTraffic"] : null;
            var cs2Lht = LeftHandTraffic(w) as bool?;
            string handednessFrom = "detected from the lanes";
            if (planLht.HasValue && cs2Lht.HasValue) { mirrored = planLht.Value != cs2Lht.Value; handednessFrom = "plan.source.leftHandTraffic"; }
            var sources = new Dictionary<string, Source>();
            Func<long, Dictionary<string, object>, bool, Source> resolve = (node, lr, inbound) =>
            {
                refsTotal++;
                long segment = (long)P.Num(lr, "segment", -1);
                bool isEnd;
                var edge = res.EdgeAt(w, segment, node, out isEnd);
                if (edge == Entity.Null) { bad("segment_or_node_not_found"); return null; }
                var lanes = EdgeLanes(w, job, edge, isEnd, node, segment);
                if (lanes == null) { bad("segment_not_in_job"); return null; }
                if (armSeen.Add(node + ":" + segment))
                {
                    double ang = ArmAngle(w, job, node, segment, edge, isEnd);
                    if (double.IsNaN(ang)) armUnknown++;
                    else if (ang <= armTol) armOk++;
                    else
                    {
                        armOff++;
                        if (!unsure.ContainsKey(node)) unsure[node] = "segment " + segment + " " + Math.Round(ang) + "° off";
                        if (armDiag.Count < maxDiag) armDiag.Add(new Dictionary<string, object> { { "node", node }, { "segment", segment }, { "edge", edge.Index }, { "angle", Math.Round(ang, 1) } });
                    }
                }
                string problem;
                int ordinal = (int)P.Num(lr, "ordinal", -1), cnt = (int)P.Num(lr, "count", -1);
                var idx = LaneMatch.Pick(lanes, mirrored ? LaneMatch.Mirror(ordinal, cnt) : ordinal, cnt, inbound, out problem);
                if (idx == null) { bad(problem); return null; }
                refsMatched++;
                var seg = job.Segments.First(x => x.Id == segment);
                {
                    double cs1Off = P.Num(lr, "offset", double.NaN), hw1 = seg.Source.HalfWidth, hw2 = HalfWidth(w, edge);
                    if (!double.IsNaN(cs1Off) && hw1 > 0 && hw2 > 0)
                    {
                        posChecked++;
                        var byPos = LaneMatch.PickByPosition(lanes, cs1Off / hw1, hw1, inbound); // absolute offsets: half-widths include sidewalks (CS1 16 m vs CS2 11.5 m for the same lanes)
                        var byPosM = LaneMatch.PickByPosition(lanes, -cs1Off / hw1, hw1, inbound);
                        if (byPos == idx) posAgree++;
                        if (byPosM == idx) posAgreeMirrored++;
                        if (byPos != idx && byPosM != idx && posDiag.Count < maxDiag)
                            posDiag.Add(new Dictionary<string, object> { { "node", node }, { "ref", segment + "#" + ordinal + "/" + cnt + " " + P.Str(lr, "dir", "") + (seg.Source.Reversed ? " reversed" : "") },
                                { "cs1_offset", cs1Off }, { "cs1_half_width", hw1 }, { "cs2_half_width", Math.Round(hw2, 2) }, { "picked", idx }, { "by_position", byPos }, { "by_position_mirrored", byPosM },
                                { "lanes", lanes.OrderBy(x => x.Offset).Select(x => x.Index + (x.Inbound ? "in" : "out") + "@" + Math.Round(x.Offset, 1)).Cast<object>().ToList() } });
                    }
                }
                return new Source
                {
                    PlanNode = node, Node = res.Node[node], Edge = edge, IsEnd = isEnd, LaneIndex = idx.Value, Segment = segment,
                    Ref = segment + "#" + P.Num(lr, "ordinal", -1) + "/" + P.Num(lr, "count", -1) + " " + P.Str(lr, "dir", "") + (seg.Source.Reversed ? " reversed" : ""),
                };
            };
            Func<Source, Source> get = src => { Source x; if (!sources.TryGetValue(src.Key, out x)) sources[src.Key] = x = src; return x; };
            foreach (var o in conns)
            {
                var c = (Dictionary<string, object>)o;
                long node = (long)P.Num(c, "node", -1);
                var src = resolve(node, P.Obj(c, "from"), true);
                var tgt = resolve(node, P.Obj(c, "to"), false);
                if (src == null || tgt == null) continue;
                var s = get(src);
                s.Targets.Add(new KeyValuePair<Entity, int>(tgt.Edge, tgt.LaneIndex));
                s.TargetRefs.Add(tgt.Ref);
                var wd = PlanArmCurve(job, node, tgt.Segment);
                if (wd != null) s.WantCurves.Add(wd);
            }
            int arrowsOverridden = 0;
            foreach (var o in arrows)
            {
                var c = (Dictionary<string, object>)o;
                long node = (long)P.Num(c, "node", -1);
                var src = resolve(node, P.Obj(c, "lane"), true);
                if (src == null) continue;
                var s = get(src);
                if (s.Targets.Count > 0) { arrowsOverridden++; continue; }
                s.Arrows = new HashSet<string>((P.List(c, "arrows") ?? new List<object>()).Cast<string>());
            }

            // the junctions' lanes before anything is written (U-turns, arrows, the report)
            var vanilla = new Dictionary<Entity, List<NodeLane>>();
            Func<Entity, List<NodeLane>> lanesAt = n => { List<NodeLane> l; if (!vanilla.TryGetValue(n, out l)) vanilla[n] = l = NodeCarLanes(w, n); return l; };

            // U-turn rules (TM:PE junction restriction per segment end) as lane connections: the Traffic mod has none
            int uturnBans = 0, uturnAdds = 0, uturnAlready = 0;
            var uturnChecks = new List<KeyValuePair<Source, bool>>();
            foreach (var e in uturnEnds)
            {
                long node = (long)P.Num(e, "node", -1), segment = (long)P.Num(e, "segment", -1);
                bool allowed = (bool)e["uturn"];
                bool isEnd;
                var edge = res.EdgeAt(w, segment, node, out isEnd);
                if (edge == Entity.Null) { bad("uturn_end_not_found"); continue; }
                var lanes = EdgeLanes(w, job, edge, isEnd, node, segment);
                if (lanes == null) continue;
                var n = res.Node[node];
                var at = lanesAt(n);
                if (!allowed)
                {
                    foreach (var l in lanes.Where(x => x.Inbound))
                    {
                        if (!at.Any(x => x.SrcEdge == edge && x.SrcIndex == l.Index && x.TgtEdge == edge)) continue; // no U-turn lane to ban
                        var s = get(new Source { PlanNode = node, Node = n, Edge = edge, IsEnd = isEnd, LaneIndex = l.Index, Segment = segment, Ref = segment + " u-turn ban" });
                        s.NoUTurnTo = edge;
                        if (s.Targets.Count > 0) s.Targets.RemoveAll(t => t.Key == edge);
                        else if (s.Arrows == null) s.Arrows = new HashSet<string>(AllArrows);
                        uturnBans++;
                        uturnChecks.Add(new KeyValuePair<Source, bool>(s, false));
                    }
                }
                else
                {
                    var inn = LaneMatch.Innermost(lanes, true);
                    var outn = LaneMatch.Innermost(lanes, false);
                    if (inn == null || outn == null) { bad("uturn_without_lanes"); continue; }
                    var s0 = new Source { PlanNode = node, Node = n, Edge = edge, IsEnd = isEnd, LaneIndex = inn.Value, Segment = segment, Ref = segment + " u-turn" };
                    if (at.Any(x => x.SrcEdge == edge && x.SrcIndex == inn.Value && x.TgtEdge == edge) && !sources.ContainsKey(s0.Key)) { uturnAlready++; continue; }
                    var s = get(s0);
                    var target = new KeyValuePair<Entity, int>(edge, outn.Value);
                    if (s.Targets.Count > 0) { if (!s.Targets.Contains(target)) { s.Targets.Add(target); s.TargetRefs.Add(segment + " u-turn"); } }
                    else
                    {
                        if (s.Arrows == null) s.Arrows = new HashSet<string>(AllArrows);
                        s.Extra.Add(target);
                    }
                    uturnAdds++;
                    uturnChecks.Add(new KeyValuePair<Source, bool>(s, true));
                }
            }

            // per node: the GeneratedConnection rows of each source lane
            var sys = w.World.GetOrCreateSystemManaged<Game.Tools.ToolSystem>(); // lookups need a system in this Entities version
            object boxedComp = sys.GetComponentLookup<Composition>(true), boxedLanes = sys.GetBufferLookup<NetCompositionLane>(true);
            var byNode = sources.Values.GroupBy(s => s.Node).ToList();
            foreach (var g in byNode) lanesAt(g.Key);
            int nodesDone = 0, nodesSkipped = 0, connections = 0, sourcesDone = 0, sourcesEmpty = 0, nodesPassThrough = 0, nodesUnsure = 0;
            var passThrough = new HashSet<Entity>(byNode.Where(g => w.IsReal(g.Key) && lanesAt(g.Key).Count == 0).Select(g => g.Key));
            if (!checkOnly)
                foreach (var g in byNode)
                {
                    var node = g.Key;
                    if (!w.IsReal(node)) { nodesSkipped++; continue; }
                    if (g.Any(x => unsure.ContainsKey(x.PlanNode))) { nodesUnsure++; continue; } // arm check failed: leave CS2's lanes
                    if (HasComp(em, node, mod.ModifiedConnections)) { nodesSkipped++; bad("node_already_has_lane_connections"); continue; }
                    // a node where one road just continues into another has no junction lanes: each lane runs on into the
                    // lane that starts where it ends, and there is nothing for the Traffic mod to connect
                    if (passThrough.Contains(node)) { nodesPassThrough++; continue; }
                    var nodeLanes = lanesAt(node);
                    var elements = new List<object>();
                    foreach (var s in g)
                    {
                        var rows = new List<object>();
                        var fromHere = nodeLanes.Where(l => l.SrcEdge == s.Edge && l.SrcIndex == s.LaneIndex).ToList();
                        var method = fromHere.Select(l => l.Methods).FirstOrDefault();
                        if (method == 0) method = Game.Pathfind.PathMethod.Road;
                        var targets = new List<KeyValuePair<Entity, int>>();
                        var unsafeTargets = new HashSet<string>();
                        if (s.Targets.Count > 0) targets.AddRange(s.Targets);
                        else if (s.Arrows != null)
                        {
                            foreach (var l in fromHere.Where(l => LaneMatch.Allows(s.Arrows, TurnOf(l.Flags)) && l.TgtEdge != s.NoUTurnTo))
                            {
                                targets.Add(new KeyValuePair<Entity, int>(l.TgtEdge, l.TgtIndex));
                                if ((l.Flags & CarLaneFlags.Unsafe) != 0) unsafeTargets.Add(l.TgtEdge.Index + ":" + l.TgtIndex);
                            }
                            foreach (var x in s.Extra) if (!targets.Contains(x)) targets.Add(x);
                        }
                        foreach (var t in targets.Distinct())
                        {
                            bool tgtIsEnd = em.GetComponentData<Edge>(t.Key).m_End == node;
                            var row = Row(mod, s.Edge, t.Key, s.LaneIndex, t.Value, s.IsEnd, tgtIsEnd, method, unsafeTargets.Contains(t.Key.Index + ":" + t.Value), ref boxedComp, ref boxedLanes);
                            if (row != null) rows.Add(row); else bad("lane_details_not_found");
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
            for (int f = 0; f < (checkOnly ? 1 : 60); f++) yield return null;

            // check: after the mod regenerated the junction lanes, each explicit source lane leads exactly to its targets,
            // and each U-turn rule holds
            var outcome = new Dictionary<string, int>();
            var diag = new List<object>();
            // turn check (#126): the CS2 junction lanes leaving each explicit source lane, by their own geometry, against
            // CS1's target arms; independent of the plan → edge → lane mapping
            int turnChecked = 0, turnOk = 0, turnWrong = 0, turnNone = 0, arrowChecked = 0, arrowOk = 0, arrowWrong = 0, arrowNone = 0;
            var turnDiag = new List<object>();
            var turnBad = new Dictionary<Entity, long>(); // CS2 node → plan node, junctions whose written lanes failed the turn check
            int nodesWithData = 0;
            var after = new Dictionary<Entity, List<NodeLane>>();
            foreach (var g in byNode)
            {
                if (!w.IsReal(g.Key)) continue;
                if (HasComp(em, g.Key, mod.ModifiedConnections)) nodesWithData++;
                after[g.Key] = NodeCarLanes(w, g.Key);
                foreach (var s in g)
                {
                    if (s.Targets.Count == 0)
                    {
                        // a TM:PE arrow lane: the CS2 lanes written from it must leave towards an arm CS1 allows (TM:PE's
                        // turn rule by geometry); reaching only some of them is fine
                        if (s.Arrows != null && s.Arrows.Count < 3 && s.Ref.IndexOf("u-turn", StringComparison.Ordinal) < 0 && !unsure.ContainsKey(s.PlanNode) && !passThrough.Contains(g.Key))
                        {
                            var allowed = ArrowArms(w, job, res, s);
                            var gotA = after[g.Key].Where(l => l.SrcEdge == s.Edge && l.SrcIndex == s.LaneIndex && l.TgtEdge != s.Edge).ToList();
                            string whyA;
                            arrowChecked++;
                            if (LaneMatch.TurnCheckCurves(allowed, gotA.Select(l => l.Exit).ToList(), gotA.Select(l => l.End).ToList(), armTol, out whyA, false)) arrowOk++;
                            else
                            {
                                if (whyA == "none") arrowNone++; else { arrowWrong++; turnBad[g.Key] = s.PlanNode; }
                                if (turnDiag.Count < maxDiag)
                                    turnDiag.Add(new Dictionary<string, object> {
                                        { "node", s.PlanNode }, { "why", whyA }, { "from", s.Ref }, { "arrows", s.Arrows.Cast<object>().ToList() },
                                        { "allowed_deg", allowed.Select(c => { var d = LaneMatch.TangentNear(c, c[0][0], c[0][1]); return (object)Math.Round(Math.Atan2(d[0], d[1]) * 180 / Math.PI); }).ToList() },
                                        { "got_deg", gotA.Select(l => (object)Math.Round(Math.Atan2(l.Exit[0], l.Exit[1]) * 180 / Math.PI)).ToList() } });
                            }
                        }
                        continue;
                    }
                    if (unsure.ContainsKey(s.PlanNode) && !checkOnly) { int u; outcome.TryGetValue("left_to_cs2_arm_check", out u); outcome["left_to_cs2_arm_check"] = u + 1; continue; }
                    var want = s.Targets.Select(t => t.Key.Index + ":" + t.Value).ToList();
                    List<string> got;
                    string kind;
                    if (passThrough.Contains(g.Key))
                    {
                        got = Continuations(w, g.Key, s.Edge, s.IsEnd, s.LaneIndex);
                        kind = LaneMatch.Compare(want, got) == "match" ? "continuous" : "pass_through_not_expressible";
                    }
                    else
                    {
                        var gotLanes = after[g.Key].Where(l => l.SrcEdge == s.Edge && l.SrcIndex == s.LaneIndex).ToList();
                        got = gotLanes.Select(l => l.TgtEdge.Index + ":" + l.TgtIndex).ToList();
                        kind = LaneMatch.Compare(want, got);
                        if (s.WantCurves.Count == s.Targets.Count && !unsure.ContainsKey(s.PlanNode)) // unsure junctions keep CS2's own lanes
                        {
                            string why;
                            turnChecked++;
                            if (LaneMatch.TurnCheckCurves(s.WantCurves, gotLanes.Select(l => l.Exit).ToList(), gotLanes.Select(l => l.End).ToList(), armTol, out why)) turnOk++;
                            else
                            {
                                if (why == "none") turnNone++; else { turnWrong++; turnBad[g.Key] = s.PlanNode; }
                                if (turnDiag.Count < maxDiag)
                                    turnDiag.Add(new Dictionary<string, object> {
                                        { "node", s.PlanNode }, { "why", why }, { "from", s.Ref }, { "want", s.TargetRefs.Cast<object>().ToList() },
                                        { "want_deg", s.WantCurves.Select(c => { var d = LaneMatch.TangentNear(c, c[0][0], c[0][1]); return (object)Math.Round(Math.Atan2(d[0], d[1]) * 180 / Math.PI); }).ToList() },
                                        { "got_deg", gotLanes.Select(l => (object)Math.Round(Math.Atan2(l.Exit[0], l.Exit[1]) * 180 / Math.PI)).ToList() } });
                            }
                        }
                    }
                    int k; outcome.TryGetValue(kind, out k); outcome[kind] = k + 1;
                    if (kind != "match" && kind != "continuous" && diag.Count < maxDiag)
                    {
                        var edgeLanes = EdgeLanes(w, job, s.Edge, s.IsEnd, s.PlanNode, s.Segment) ?? new List<EdgeLaneAtNode>();
                        diag.Add(new Dictionary<string, object>
                        {
                            { "node", s.PlanNode }, { "kind", kind }, { "from", s.Ref + " = " + s.Edge.Index + ":" + s.LaneIndex + (s.IsEnd ? " (edge end)" : " (edge start)") },
                            { "want", s.TargetRefs.Zip(want, (r, x) => r + " = " + x).Cast<object>().ToList() }, { "got", got.Cast<object>().ToList() },
                            { "got_forbidden", NodeCarLanes(w, g.Key, true).Where(l => l.SrcEdge == s.Edge && l.SrcIndex == s.LaneIndex).Select(l => l.TgtEdge.Index + ":" + l.TgtIndex + " " + TurnOf(l.Flags)).Cast<object>().ToList() },
                            { "before", vanilla[g.Key].Where(l => l.SrcEdge == s.Edge && l.SrcIndex == s.LaneIndex).Select(l => l.TgtEdge.Index + ":" + l.TgtIndex + " " + TurnOf(l.Flags)).Cast<object>().ToList() },
                            { "source_edge_lanes", edgeLanes.OrderBy(x => x.Offset).Select(x => x.Index + (x.Inbound ? "in" : "out") + "@" + Math.Round(x.Offset, 1)).Cast<object>().ToList() },
                        });
                    }
                }
            }
            // a junction whose written connections lead to another arm than CS1's is not mapped confidently: take back what
            // was written there (as the mod's "Reset to vanilla" does) and leave CS2's own lanes, reported (DECISIONS.md #126)
            int turnReverted = 0;
            if (!checkOnly)
            {
                foreach (var kv in turnBad) if (RevertNode(w, mod, kv.Key)) turnReverted++;
                if (turnReverted > 0) for (int f = 0; f < 30; f++) yield return null;
            }
            int uturnOk = 0, uturnPassThrough = 0;
            var uturnDiag = new List<object>();
            foreach (var kv in uturnChecks)
            {
                var s = kv.Key;
                List<NodeLane> l;
                if (!after.TryGetValue(s.Node, out l)) continue;
                if (passThrough.Contains(s.Node)) { uturnPassThrough++; continue; } // no junction, no U-turn lane to give or take
                bool hasUturn = l.Any(x => x.SrcEdge == s.Edge && x.TgtEdge == s.Edge && x.SrcIndex == s.LaneIndex);
                if (hasUturn == kv.Value) uturnOk++;
                else if (uturnDiag.Count < 20)
                    uturnDiag.Add(new Dictionary<string, object>
                    {
                        { "node", s.PlanNode }, { "segment", s.Segment }, { "allowed", kv.Value }, { "pass_through", passThrough.Contains(s.Node) }, { "lane", s.Edge.Index + ":" + s.LaneIndex },
                        { "extra", s.Extra.Select(x => x.Key.Index + ":" + x.Value).Cast<object>().ToList() },
                        { "after", l.Where(x => x.SrcEdge == s.Edge && x.SrcIndex == s.LaneIndex).Select(x => x.TgtEdge.Index + ":" + x.TgtIndex + " " + TurnOf(x.Flags)).Cast<object>().ToList() },
                        { "before", vanilla[s.Node].Where(x => x.SrcEdge == s.Edge && x.SrcIndex == s.LaneIndex).Select(x => x.TgtEdge.Index + ":" + x.TgtIndex + " " + TurnOf(x.Flags)).Cast<object>().ToList() },
                        { "node_has_data", HasComp(em, s.Node, mod.ModifiedConnections) },
                    });
            }
            int matching, continuous, leftToCs2; outcome.TryGetValue("match", out matching); outcome.TryGetValue("continuous", out continuous);
            outcome.TryGetValue("left_to_cs2_arm_check", out leftToCs2);
            matching += continuous; // CS2 already does what CS1's rule says
            int checkedN = outcome.Values.Sum() - leftToCs2;
            yield return new Dictionary<string, object>
            {
                { "op", "lanes" }, { "traffic_mod", mod.Version }, { "check_only", checkOnly },
                { "lane_refs", refsTotal }, { "lane_refs_matched", refsMatched }, { "lane_refs_fitting_plain", okPlain }, { "lane_refs_fitting_mirrored", okMirror },
                { "handedness", mirrored ? "mirrored (the CS1 city drove on the other side)" : "same" }, { "handedness_from", handednessFrom },
                { "cs1_left_hand_traffic", planLht }, { "cs2_left_hand_traffic", LeftHandTraffic(w) },
                { "plan_connections", conns.Count }, { "plan_arrows", arrows.Count }, { "arrows_overridden_by_connections", arrowsOverridden },
                { "nodes_planned", byNode.Count }, { "nodes_done", nodesDone }, { "nodes_with_lane_data", nodesWithData }, { "nodes_skipped", nodesSkipped },
                { "nodes_pass_through", nodesPassThrough }, { "connections", connections }, { "source_lanes", sourcesDone }, { "source_lanes_without_connections", sourcesEmpty },
                { "explicit_sources_checked", checkedN }, { "explicit_sources_matching", matching },
                { "match_rate", checkedN == 0 ? 0 : Math.Round(100.0 * matching / checkedN, 1) }, { "outcomes", outcome.ToDictionary(kv => kv.Key, kv => (object)kv.Value) },
                { "uturn_rules", uturnEnds.Count }, { "uturn_bans", uturnBans }, { "uturn_added", uturnAdds }, { "uturn_already_in_cs2", uturnAlready },
                { "uturn_checked", uturnChecks.Count - uturnPassThrough }, { "uturn_holding", uturnOk }, { "uturn_at_nodes_without_junction", uturnPassThrough }, { "uturn_failures", uturnDiag },
                { "problems", problems.ToDictionary(kv => kv.Key, kv => (object)kv.Value) }, { "mismatches", diag },
                { "arm_check", new Dictionary<string, object> {
                    { "tolerance_deg", armTol }, { "arms_ok", armOk }, { "arms_off", armOff }, { "arms_unknown", armUnknown },
                    { "junctions_off", unsure.Count }, { "junctions_left_to_cs2", nodesUnsure }, { "source_lanes_left_to_cs2", leftToCs2 },
                    { "junctions", unsure.Take(maxDiag).ToDictionary(kv => kv.Key.ToString(), kv => (object)kv.Value) }, { "arms", armDiag } } },
                { "turn_check", new Dictionary<string, object> {
                    { "tolerance_deg", armTol }, { "checked", turnChecked }, { "ok", turnOk }, { "wrong", turnWrong }, { "none", turnNone },
                    { "junctions_wrong", turnBad.Count }, { "junctions_reverted_to_cs2", turnReverted }, { "junctions", turnBad.Values.Distinct().Cast<object>().ToList() },
                    { "arrow_lanes_checked", arrowChecked }, { "arrow_lanes_ok", arrowOk }, { "arrow_lanes_wrong", arrowWrong }, { "arrow_lanes_none", arrowNone }, { "wrong_examples", turnDiag } } },
                { "lane_position_check", new Dictionary<string, object> {
                    { "checked", posChecked }, { "agree", posAgree }, { "agree_mirrored", posAgreeMirrored },
                    { "agree_rate", posChecked == 0 ? 0 : Math.Round(100.0 * Math.Max(posAgree, posAgreeMirrored) / posChecked, 1) }, { "disagreements", posDiag } } },
                { "seconds", Entry.R(sw.Elapsed.TotalSeconds) },
            };
        }

        /// <summary>
        /// At a node without junction lanes: the lanes the source lane runs on into, i.e. the other roads' lanes that start
        /// within 1 m of where it ends.
        /// </summary>
        static List<string> Continuations(GameWorld w, Entity node, Entity srcEdge, bool srcIsEnd, int srcIndex)
        {
            var r = new List<string>();
            var src = LanePoints(w, srcEdge, srcIsEnd).Where(x => x.Inbound && x.Index == srcIndex).ToList();
            if (src.Count == 0) return r;
            var p = src[0].Point;
            foreach (var e in w.EdgesOf(node))
            {
                if (e == srcEdge || !w.IsReal(e)) continue;
                bool isEnd = w.Em.GetComponentData<Edge>(e).m_End == node;
                foreach (var l in LanePoints(w, e, isEnd))
                    if (!l.Inbound && math.distance(l.Point, p) < 1f) r.Add(e.Index + ":" + l.Index);
            }
            return r;
        }

        struct LanePoint { public int Index; public bool Inbound; public float3 Point; }

        static List<LanePoint> LanePoints(GameWorld w, Entity edge, bool isEnd)
        {
            var em = w.Em;
            var r = new List<LanePoint>();
            if (!em.HasBuffer<Game.Net.SubLane>(edge)) return r;
            float t = isEnd ? 1f : 0f;
            var subs = em.GetBuffer<Game.Net.SubLane>(edge, true);
            for (int i = 0; i < subs.Length; i++)
            {
                var le = subs[i].m_SubLane;
                if ((subs[i].m_PathMethods & Game.Pathfind.PathMethod.Road) == 0) continue;
                if (!em.HasComponent<Game.Net.CarLane>(le) || !em.HasComponent<EdgeLane>(le) || !em.HasComponent<Lane>(le) || em.HasComponent<Game.Net.SecondaryLane>(le) || em.HasComponent<MasterLane>(le)) continue;
                var delta = em.GetComponentData<EdgeLane>(le).m_EdgeDelta;
                bool endsHere = math.abs(delta.y - t) < 0.001f, startsHere = math.abs(delta.x - t) < 0.001f;
                if (!endsHere && !startsHere) continue;
                var lane = em.GetComponentData<Lane>(le);
                var lc = em.GetComponentData<Curve>(le).m_Bezier;
                r.Add(new LanePoint { Index = (endsHere ? lane.m_EndNode : lane.m_StartNode).GetLaneIndex() & 0xff, Inbound = endsHere, Point = endsHere ? lc.d : lc.a });
            }
            return r;
        }

        /// <summary>Angle between the CS2 edge's direction leaving the node and the plan segment's (CS1 bezier) direction
        /// leaving the same node. NaN when unknown.</summary>
        static double ArmAngle(GameWorld w, ImportJob job, long node, long segment, Entity edge, bool isEnd)
        {
            var d = PlanArmDir(job, node, segment);
            if (d == null || !w.Em.HasComponent<Curve>(edge)) return double.NaN;
            var c = w.Em.GetComponentData<Curve>(edge).m_Bezier;
            float3 t = isEnd ? (c.c - c.d) : (c.b - c.a);
            if (math.lengthsq(t.xz) < 0.01f) t = isEnd ? (c.a - c.d) : (c.d - c.a);
            return LaneMatch.AngleDeg(d[0], d[1], t.x, t.z);
        }

        /// <summary>The CS1 arms an arrow lane may lead into: the node's other plan segments with lanes leaving the node,
        /// whose CS1 turn (LaneMatch.Cs1Turn, from the CS1 beziers) the arrows allow; their directions leaving the node.</summary>
        static List<double[][]> ArrowArms(GameWorld w, ImportJob job, M2Ops.Resolved res, Source s)
        {
            var r = new List<double[][]>();
            var src = PlanArmDir(job, s.PlanNode, s.Segment);
            if (src == null) return r;
            foreach (var id in job.Segments.Where(x => x.Start.Id == s.PlanNode || x.End.Id == s.PlanNode).Select(x => x.Id).Distinct())
            {
                if (id == s.Segment) continue;
                bool isEnd;
                var edge = res.EdgeAt(w, id, s.PlanNode, out isEnd);
                if (edge == Entity.Null) continue;
                var lanes = EdgeLanes(w, job, edge, isEnd, s.PlanNode, id);
                if (lanes == null || !lanes.Any(l => !l.Inbound)) continue;
                var d = PlanArmDir(job, s.PlanNode, id);
                if (d != null && LaneMatch.Allows(s.Arrows, LaneMatch.Cs1Turn(src, d))) r.Add(PlanArmCurve(job, s.PlanNode, id));
            }
            return r;
        }

        /// <summary>The CS1 segment's curve from the plan node outwards, 4 points {x, z} in CS2 world coordinates (the
        /// test offset added); straight to the other node without a bezier. Null when unknown.</summary>
        static double[][] PlanArmCurve(ImportJob job, long node, long segment)
        {
            var seg = job.SegmentAt(segment, node);
            if (seg == null) return null;
            bool atStart = seg.Start.Id == node;
            double ox = job.Options.OffsetX, oz = job.Options.OffsetZ;
            var b = seg.Bezier;
            if (b == null)
            {
                var n0 = atStart ? seg.Start : seg.End; var n1 = atStart ? seg.End : seg.Start;
                return new[] { new[] { n0.X, n0.Z }, new[] { n0.X + (n1.X - n0.X) / 3, n0.Z + (n1.Z - n0.Z) / 3 }, new[] { n0.X + 2 * (n1.X - n0.X) / 3, n0.Z + 2 * (n1.Z - n0.Z) / 3 }, new[] { n1.X, n1.Z } };
            }
            var pts = atStart ? new[] { b[0], b[1], b[2], b[3] } : new[] { b[3], b[2], b[1], b[0] };
            return pts.Select(p => new[] { p.X + ox, p.Z + oz }).ToArray();
        }

        /// <summary>The CS1 segment's direction leaving the plan node (its bezier tangent there), x/z; null when unknown.</summary>
        static double[] PlanArmDir(ImportJob job, long node, long segment)
        {
            var seg = job.SegmentAt(segment, node);
            if (seg == null) return null;
            bool atStart = seg.Start.Id == node;
            double px, pz;
            var b = seg.Bezier;
            if (b != null)
            {
                var p0 = atStart ? b[0] : b[3];
                var p1 = atStart ? b[1] : b[2];
                px = p1.X - p0.X; pz = p1.Z - p0.Z;
                if (px * px + pz * pz < 0.01) { var q = atStart ? b[3] : b[0]; px = q.X - p0.X; pz = q.Z - p0.Z; }
            }
            else
            {
                var n0 = atStart ? seg.Start : seg.End;
                var o = atStart ? seg.End : seg.Start;
                px = o.X - n0.X; pz = o.Z - n0.Z;
            }
            return px * px + pz * pz < 1e-6 ? null : new[] { px, pz };
        }

        /// <summary>Half the edge composition's width (m); NaN when unknown.</summary>
        static double HalfWidth(GameWorld w, Entity edge)
        {
            var em = w.Em;
            if (!em.HasComponent<Composition>(edge)) return double.NaN;
            var ce = em.GetComponentData<Composition>(edge).m_Edge;
            if (ce == Entity.Null || !em.HasComponent<NetCompositionData>(ce)) return double.NaN;
            return em.GetComponentData<NetCompositionData>(ce).m_Width / 2.0;
        }

        static object LeftHandTraffic(GameWorld w)
        {
            try { return w.World.GetOrCreateSystemManaged<Game.City.CityConfigurationSystem>().leftHandTraffic; } catch { return null; }
        }

        static Turn TurnOf(CarLaneFlags f)
        {
            if ((f & CarLaneFlags.UTurnLeft) != 0) return Turn.UTurnLeft;
            if ((f & CarLaneFlags.UTurnRight) != 0) return Turn.UTurnRight;
            if ((f & (CarLaneFlags.TurnLeft | CarLaneFlags.GentleTurnLeft)) != 0) return Turn.Left;
            if ((f & (CarLaneFlags.TurnRight | CarLaneFlags.GentleTurnRight)) != 0) return Turn.Right;
            return Turn.Straight;
        }

        sealed class NodeLane { public Entity SrcEdge, TgtEdge; public int SrcIndex, TgtIndex; public CarLaneFlags Flags; public Game.Pathfind.PathMethod Methods; public double[] Exit, End; }

        /// <summary>The junction's car lanes (no master lanes): source/target edge and lane index, turn flags.</summary>
        static List<NodeLane> NodeCarLanes(GameWorld w, Entity node, bool forbiddenOnly = false)
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
                if (((cl.m_Flags & CarLaneFlags.Forbidden) != 0) != forbiddenOnly) continue;
                var lc = em.HasComponent<Curve>(l) ? em.GetComponentData<Curve>(l).m_Bezier : default(Bezier4x3);
                var ex = MathUtils.EndTangent(lc);
                r.Add(new NodeLane { SrcEdge = src, TgtEdge = tgt, SrcIndex = lane.m_StartNode.GetLaneIndex() & 0xff, TgtIndex = lane.m_EndNode.GetLaneIndex() & 0xff, Flags = cl.m_Flags, Methods = subs[i].m_PathMethods, Exit = new double[] { ex.x, ex.z }, End = new double[] { lc.d.x, lc.d.z } });
            }
            return r;
        }

        /// <summary>
        /// The edge's car lanes at the node: offset in the plan segment's direction (+ = right), the lane index at that
        /// node end, and whether the lane ends there. Null when the segment isn't in the job.
        /// </summary>
        static List<EdgeLaneAtNode> EdgeLanes(GameWorld w, ImportJob job, Entity edge, bool isEnd, long planNode, long segment)
        {
            var em = w.Em;
            var seg = job.SegmentAt(segment, planNode);
            if (seg == null || !em.HasBuffer<Game.Net.SubLane>(edge)) return null;
            // plan forward = edge forward when the node is the plan segment's end and the edge's end (or start and start)
            bool sameDir = (planNode == seg.End.Id) == isEnd;
            var curve = em.GetComponentData<Curve>(edge).m_Bezier;
            float t = isEnd ? 1f : 0f;
            var at = MathUtils.Position(curve, t);
            var tan = MathUtils.Tangent(curve, t);
            if (!sameDir) tan = -tan;
            var right = math.normalizesafe(new float2(tan.z, -tan.x));
            var r = new List<EdgeLaneAtNode>();
            var subs = em.GetBuffer<Game.Net.SubLane>(edge, true);
            for (int i = 0; i < subs.Length; i++)
            {
                var sl = subs[i];
                if ((sl.m_PathMethods & Game.Pathfind.PathMethod.Road) == 0) continue;
                var le = sl.m_SubLane;
                if (!em.HasComponent<Game.Net.CarLane>(le) || !em.HasComponent<EdgeLane>(le) || !em.HasComponent<Lane>(le) || em.HasComponent<Game.Net.SecondaryLane>(le) || em.HasComponent<MasterLane>(le)) continue;
                var delta = em.GetComponentData<EdgeLane>(le).m_EdgeDelta;
                bool endsHere = math.abs(delta.y - t) < 0.001f, startsHere = math.abs(delta.x - t) < 0.001f;
                if (!endsHere && !startsHere) continue;
                var lane = em.GetComponentData<Lane>(le);
                var lc = em.GetComponentData<Curve>(le).m_Bezier;
                var p = endsHere ? lc.d : lc.a;
                r.Add(new EdgeLaneAtNode { Offset = math.dot((p - at).xz, right), Index = (endsHere ? lane.m_EndNode : lane.m_StartNode).GetLaneIndex() & 0xff, Inbound = endsHere });
            }
            return r;
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

        /// <summary>Removes the lane connections written at a node, like the mod's RemoveLaneConnectionsJob: Deleted on each
        /// connections entity, ModifiedLaneConnections and ModifiedConnections off the node, Updated on it and its edges.</summary>
        static bool RevertNode(GameWorld w, ModTypes mod, Entity node)
        {
            var em = w.Em;
            if (!w.IsReal(node) || !HasComp(em, node, mod.ModifiedConnections)) return false;
            var getBuf = typeof(EntityManager).GetMethods().First(x => x.Name == "GetBuffer" && x.IsGenericMethodDefinition && x.GetParameters().Length == 2).MakeGenericMethod(mod.ModifiedLaneConnections);
            var buf = getBuf.Invoke(em, new object[] { node, false });
            int n = (int)buf.GetType().GetProperty("Length").GetValue(buf);
            var item = buf.GetType().GetMethod("get_Item");
            for (int i = 0; i < n; i++)
            {
                var ce = (Entity)Get(item.Invoke(buf, new object[] { i }), "modifiedConnections");
                if (ce != Entity.Null && em.Exists(ce) && !em.HasComponent<Deleted>(ce)) em.AddComponent<Deleted>(ce);
            }
            RemoveComponent(em, node, mod.ModifiedLaneConnections);
            RemoveComponent(em, node, mod.ModifiedConnections);
            if (!em.HasComponent<Updated>(node)) em.AddComponent<Updated>(node);
            foreach (var e in w.EdgesOf(node)) if (!em.HasComponent<Updated>(e)) em.AddComponent<Updated>(e);
            return true;
        }

        static void RemoveComponent(EntityManager em, Entity e, Type t)
        {
            var m = typeof(EntityManager).GetMethods().First(x => x.Name == "RemoveComponent" && x.IsGenericMethodDefinition && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(Entity)).MakeGenericMethod(t);
            m.Invoke(em, new object[] { e });
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
