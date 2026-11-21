// M2 plan additions applied after the road import (schema/README.md §2): zoning, vanilla traffic settings and Traffic
// mod priority signs. Each op is a coroutine run with phase "tool" + pump "tool": structural changes (Updated,
// Upgraded, buffers) from the main-loop pump crash CS2 natively (terrain worker, 2026-11-21).
//
//   zoning     {plan, offsetX/Z?, waitFrames=600}   paint plan.zoning.cells onto the zone blocks of the imported roads
//   traffic    {plan, ...}                          vanilla: traffic lights / all-way stop (node Upgraded), turn bans and
//                                                   crosswalks (edge Upgraded, the side of that end)
//   trafficmod {plan, ...}                          Traffic mod (Paradox Mods 80095): priority signs, by reflection;
//                                                   lane connections / arrows / U-turns are counted and reported
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Cs2Importer.Core;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Tools;
using Game.Zones;
using GameBridge;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Importer
{
    public static class M2Ops
    {
        // ---------------------------------------------------------------- shared

        /// <summary>Plan nodes → CS2 node entities and plan segments → their CS2 edges (start → end), found by position.</summary>
        internal sealed class Resolved
        {
            public ImportJob Job;
            public readonly Dictionary<long, Entity> Node = new Dictionary<long, Entity>();
            public readonly Dictionary<long, List<Entity>> Edges = new Dictionary<long, List<Entity>>();

            /// <summary>The CS2 edge of a plan segment at one of its nodes; isEnd = the node is that edge's m_End.</summary>
            public Entity EdgeAt(GameWorld w, long segment, long node, out bool isEnd)
            {
                isEnd = false;
                List<Entity> path;
                Entity n;
                if (!Edges.TryGetValue(segment, out path) || !Node.TryGetValue(node, out n) || path.Count == 0) return Entity.Null;
                foreach (var e in new[] { path[0], path[path.Count - 1] })
                {
                    var ed = w.Em.GetComponentData<Edge>(e);
                    if (ed.m_Start == n) { isEnd = false; return e; }
                    if (ed.m_End == n) { isEnd = true; return e; }
                }
                return Entity.Null;
            }
        }

        internal static Resolved Resolve(GameWorld w, ImportJob job)
        {
            var r = new Resolved { Job = job };
            var idx = w.IndexRealNodes();
            w.RefreshTerrain();
            var planNodes = new HashSet<Entity>();
            foreach (var n in job.Nodes.Values)
            {
                var pos = new float3((float)n.X, w.TerrainHeight(n.X, n.Z) + (float)n.PositionHeight, (float)n.Z);
                float3 at;
                var e = idx.Find(pos, 1.0f, 30.0f, out at);
                if (e == Entity.Null) continue;
                r.Node[n.Id] = e;
                planNodes.Add(e);
            }
            var used = new HashSet<Entity>();
            foreach (var s in job.Segments)
            {
                Entity a, b;
                if (!r.Node.TryGetValue(s.Start.Id, out a) || !r.Node.TryGetValue(s.End.Id, out b)) continue;
                var path = Entry.FindPath(w, a, b, planNodes, used);
                if (path == null) continue;
                foreach (var e in path) used.Add(e);
                r.Edges[s.Id] = path;
            }
            return r;
        }

        static string ReadPlan(Dictionary<string, object> a) { return File.ReadAllText(Entry.PlanFile(a), Encoding.UTF8); }

        static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        static void MarkUpdated(GameWorld w, Entity e)
        {
            if (!w.IsReal(e)) return;
            if (!w.Em.HasComponent<Updated>(e)) w.Em.AddComponent<Updated>(e);
        }

        /// <summary>Updated on a node and every edge at it (and their other nodes), so compositions and lanes rebuild.</summary>
        static void MarkNodeUpdated(GameWorld w, Entity node)
        {
            MarkUpdated(w, node);
            foreach (var e in w.EdgesOf(node)) MarkUpdated(w, e);
        }

        static Dictionary<string, object> Skipped(string op, string why)
        {
            return new Dictionary<string, object> { { "op", op }, { "skipped", why } };
        }

        // ---------------------------------------------------------------- zoning

        public static IEnumerator Zoning(Dictionary<string, object> a)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var m2 = PlanM2.Parse(ReadPlan(a));
            if (m2.Zoning == null || m2.Zoning.Cells.Count == 0) { yield return Skipped("zoning", "the plan has no zoning cells"); yield break; }
            var z = m2.Zoning;
            var w = new GameWorld();
            float offX = (float)P.Num(a, "offsetX", 0), offZ = (float)P.Num(a, "offsetZ", 0);
            bool checkOnly = P.Bool(a, "checkOnly", false); // only measure how many plan cells carry their zone now
            int waitFrames = checkOnly ? 0 : (int)P.Num(a, "waitFrames", 600);

            // zone prefab -> ZoneType
            var types = new ZoneType?[z.Prefabs.Count];
            var prefabRows = new List<object>();
            for (int i = 0; i < z.Prefabs.Count; i++)
            {
                PrefabBase pb;
                Entity pe;
                bool ok = w.Prefabs.TryGetPrefab(new PrefabID("ZonePrefab", z.Prefabs[i]), out pb) && w.Prefabs.TryGetEntity(pb, out pe) && w.Em.HasComponent<ZoneData>(pe);
                if (ok)
                {
                    w.Prefabs.TryGetPrefab(new PrefabID("ZonePrefab", z.Prefabs[i]), out pb);
                    w.Prefabs.TryGetEntity(pb, out pe);
                    types[i] = w.Em.GetComponentData<ZoneData>(pe).m_ZoneType;
                }
                prefabRows.Add(new Dictionary<string, object> { { "name", z.Prefabs[i] }, { "found", ok } });
            }

            // the zone area of the cells (+ a margin)
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var c in z.Cells) { minX = math.min(minX, c.X + offX); maxX = math.max(maxX, c.X + offX); minZ = math.min(minZ, c.Z + offZ); maxZ = math.max(maxZ, c.Z + offZ); }
            var box = new float4(minX - 100, minZ - 100, maxX + 100, maxZ + 100);

            // CS2 creates the blocks itself along zoning roads, a few frames after the roads: wait until their count settles
            int last = -1, stable = 0, waited = 0;
            while (waited < waitFrames)
            {
                int n = BlocksIn(w, box).Count;
                stable = n == last && n > 0 ? stable + 1 : 0;
                last = n;
                if (stable >= 20) break;
                yield return null;
                waited++;
            }

            // spatial index of blocks (64 m grid over their bounds)
            var blocks = BlocksIn(w, box);
            var grid = new Dictionary<long, List<Entity>>();
            foreach (var be in blocks)
            {
                var bounds = ZoneUtils.CalculateBounds(w.Em.GetComponentData<Block>(be));
                for (int gx = (int)math.floor(bounds.min.x / 64); gx <= (int)math.floor(bounds.max.x / 64); gx++)
                    for (int gz = (int)math.floor(bounds.min.y / 64); gz <= (int)math.floor(bounds.max.y / 64); gz++)
                    {
                        long k = ((long)gx << 32) ^ (uint)gz;
                        List<Entity> l;
                        if (!grid.TryGetValue(k, out l)) grid[k] = l = new List<Entity>();
                        l.Add(be);
                    }
            }

            // paint: the nearest CS2 cell under each plan cell centre (CS1 and CS2 cell grids don't line up exactly)
            int painted = 0, already = 0, noBlock = 0, notVisible = 0, blocked = 0, unknownPrefab = 0, matched = 0;
            var targets = new List<KeyValuePair<Entity, int>>();
            var targetZone = new List<ZoneType>();
            var changed = new HashSet<Entity>();
            foreach (var c in z.Cells)
            {
                if (c.Prefab < 0 || c.Prefab >= types.Length || !types[c.Prefab].HasValue) { unknownPrefab++; continue; }
                var zt = types[c.Prefab].Value;
                var p = new float2(c.X + offX, c.Z + offZ);
                List<Entity> cand;
                if (!grid.TryGetValue(((long)(int)math.floor(p.x / 64) << 32) ^ (uint)(int)math.floor(p.y / 64), out cand)) { noBlock++; continue; }
                // every CS2 cell under this plan cell, in every block: blocks overlap at corners and along parallel roads,
                // and an overlapped cell is "Shared": the game keeps the two copies in sync, so painting only one of them
                // gets reverted (what the zone tool does: it paints every block under the brush)
                int hits = 0;
                bool anyInRange = false, anyVisible = false;
                foreach (var be in cand)
                {
                    var bl = w.Em.GetComponentData<Block>(be);
                    var ci = ZoneUtils.GetCellIndex(bl, p);
                    if (ci.x < 0 || ci.y < 0 || ci.x >= bl.m_Size.x || ci.y >= bl.m_Size.y) continue;
                    if (math.distancesq(ZoneUtils.GetCellPosition(bl, ci).xz, p) > 4.5f * 4.5f) continue; // within the 8 m cell
                    anyInRange = true;
                    var cells = w.Em.GetBuffer<Cell>(be, true);
                    int i = ci.y * bl.m_Size.x + ci.x;
                    if (i >= cells.Length) continue;
                    var st = cells[i].m_State;
                    if ((st & CellFlags.Visible) != 0) anyVisible = true;
                    if ((st & CellFlags.Blocked) != 0) continue;
                    if ((st & (CellFlags.Visible | CellFlags.Shared)) == 0) continue; // hidden and not a shared copy
                    targets.Add(new KeyValuePair<Entity, int>(be, i));
                    targetZone.Add(zt);
                    hits++;
                }
                if (hits > 0) { matched++; continue; }
                if (!anyInRange) noBlock++; else if (!anyVisible) notVisible++; else blocked++;
            }

            // CS1's and CS2's 8 m grids don't line up, so several plan cells can land on one CS2 cell: the CS2 cell gets
            // the zone most of them ask for (ties: the first one)
            var votes = new Dictionary<KeyValuePair<Entity, int>, Dictionary<ushort, int>>();
            var order = new List<KeyValuePair<Entity, int>>();
            for (int i = 0; i < targets.Count; i++)
            {
                Dictionary<ushort, int> v;
                if (!votes.TryGetValue(targets[i], out v)) { votes[targets[i]] = v = new Dictionary<ushort, int>(); order.Add(targets[i]); }
                int n; v.TryGetValue(targetZone[i].m_Index, out n); v[targetZone[i].m_Index] = n + 1;
            }
            int conflicts = 0;
            var chosen = new Dictionary<KeyValuePair<Entity, int>, ushort>();
            foreach (var key in order)
            {
                var v = votes[key];
                if (v.Count > 1) conflicts++;
                ushort bestZone = 0; int bestN = -1;
                foreach (var kv in v) if (kv.Value > bestN) { bestN = kv.Value; bestZone = kv.Key; }
                chosen[key] = bestZone;
                var buf = w.Em.GetBuffer<Cell>(key.Key, checkOnly);
                var cell = buf[key.Value];
                if (cell.m_Zone.m_Index == bestZone) { already++; continue; }
                if (checkOnly) continue;
                cell.m_Zone = new ZoneType { m_Index = bestZone };
                buf[key.Value] = cell;
                changed.Add(key.Key);
                painted++;
            }
            // what the zone tool's apply does: Updated + BatchesUpdated on the block (structural, after all buffer writes)
            foreach (var be in changed)
            {
                MarkUpdated(w, be);
                if (!w.Em.HasComponent<BatchesUpdated>(be)) w.Em.AddComponent<BatchesUpdated>(be);
            }

            var e1 = Frames(checkOnly ? 0 : 30);
            while (e1.MoveNext()) yield return null;

            // verify: does each CS2 cell still carry its chosen zone, and how many plan cells agree with their cell?
            int kept = 0, planAgree = 0, gone = 0;
            var byZone = new Dictionary<string, int>();
            var failedFlags = new SortedDictionary<string, int>();
            foreach (var kv in chosen)
            {
                var be = kv.Key.Key;
                if (!w.IsReal(be) || !w.Em.HasBuffer<Cell>(be)) { gone++; continue; }
                var cells = w.Em.GetBuffer<Cell>(be, true);
                if (kv.Key.Value >= cells.Length) { gone++; continue; }
                if (cells[kv.Key.Value].m_Zone.m_Index == kv.Value) { kept++; continue; }
                var k = cells[kv.Key.Value].m_State.ToString() + (cells[kv.Key.Value].m_Zone.m_Index == 0 ? " / zone none" : " / other zone");
                int n; failedFlags.TryGetValue(k, out n); failedFlags[k] = n + 1;
            }
            for (int i = 0; i < targets.Count; i++)
            {
                var be = targets[i].Key;
                if (!w.IsReal(be) || !w.Em.HasBuffer<Cell>(be)) continue;
                var cells = w.Em.GetBuffer<Cell>(be, true);
                if (targets[i].Value < cells.Length && cells[targets[i].Value].m_Zone.Equals(targetZone[i])) planAgree++;
            }
            foreach (var c in z.Cells)
                if (c.Prefab >= 0 && c.Prefab < z.Prefabs.Count) { int n; byZone.TryGetValue(z.Prefabs[c.Prefab], out n); byZone[z.Prefabs[c.Prefab]] = n + 1; }
            yield return new Dictionary<string, object>
            {
                { "op", "zoning" }, { "check_only", checkOnly }, { "theme", z.Theme }, { "prefabs", prefabRows }, { "plan_cells", z.Cells.Count }, { "plan_cells_by_zone", byZone },
                { "blocks", blocks.Count }, { "block_wait_frames", waited },
                { "cells_matched", matched }, { "cs2_cells", chosen.Count }, { "cs2_cells_with_conflicting_votes", conflicts },
                { "cells_painted", painted }, { "cells_already_zoned", already }, { "cs2_cells_verified", kept }, { "cs2_cells_failed_by_state", failedFlags.ToDictionary(x => x.Key, x => (object)x.Value) }, { "blocks_gone", gone }, { "cell_votes_agreeing", planAgree },
                { "coverage", z.Cells.Count == 0 ? 0 : Entry.R(100.0 * matched / z.Cells.Count) },
                { "unmatched", new Dictionary<string, object> { { "no_block", noBlock }, { "hidden_cell", notVisible }, { "blocked_cell", blocked }, { "unknown_zone", unknownPrefab } } },
                { "blocks_changed", changed.Count }, { "seconds", Entry.R(sw.Elapsed.TotalSeconds) },
            };
        }

        static List<Entity> BlocksIn(GameWorld w, float4 box)
        {
            var r = new List<Entity>();
            using (var q = w.Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Block>(), ComponentType.ReadOnly<Cell>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            }))
            using (var es = q.ToEntityArray(Allocator.Temp))
            using (var bs = q.ToComponentDataArray<Block>(Allocator.Temp))
                for (int i = 0; i < es.Length; i++)
                {
                    var p = bs[i].m_Position;
                    if (p.x >= box.x && p.x <= box.z && p.z >= box.y && p.z <= box.w) r.Add(es[i]);
                }
            return r;
        }

        // ---------------------------------------------------------------- vanilla traffic settings

        public static IEnumerator Traffic(Dictionary<string, object> a)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var json = ReadPlan(a);
            var m2 = PlanM2.Parse(json);
            if (m2.Traffic == null) { yield return Skipped("traffic", "the plan has no traffic section"); yield break; }
            var t = m2.Traffic;
            var w = new GameWorld();
            var res = Resolve(w, Entry.LoadJob(a, json));

            int nodesMissing = 0, lightsAdded = 0, lightsRemoved = 0, lightsAlready = 0, allWay = 0;
            var lightPlan = new List<KeyValuePair<Entity, bool>>();
            var touched = new HashSet<Entity>();
            foreach (var tn in t.Nodes)
            {
                Entity n;
                if (!res.Node.TryGetValue(tn.Id, out n)) { nodesMissing++; continue; }
                if (w.EdgesOf(n).Count < 3 && !tn.AllWayStop) continue; // lights only matter at junctions
                bool hasLights = NodeHasLights(w, n);
                var u = w.Em.HasComponent<Upgraded>(n) ? w.Em.GetComponentData<Upgraded>(n) : default(Upgraded);
                var f = u.m_Flags;
                var before = f;
                if (tn.TrafficLights.HasValue)
                {
                    lightPlan.Add(new KeyValuePair<Entity, bool>(n, tn.TrafficLights.Value));
                    if (tn.TrafficLights.Value == hasLights) lightsAlready++;
                    else if (tn.TrafficLights.Value)
                    {
                        f.m_General = (f.m_General | CompositionFlags.General.TrafficLights) & ~CompositionFlags.General.RemoveTrafficLights;
                        lightsAdded++;
                    }
                    else
                    {
                        f.m_General = (f.m_General | CompositionFlags.General.RemoveTrafficLights) & ~CompositionFlags.General.TrafficLights;
                        lightsRemoved++;
                    }
                }
                if (tn.AllWayStop) { f.m_General |= CompositionFlags.General.AllWayStop; allWay++; }
                if (f.Equals(before)) continue;
                u.m_Flags = f;
                if (w.Em.HasComponent<Upgraded>(n)) w.Em.SetComponentData(n, u); else w.Em.AddComponentData(n, u);
                touched.Add(n);
            }

            // per segment end: turn bans and crosswalks on the edge's side for that end (left = start node, right = end
            // node: CompositionSelectSystem mirrors the edge's flags for the end node's composition)
            int endsMissing = 0, forbidEnds = 0, crosswalkAdded = 0, crosswalkRemoved = 0;
            var forbidPlan = new List<object[]>();
            foreach (var se in t.SegmentEnds)
            {
                bool wantsForbid = se.Forbid.Count > 0, wantsCross = se.Crosswalk.HasValue;
                if (!wantsForbid && !wantsCross) continue;
                bool isEnd;
                var e = res.EdgeAt(w, se.Segment, se.Node, out isEnd);
                if (e == Entity.Null) { endsMissing++; continue; }
                CompositionFlags.Side add = 0, remove = 0;
                foreach (var f in se.Forbid)
                {
                    if (f == "left") add |= CompositionFlags.Side.ForbidLeftTurn;
                    else if (f == "right") add |= CompositionFlags.Side.ForbidRightTurn;
                    else if (f == "straight") add |= CompositionFlags.Side.ForbidStraight;
                }
                if (wantsForbid) forbidEnds++;
                if (wantsCross)
                {
                    if (se.Crosswalk.Value) { add |= CompositionFlags.Side.AddCrosswalk; remove |= CompositionFlags.Side.RemoveCrosswalk; crosswalkAdded++; }
                    else { add |= CompositionFlags.Side.RemoveCrosswalk; remove |= CompositionFlags.Side.AddCrosswalk; crosswalkRemoved++; }
                }
                var u = w.Em.HasComponent<Upgraded>(e) ? w.Em.GetComponentData<Upgraded>(e) : default(Upgraded);
                if (isEnd) u.m_Flags.m_Right = (u.m_Flags.m_Right & ~remove) | add;
                else u.m_Flags.m_Left = (u.m_Flags.m_Left & ~remove) | add;
                if (w.Em.HasComponent<Upgraded>(e)) w.Em.SetComponentData(e, u); else w.Em.AddComponentData(e, u);
                touched.Add(e);
                if (wantsForbid) forbidPlan.Add(new object[] { e, isEnd, add & (CompositionFlags.Side.ForbidLeftTurn | CompositionFlags.Side.ForbidRightTurn | CompositionFlags.Side.ForbidStraight) });
            }
            foreach (var x in touched)
            {
                if (w.Em.HasComponent<Node>(x)) MarkNodeUpdated(w, x);
                else
                {
                    MarkUpdated(w, x);
                    var ed = w.Em.GetComponentData<Edge>(x);
                    MarkUpdated(w, ed.m_Start);
                    MarkUpdated(w, ed.m_End);
                }
            }

            var e2 = Frames(30);
            while (e2.MoveNext()) yield return null;

            // verify against the rebuilt compositions
            int lightsOk = 0;
            foreach (var kv in lightPlan) if (w.IsReal(kv.Key) && NodeHasLights(w, kv.Key) == kv.Value) lightsOk++;
            int forbidSeenSameSide = 0, forbidSeenOtherSide = 0;
            foreach (var row in forbidPlan)
            {
                var e = (Entity)row[0];
                if (!w.IsReal(e) || !w.Em.HasComponent<Composition>(e)) continue;
                bool isEnd = (bool)row[1];
                var want = (CompositionFlags.Side)row[2];
                var c = w.Em.GetComponentData<Composition>(e);
                var nodeComp = isEnd ? c.m_EndNode : c.m_StartNode;
                if (!w.Em.HasComponent<NetCompositionData>(nodeComp)) continue;
                var nf = w.Em.GetComponentData<NetCompositionData>(nodeComp).m_Flags;
                if ((nf.m_Left & want) == want) forbidSeenSameSide++;
                else if ((nf.m_Right & want) == want) forbidSeenOtherSide++;
            }
            yield return new Dictionary<string, object>
            {
                { "op", "traffic" }, { "plan_nodes", t.Nodes.Count }, { "plan_segment_ends", t.SegmentEnds.Count },
                { "nodes_missing", nodesMissing }, { "segment_ends_missing", endsMissing },
                { "lights_added", lightsAdded }, { "lights_removed", lightsRemoved }, { "lights_already_as_planned", lightsAlready },
                { "lights_as_planned_after", lightsOk }, { "lights_planned", lightPlan.Count }, { "all_way_stops", allWay },
                { "turn_ban_ends", forbidEnds }, { "turn_bans_in_node_composition", forbidSeenSameSide + forbidSeenOtherSide },
                { "turn_bans_composition_side", new Dictionary<string, object> { { "left", forbidSeenSameSide }, { "right", forbidSeenOtherSide } } },
                { "crosswalks_added", crosswalkAdded }, { "crosswalks_removed", crosswalkRemoved },
                { "entities_updated", touched.Count }, { "seconds", Entry.R(sw.Elapsed.TotalSeconds) },
            };
        }

        /// <summary>Whether CS2 currently gives this node traffic lights (any connected edge's node composition).</summary>
        static bool NodeHasLights(GameWorld w, Entity node)
        {
            foreach (var e in w.EdgesOf(node))
            {
                if (!w.Em.HasComponent<Composition>(e)) continue;
                var c = w.Em.GetComponentData<Composition>(e);
                var ed = w.Em.GetComponentData<Edge>(e);
                var nc = ed.m_Start == node ? c.m_StartNode : c.m_EndNode;
                if (w.Em.HasComponent<NetCompositionData>(nc) && (w.Em.GetComponentData<NetCompositionData>(nc).m_Flags.m_General & CompositionFlags.General.TrafficLights) != 0) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- Traffic mod (by reflection)

        /// <summary>The Traffic mod's component types, or null when the mod isn't loaded.</summary>
        sealed class TrafficModTypes
        {
            public Type LanePriority, PriorityType, ModifiedPriorities;
            public FieldInfo LaneIndex, Priority, IsEnd;
            public string Version;

            public static TrafficModTypes Find()
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name != "Traffic") continue;
                    var lp = asm.GetType("Traffic.Components.PrioritySigns.LanePriority");
                    var pt = asm.GetType("Traffic.Components.PrioritySigns.PriorityType");
                    var mp = asm.GetType("Traffic.Components.ModifiedPriorities");
                    if (lp == null || pt == null || mp == null) continue;
                    var t = new TrafficModTypes { LanePriority = lp, PriorityType = pt, ModifiedPriorities = mp, Version = asm.GetName().Version.ToString() };
                    t.LaneIndex = lp.GetField("laneIndex"); t.Priority = lp.GetField("priority"); t.IsEnd = lp.GetField("isEnd");
                    if (t.LaneIndex == null || t.Priority == null || t.IsEnd == null) continue;
                    return t;
                }
                return null;
            }
        }

        static MethodInfo Generic(string name, int args, Type t)
        {
            return typeof(EntityManager).GetMethods().First(m => m.Name == name && m.IsGenericMethodDefinition && m.GetParameters().Length == args && m.GetParameters()[0].ParameterType == typeof(Entity)).MakeGenericMethod(t);
        }

        public static IEnumerator TrafficMod(Dictionary<string, object> a)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var json = ReadPlan(a);
            var m2 = PlanM2.Parse(json);
            if (m2.Traffic == null) { yield return Skipped("trafficmod", "the plan has no traffic section"); yield break; }
            var t = m2.Traffic;
            var mod = TrafficModTypes.Find();
            int uturns = t.SegmentEnds.Count(s => s.UTurn.HasValue);
            var notApplied = new Dictionary<string, object> { { "lane_connections", t.LaneConnections }, { "lane_arrows", t.LaneArrows }, { "uturn_rules", uturns } };
            if (mod == null)
            {
                var sk = Skipped("trafficmod", "the Traffic mod (Paradox Mods 80095) is not loaded");
                sk["priority_signs_planned"] = t.SegmentEnds.Count(s => s.Priority != null);
                sk["not_applied"] = notApplied;
                yield return sk;
                yield break;
            }
            var w = new GameWorld();
            var res = Resolve(w, Entry.LoadJob(a, json));
            var em = w.Em;
            var hasBuffer = Generic("HasBuffer", 1, mod.LanePriority);
            var addBuffer = Generic("AddBuffer", 1, mod.LanePriority);
            var getBuffer = typeof(EntityManager).GetMethods().First(m => m.Name == "GetBuffer" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2).MakeGenericMethod(mod.LanePriority);
            var hasComp = Generic("HasComponent", 1, mod.ModifiedPriorities);
            var addComp = typeof(EntityManager).GetMethods().First(m => m.Name == "AddComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Entity)).MakeGenericMethod(mod.ModifiedPriorities);
            var bufType = typeof(DynamicBuffer<>).MakeGenericType(mod.LanePriority);
            var bufAdd = bufType.GetMethod("Add");
            var bufClear = bufType.GetMethod("Clear");
            var bufLen = bufType.GetProperty("Length");
            var bufItem = bufType.GetMethod("get_Item");
            object boxedEm = em;

            // collect per edge: (lane key, priority, isEnd)
            var perEdge = new Dictionary<Entity, List<object>>();
            int endsMissing = 0, endsWithoutLanes = 0, signs = 0, lanes = 0;
            var byKind = new Dictionary<string, int>();
            foreach (var se in t.SegmentEnds)
            {
                if (se.Priority == null) continue;
                ushort prio = se.Priority == "main" ? (ushort)1 : se.Priority == "yield" ? (ushort)2 : se.Priority == "stop" ? (ushort)3 : (ushort)0;
                if (prio == 0) continue;
                bool isEnd;
                var e = res.EdgeAt(w, se.Segment, se.Node, out isEnd);
                if (e == Entity.Null) { endsMissing++; continue; }
                var keys = InboundCarLanes(w, e, isEnd);
                if (keys.Count == 0) { endsWithoutLanes++; continue; }
                List<object> l;
                if (!perEdge.TryGetValue(e, out l)) perEdge[e] = l = new List<object>();
                foreach (var k in keys)
                {
                    var lp = Activator.CreateInstance(mod.LanePriority);
                    mod.LaneIndex.SetValue(lp, k);
                    mod.Priority.SetValue(lp, Enum.ToObject(mod.PriorityType, prio));
                    mod.IsEnd.SetValue(lp, isEnd);
                    l.Add(lp);
                    lanes++;
                }
                signs++;
                int n; byKind.TryGetValue(se.Priority, out n); byKind[se.Priority] = n + 1;
            }

            var signedNodes = new HashSet<Entity>();
            foreach (var se in t.SegmentEnds)
            {
                Entity n;
                if (se.Priority != null && res.Node.TryGetValue(se.Node, out n)) signedNodes.Add(n);
            }
            var flagsBefore = NodeLaneFlagCounts(w, signedNodes);

            // write: keep the edge's other entries (the other end), replace those of the same lane + end
            foreach (var kv in perEdge)
            {
                var e = kv.Key;
                if (!w.IsReal(e)) continue;
                var keep = new List<object>();
                if ((bool)hasBuffer.Invoke(boxedEm, new object[] { e }))
                {
                    var old = getBuffer.Invoke(boxedEm, new object[] { e, false });
                    int len = (int)bufLen.GetValue(old);
                    for (int i = 0; i < len; i++)
                    {
                        var item = bufItem.Invoke(old, new object[] { i });
                        bool replaced = kv.Value.Any(x => mod.LaneIndex.GetValue(x).Equals(mod.LaneIndex.GetValue(item)) && mod.IsEnd.GetValue(x).Equals(mod.IsEnd.GetValue(item)));
                        if (!replaced) keep.Add(item);
                    }
                }
                else addBuffer.Invoke(boxedEm, new object[] { e });
                if (!(bool)hasComp.Invoke(boxedEm, new object[] { e })) addComp.Invoke(boxedEm, new object[] { e });
                var buf = getBuffer.Invoke(boxedEm, new object[] { e, false }); // fresh handle after the structural changes
                bufClear.Invoke(buf, null);
                foreach (var x in keep) bufAdd.Invoke(buf, new[] { x });
                foreach (var x in kv.Value) bufAdd.Invoke(buf, new[] { x });
                MarkUpdated(w, e);
                var ed = em.GetComponentData<Edge>(e);
                MarkUpdated(w, ed.m_Start);
                MarkUpdated(w, ed.m_End);
            }

            var e3 = Frames(30);
            while (e3.MoveNext()) yield return null;

            int edgesKept = 0;
            foreach (var e in perEdge.Keys) if (w.IsReal(e) && (bool)hasBuffer.Invoke(boxedEm, new object[] { e })) edgesKept++;
            var flagsAfter = NodeLaneFlagCounts(w, signedNodes);
            yield return new Dictionary<string, object>
            {
                { "op", "trafficmod" }, { "traffic_mod", mod.Version },
                { "priority_signs", signs }, { "priority_by_kind", byKind }, { "priority_lanes", lanes }, { "edges_with_priorities", perEdge.Count },
                { "edges_with_priorities_after", edgesKept }, { "segment_ends_missing", endsMissing }, { "segment_ends_without_car_lanes", endsWithoutLanes },
                { "node_lane_flags_before", flagsBefore }, { "node_lane_flags_after", flagsAfter }, { "signed_nodes", signedNodes.Count },
                { "not_applied", notApplied }, { "seconds", Entry.R(sw.Elapsed.TotalSeconds) },
            };
        }

        /// <summary>How many car lanes inside these junctions carry CS2's priority flags (what the Traffic mod sets).</summary>
        static Dictionary<string, object> NodeLaneFlagCounts(GameWorld w, IEnumerable<Entity> nodes)
        {
            int yield = 0, stop = 0, rightOfWay = 0, lanes = 0;
            foreach (var n in nodes)
            {
                if (!w.IsReal(n) || !w.Em.HasBuffer<Game.Net.SubLane>(n)) continue;
                var subs = w.Em.GetBuffer<Game.Net.SubLane>(n, true);
                for (int i = 0; i < subs.Length; i++)
                {
                    var l = subs[i].m_SubLane;
                    if (!w.Em.HasComponent<Game.Net.CarLane>(l)) continue;
                    lanes++;
                    var f = w.Em.GetComponentData<Game.Net.CarLane>(l).m_Flags;
                    if ((f & CarLaneFlags.Yield) != 0) yield++;
                    if ((f & CarLaneFlags.Stop) != 0) stop++;
                    if ((f & CarLaneFlags.RightOfWay) != 0) rightOfWay++;
                }
            }
            return new Dictionary<string, object> { { "car_lanes", lanes }, { "yield", yield }, { "stop", stop }, { "right_of_way", rightOfWay } };
        }

        /// <summary>
        /// Car lanes of an edge that end at one of its nodes, as the Traffic mod keys them: (composition lane index,
        /// group, carriageway). Same selection as its GenerateLaneHandlesJob: road/bicycle edge lanes, no secondary or
        /// master lanes, lanes whose EdgeLane delta ends at that node.
        /// </summary>
        static List<int3> InboundCarLanes(GameWorld w, Entity edge, bool isEnd)
        {
            var r = new List<int3>();
            var em = w.Em;
            if (!em.HasBuffer<Game.Net.SubLane>(edge) || !em.HasComponent<Composition>(edge)) return r;
            var compEdge = em.GetComponentData<Composition>(edge).m_Edge;
            if (!em.HasBuffer<NetCompositionLane>(compEdge)) return r;
            var compLanes = em.GetBuffer<NetCompositionLane>(compEdge, true);
            float target = isEnd ? 1f : 0f;
            var subs = em.GetBuffer<Game.Net.SubLane>(edge, true);
            for (int i = 0; i < subs.Length; i++)
            {
                var sl = subs[i];
                if ((sl.m_PathMethods & Game.Pathfind.PathMethod.Road) == 0) continue;
                var le = sl.m_SubLane;
                if (!em.HasComponent<EdgeLane>(le) || !em.HasComponent<Lane>(le) || em.HasComponent<Game.Net.SecondaryLane>(le) || em.HasComponent<MasterLane>(le)) continue;
                if (math.abs(em.GetComponentData<EdgeLane>(le).m_EdgeDelta.y - target) > 0.001f) continue;
                int li = em.GetComponentData<Lane>(le).m_EndNode.GetLaneIndex() & 0xff;
                if (li >= compLanes.Length) continue;
                var cl = compLanes[li];
                var key = new int3(cl.m_Index, cl.m_Group, cl.m_Carriageway);
                if (!r.Contains(key)) r.Add(key);
            }
            return r;
        }
    }
}
