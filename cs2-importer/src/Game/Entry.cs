// exec entry point of the M1 CS2 importer (tools/PROTOCOL.md "exec"). One op per call, dispatched on args.op:
//
//   check  {plan, ...options}                          [phase main]  parse + validate the plan, look up every prefab,
//                                                                    count CS2 roads already in the area
//   clear  {plan | x0,z0,x1,z1, margin=50}             [phase tool, pump tool]  bulldoze top-level CS2 roads in the area
//   import {plan, clear=false, margin=50, batchSize=400, settleFrames=2, verifyFrames=30, ...}
//                                                      [phase tool, pump tool]  build the plan's roads, then verify;
//                                                                    returns the JSON report (and writes it to {report} if given)
//   verify {plan, ...options}                          [phase main]  re-check an earlier import against the plan
//
// Options shared by check/import/verify: batchSize, cellSize, groundSnap, minLength, shortLength (see ImportOptions).
// Statics never survive between calls (the bridge loads a fresh copy of this DLL every time); all state lives in the
// coroutine or in the game world.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Colossal.Logging;
using Cs2Importer.Core;
using GameBridge;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Importer
{
    public static class Entry
    {
        public const string Build = "m1";
        static readonly ILog Log = LogManager.GetLogger("Cs2Importer").SetShowsErrorsInUI(false);

        public static object Run(Dictionary<string, object> args)
        {
            string op = P.Str(args, "op", "check");
            switch (op)
            {
                case "check": return Check(args);
                case "verify": return VerifyOnly(args);
                case "clear": return ClearOp(args);
                case "import": return Import(args);
                case "terrain": return TerrainStats(args);
                case "inspect": return Inspect(args);
                case "mod": return ModScript.Drive(args);
                default: throw new ArgumentException("unknown op '" + op + "' (check, clear, import, verify)");
            }
        }

        // ---------------------------------------------------------------- plan loading

        static ImportOptions Options(Dictionary<string, object> a)
        {
            var o = new ImportOptions();
            o.BatchSize = (int)P.Num(a, "batchSize", o.BatchSize);
            o.CellSize = P.Num(a, "cellSize", o.CellSize);
            o.GroundSnap = P.Num(a, "groundSnap", o.GroundSnap);
            o.MinLength = P.Num(a, "minLength", o.MinLength);
            o.ShortLength = P.Num(a, "shortLength", o.ShortLength);
            o.OffsetX = P.Num(a, "offsetX", 0);
            o.OffsetZ = P.Num(a, "offsetZ", 0);
            o.KeepEmbankments = P.Bool(a, "keepEmbankments", false);
            o.Heights = P.Str(a, "heights", "flat");
            o.ExactLimit = P.Num(a, "exactLimit", o.ExactLimit);
            if (o.Heights != "flat" && o.Heights != "exact") throw new ArgumentException("heights must be \"flat\" or \"exact\"");
            return o;
        }

        static ImportJob LoadJob(Dictionary<string, object> a)
        {
            var path = P.Required(a, "plan");
            if (Directory.Exists(path)) path = Path.Combine(path, "cs1conv-plan.json");
            if (!File.Exists(path)) throw new FileNotFoundException("plan not found: " + path);
            var plan = PlanParser.Parse(File.ReadAllText(path, Encoding.UTF8));
            return ImportPlanner.Build(plan, Options(a));
        }

        sealed class PrefabTable
        {
            public readonly Dictionary<string, GameWorld.PrefabInfo> ByKey = new Dictionary<string, GameWorld.PrefabInfo>();
            public readonly List<object> Json = new List<object>();
            public int Missing;
        }

        static PrefabTable LookupPrefabs(GameWorld w, ImportJob job)
        {
            var t = new PrefabTable();
            var keys = new List<string>(job.UsedPrefabKeys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                var p = job.Plan.Prefabs[k];
                var info = w.LookupPrefab(p);
                t.ByKey[k] = info;
                int uses = 0;
                foreach (var s in job.Segments) if (s.PrefabKey == k) uses++;
                var row = new Dictionary<string, object>
                {
                    { "key", k }, { "type", p.Type }, { "name", p.Name }, { "source", p.Source }, { "segments", uses }, { "found", info.Found },
                };
                if (info.Found) { row["width"] = info.Width; row["elevation_limit"] = info.ElevationLimit; }
                else
                {
                    t.Missing++;
                    row["hint"] = p.Type == "RoadBuilderPrefab"
                        ? "Road Builder config '" + p.Name + "' is not loaded: copy the package's roadbuilder/" + p.Name + ".json to ModsData/RoadBuilder before starting CS2 (Road Builder reads its configs once, at startup)"
                        : "no " + p.Type + " named '" + p.Name + "' in this game";
                }
                t.Json.Add(row);
            }
            return t;
        }

        static Dictionary<string, object> Bounds(ImportJob job)
        {
            var o = job.Options;
            bool any = job.Nodes.Count > 0;
            return new Dictionary<string, object>
            {
                { "crop", new Dictionary<string, object> { { "centerX", job.Plan.CropCenterX }, { "centerZ", job.Plan.CropCenterZ }, { "size", job.Plan.CropSize } } },
                { "cs2_min", any ? new List<object> { R(job.MinX), R(job.MinZ) } : null },
                { "cs2_max", any ? new List<object> { R(job.MaxX), R(job.MaxZ) } : null },
                { "inside_cs2_map", !any || (Math.Max(Math.Abs(job.MinX), Math.Abs(job.MaxX)) <= o.MapHalfSize && Math.Max(Math.Abs(job.MinZ), Math.Abs(job.MaxZ)) <= o.MapHalfSize) },
                { "map_half_size", o.MapHalfSize }, { "offset", new List<object> { o.OffsetX, o.OffsetZ } }, { "heights", o.Heights },
            };
        }

        static Dictionary<string, object> PlanSummary(ImportJob job)
        {
            int ground = 0, elevated = 0, tunnel = 0;
            foreach (var s in job.Segments)
            {
                if (s.Start.Elevation > 0 || s.End.Elevation > 0) elevated++;
                else if (s.Start.Elevation < 0 || s.End.Elevation < 0) tunnel++;
                else ground++;
            }
            return new Dictionary<string, object>
            {
                { "source", job.Plan.SourceSave ?? job.Plan.SourceCity },
                { "segments_in_plan", job.Plan.Segments.Count }, { "nodes_in_plan", job.Plan.Nodes.Count },
                { "segments_to_build", job.Segments.Count }, { "nodes_to_build", job.Nodes.Count },
                { "segments_skipped", job.Skipped.Count }, { "skipped_by_reason", ImportPlanner.CountByKind(job.Skipped) },
                { "ground", ground }, { "elevated", elevated }, { "tunnel", tunnel },
                { "batches", job.Batches.Count }, { "batch_size", job.Options.BatchSize },
            };
        }

        // ---------------------------------------------------------------- check

        static object Check(Dictionary<string, object> a)
        {
            var job = LoadJob(a);
            var w = new GameWorld();
            var prefabs = LookupPrefabs(w, job);
            double m = P.Num(a, "margin", 50);
            int owned;
            var existing = job.Nodes.Count > 0 ? w.EdgesInBox(job.MinX - m, job.MinZ - m, job.MaxX + m, job.MaxZ + m, out owned).Count : (owned = 0);
            return new Dictionary<string, object>
            {
                { "op", "check" }, { "importer", Build },
                { "plan", PlanSummary(job) }, { "bounds", Bounds(job) },
                { "prefabs", prefabs.Json }, { "prefabs_missing", prefabs.Missing },
                { "existing_edges_in_area", existing }, { "existing_owned_edges_in_area", owned },
                { "warnings_by_kind", ImportPlanner.CountByKind(job.Warnings) },
                { "warnings", ImportPlanner.Samples(job.Warnings, 5) },
                { "skipped", ImportPlanner.Samples(job.Skipped, 5) },
                { "temp", w.TempCounts() },
            };
        }

        // ---------------------------------------------------------------- inspect

        /// <summary>Real nodes within r of (x, z) with their edges (prefab, other end, length): for debugging a failure.</summary>
        static object Inspect(Dictionary<string, object> a)
        {
            float x = (float)P.Num(a, "x", 0), z = (float)P.Num(a, "z", 0), r = (float)P.Num(a, "r", 30);
            var w = new GameWorld();
            var rows = new List<object>();
            using (var q = w.Em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Game.Net.Node>() },
                None = new[] { ComponentType.ReadOnly<Game.Tools.Temp>(), ComponentType.ReadOnly<Game.Common.Deleted>() },
            }))
            using (var es = q.ToEntityArray(Unity.Collections.Allocator.Temp))
            using (var ns = q.ToComponentDataArray<Game.Net.Node>(Unity.Collections.Allocator.Temp))
                for (int i = 0; i < es.Length; i++)
                {
                    var p = ns[i].m_Position;
                    if (math.distance(p.xz, new float2(x, z)) > r) continue;
                    var edges = new List<object>();
                    foreach (var e in w.EdgesOf(es[i]))
                    {
                        var other = w.OtherEnd(e, es[i]);
                        var op = w.Em.GetComponentData<Game.Net.Node>(other).m_Position;
                        edges.Add(new Dictionary<string, object>
                        {
                            { "edge", GameWorld.Id(e) }, { "prefab", w.Prefabs.GetPrefabName(w.Em.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab) },
                            { "other", GameWorld.Id(other) }, { "other_pos", new List<object> { R(op.x), R(op.y), R(op.z) } },
                            { "length", R(w.Em.GetComponentData<Game.Net.Curve>(e).m_Length) },
                        });
                    }
                    rows.Add(new Dictionary<string, object> { { "node", GameWorld.Id(es[i]) }, { "pos", new List<object> { R(p.x), R(p.y), R(p.z) } }, { "edges", edges } });
                }
            return new Dictionary<string, object> { { "op", "inspect" }, { "nodes", rows } };
        }

        // ---------------------------------------------------------------- terrain

        /// <summary>Terrain height range and water coverage in a square ({x, z, size=3200, step=50}): for picking a flat map.</summary>
        static object TerrainStats(Dictionary<string, object> a)
        {
            double cx = P.Num(a, "x", 0), cz = P.Num(a, "z", 0), size = P.Num(a, "size", 3200), step = P.Num(a, "step", 50);
            var w = new GameWorld();
            w.RefreshTerrain();
            var water = w.World.GetOrCreateSystemManaged<Game.Simulation.WaterSystem>();
            var surface = water.GetSurfaceData(out var dep);
            dep.Complete();
            double lo = double.MaxValue, hi = double.MinValue, sum = 0;
            int n = 0, wet = 0;
            for (double x = cx - size / 2; x <= cx + size / 2; x += step)
                for (double z = cz - size / 2; z <= cz + size / 2; z += step)
                {
                    double y = w.TerrainHeight(x, z);
                    lo = Math.Min(lo, y); hi = Math.Max(hi, y); sum += y; n++;
                    float depth = Game.Simulation.WaterUtils.SampleDepth(ref surface, new float3((float)x, 0f, (float)z));
                    if (depth > 0.5f) wet++;
                }
            int owned;
            int edges = w.EdgesInBox(cx - size / 2, cz - size / 2, cx + size / 2, cz + size / 2, out owned).Count;
            return new Dictionary<string, object>
            {
                { "op", "terrain" }, { "samples", n }, { "min", R(lo) }, { "max", R(hi) }, { "range", R(hi - lo) }, { "mean", R(sum / n) },
                { "water_fraction", R((double)wet / n) }, { "edges_in_area", edges }, { "owned_edges_in_area", owned },
            };
        }

        // ---------------------------------------------------------------- clear

        static object ClearOp(Dictionary<string, object> a)
        {
            double x0, z0, x1, z1, m = P.Num(a, "margin", 50);
            if (P.Has(a, "plan"))
            {
                var job = LoadJob(a);
                x0 = job.MinX - m; z0 = job.MinZ - m; x1 = job.MaxX + m; z1 = job.MaxZ + m;
            }
            else { x0 = P.Num(a, "x0", 0); z0 = P.Num(a, "z0", 0); x1 = P.Num(a, "x1", 0); z1 = P.Num(a, "z1", 0); }
            var w = new GameWorld();
            return Clear(w, x0, z0, x1, z1, null);
        }

        /// <summary>
        /// Bulldozes the top-level roads in a box over a few frames: Deleted on the edges and their sub-objects, then
        /// orphaned nodes are deleted and the surviving end nodes refreshed (Road Builder's delete pattern). Edges owned by
        /// buildings are left alone and counted.
        /// </summary>
        static IEnumerator Clear(GameWorld w, double x0, double z0, double x1, double z1, Dictionary<string, object> into)
        {
            var sw = Stopwatch.StartNew();
            int owned;
            var edges = w.EdgesInBox(x0, z0, x1, z1, out owned);
            var nodes = new HashSet<Entity>();
            foreach (var e in edges)
            {
                var ed = w.Em.GetComponentData<Game.Net.Edge>(e);
                nodes.Add(ed.m_Start);
                nodes.Add(ed.m_End);
            }
            foreach (var e in edges) w.Delete(e);
            Log.Info("clear: deleting " + edges.Count + " edges in [" + x0 + "," + z0 + " .. " + x1 + "," + z1 + "]");
            yield return null;

            int nodesDeleted = 0, nodesKept = 0;
            foreach (var n in nodes)
            {
                if (!w.IsReal(n)) continue;
                var left = w.EdgesOf(n);
                if (left.Count == 0) { w.Delete(n); nodesDeleted++; continue; }
                nodesKept++;
                w.Em.AddComponent<Game.Common.Updated>(n);
                foreach (var e in left)
                {
                    w.Em.AddComponent<Game.Common.Updated>(e);
                    var ed = w.Em.GetComponentData<Game.Net.Edge>(e);
                    if (w.IsReal(ed.m_Start)) w.Em.AddComponent<Game.Common.Updated>(ed.m_Start);
                    if (w.IsReal(ed.m_End)) w.Em.AddComponent<Game.Common.Updated>(ed.m_End);
                }
            }
            for (int i = 0; i < 5; i++) yield return null;
            int ownedAfter;
            int remaining = w.EdgesInBox(x0, z0, x1, z1, out ownedAfter).Count;
            var r = new Dictionary<string, object>
            {
                { "op", "clear" }, { "box", new List<object> { R(x0), R(z0), R(x1), R(z1) } },
                { "edges_deleted", edges.Count }, { "nodes_deleted", nodesDeleted }, { "boundary_nodes_kept", nodesKept },
                { "owned_edges_left", owned }, { "edges_remaining", remaining }, { "seconds", R(sw.Elapsed.TotalSeconds) },
                { "temp", w.TempCounts() },
            };
            if (into != null) foreach (var kv in r) into[kv.Key] = kv.Value;
            yield return r;
        }

        // ---------------------------------------------------------------- import

        sealed class NodeState
        {
            public NodeJob Job;
            public float3 Pos;         // the one CS2 position used for every segment at this node
            public Entity Entity;      // real node once built (null before)
            public bool Failed;
        }

        static IEnumerator Import(Dictionary<string, object> a)
        {
            var total = Stopwatch.StartNew();
            // in-process callers (the mod's options page) can pass a progress callback; exec callers can't (JSON args)
            var progress = a != null && a.TryGetValue("_progress", out object pcb) ? pcb as Action<string> : null;
            var job = LoadJob(a);
            var w = new GameWorld();
            int settle = Math.Max(1, (int)P.Num(a, "settleFrames", 2));
            int verifyFrames = Math.Max(1, (int)P.Num(a, "verifyFrames", 30));
            // trace: segment ids of each batch, appended (and flushed) before the batch is created, to find the batch
            // in which the game dies (a native crash leaves no managed stack)
            var trace = P.Str(a, "trace", null);
            double margin = P.Num(a, "margin", 50);
            var report = new Dictionary<string, object>
            {
                { "op", "import" }, { "importer", Build }, { "started", DateTime.UtcNow.ToString("o") },
                { "plan", PlanSummary(job) }, { "bounds", Bounds(job) },
            };
            var tempBefore = w.TempCounts();
            report["temp_before"] = tempBefore;
            Log.Info("import: " + job.Segments.Count + " segments, " + job.Nodes.Count + " nodes, " + job.Batches.Count + " batches");

            // 1. prefabs
            var prefabs = LookupPrefabs(w, job);
            report["prefabs"] = prefabs.Json;
            report["prefabs_missing"] = prefabs.Missing;

            // 2. optional clear
            if (P.Bool(a, "clear", false) && job.Nodes.Count > 0)
            {
                var cleared = new Dictionary<string, object>();
                var c = Clear(w, job.MinX - margin, job.MinZ - margin, job.MaxX + margin, job.MaxZ + margin, cleared);
                while (c.MoveNext()) yield return null;
                report["clear"] = cleared;
            }
            else if (job.Nodes.Count > 0)
            {
                int owned;
                int existing = w.EdgesInBox(job.MinX - margin, job.MinZ - margin, job.MaxX + margin, job.MaxZ + margin, out owned).Count;
                report["existing_edges_in_area"] = existing;
                if (existing > 0) job.Warnings.Add(new Issue("warning", "existing-roads-in-area", existing + " CS2 roads already cross the import area; planned roads that cross them can turn into Temp ghosts (run with clear=true)"));
            }

            // 3. one position per node (terrain + elevation), computed once
            w.RefreshTerrain();
            var nodes = new Dictionary<long, NodeState>();
            foreach (var n in job.Nodes.Values)
            {
                float y = w.TerrainHeight(n.X, n.Z) + (float)n.PositionHeight;
                nodes[n.Id] = new NodeState { Job = n, Pos = new float3((float)n.X, y, (float)n.Z) };
            }

            // 4. batches
            // plan nodes at the same spot (merged-nodes warning) share one CS2 node: once one is built, the other reuses it
            var builtAt = new Dictionary<string, NodeState>();
            var failures = new List<Issue>();
            var batchRows = new List<object>();
            var attempted = new List<SegmentJob>();
            for (int bi = 0; bi < job.Batches.Count; bi++)
            {
                var batch = job.Batches[bi];
                if (!string.IsNullOrEmpty(trace))
                {
                    var ids = new StringBuilder("batch " + bi + ":");
                    foreach (var s in batch) ids.Append(' ').Append(s.Id);
                    File.AppendAllText(trace, ids.Append('\n').ToString());
                }
                var sw = Stopwatch.StartNew();
                int created = 0, attached = 0;
                var touched = new HashSet<long>();
                foreach (var s in batch)
                {
                    var pf = prefabs.ByKey[s.PrefabKey];
                    if (!pf.Found)
                    {
                        failures.Add(new Issue("error", "prefab-missing", "segment " + s.Id + ": prefab " + pf.Plan.Type + " '" + pf.Plan.Name + "' is not loaded") { Segment = s.Id });
                        continue;
                    }
                    var a0 = Twin(nodes[s.Start.Id], builtAt);
                    var b0 = Twin(nodes[s.End.Id], builtAt);
                    if (a0.Failed || b0.Failed)
                    {
                        failures.Add(new Issue("error", "node-not-created", "segment " + s.Id + ": an end node failed to build in an earlier batch") { Segment = s.Id });
                        continue;
                    }
                    var curve = ImportPlanner.Curve(s, V(a0.Pos), V(b0.Pos));
                    w.CreateSegment(pf.Entity, curve, a0.Entity, b0.Entity, (float)s.Start.Elevation, (float)s.End.Elevation, (float)s.MiddleElevation);
                    created++;
                    if (a0.Entity != Entity.Null) attached++;
                    if (b0.Entity != Entity.Null) attached++;
                    attempted.Add(s);
                    touched.Add(s.Start.Id);
                    touched.Add(s.End.Id);
                }
                double createMs = sw.Elapsed.TotalMilliseconds;
                int frames = 0;
                for (int f = 0; f < settle; f++) { yield return null; frames++; }

                // resolve this batch's new nodes to real entities (the node-id -> entity map for later batches)
                var resolve = Stopwatch.StartNew();
                var idx = w.IndexRealNodes();
                int resolved = 0, missing = 0;
                foreach (var id in touched)
                {
                    var ns = nodes[id];
                    if (ns.Entity != Entity.Null && w.IsReal(ns.Entity)) continue;
                    float3 at;
                    var e = idx.Find(ns.Pos, 1.0f, 3.0f, out at);
                    if (e == Entity.Null)
                    {
                        // one more frame before giving up on it
                        ns.Entity = Entity.Null;
                        missing++;
                        continue;
                    }
                    builtAt[PosKey(ns.Pos)] = ns;
                    ns.Entity = e;
                    ns.Pos = at; // later segments attach at the node's real position
                    resolved++;
                }
                if (missing > 0)
                {
                    yield return null; frames++;
                    idx = w.IndexRealNodes();
                    missing = 0;
                    foreach (var id in touched)
                    {
                        var ns = nodes[id];
                        if (ns.Entity != Entity.Null) continue;
                        float3 at;
                        var e = idx.Find(ns.Pos, 1.0f, 3.0f, out at);
                        if (e == Entity.Null)
                        {
                            ns.Failed = true;
                            missing++;
                            failures.Add(new Issue("error", "node-not-created", "node " + id + " at " + V(ns.Pos) + " has no CS2 node after its batch") { Node = id });
                            continue;
                        }
                        builtAt[PosKey(ns.Pos)] = ns;
                        ns.Entity = e; ns.Pos = at; resolved++;
                    }
                }
                var row = new Dictionary<string, object>
                {
                    { "batch", bi }, { "segments", batch.Count }, { "created", created }, { "attached_ends", attached },
                    { "nodes_resolved", resolved }, { "nodes_missing", missing }, { "frames", frames },
                    { "create_ms", R(createMs) }, { "resolve_ms", R(resolve.Elapsed.TotalMilliseconds) }, { "wall_ms", R(sw.Elapsed.TotalMilliseconds) },
                };
                batchRows.Add(row);
                Log.Info("import: batch " + (bi + 1) + "/" + job.Batches.Count + " " + MiniJson.Write(row));
                if (progress != null) progress("batch " + (bi + 1) + " of " + job.Batches.Count + ": " + attempted.Count + " of " + job.Segments.Count + " segments placed");
            }
            report["batches"] = batchRows;

            // 5. verify after the game has settled
            if (progress != null) progress("checking what was built");
            for (int f = 0; f < verifyFrames; f++) yield return null;
            var verify = Verify(w, job, prefabs, nodes, attempted);
            failures.AddRange((List<Issue>)verify["_failures"]);
            verify.Remove("_failures");
            report["result"] = verify;
            report["failures_by_reason"] = ImportPlanner.CountByKind(failures);
            report["failures"] = ImportPlanner.Samples(failures, 10);
            report["warnings_by_kind"] = ImportPlanner.CountByKind(job.Warnings);
            report["warnings"] = ImportPlanner.Samples(job.Warnings, 5);
            report["skipped"] = ImportPlanner.Samples(job.Skipped, 5);
            report["temp_after"] = w.TempCounts();
            report["seconds"] = R(total.Elapsed.TotalSeconds);
            Log.Info("import done: " + MiniJson.Write(verify));
            var reportPath = P.Str(a, "report", null);
            if (!string.IsNullOrEmpty(reportPath))
            {
                var dir = Path.GetDirectoryName(reportPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(reportPath, MiniJson.Write(report), new UTF8Encoding(false));
            }
            yield return report;
        }

        // ---------------------------------------------------------------- verify

        static object VerifyOnly(Dictionary<string, object> a)
        {
            var job = LoadJob(a);
            var w = new GameWorld();
            var prefabs = LookupPrefabs(w, job);
            var idx = w.IndexRealNodes();
            w.RefreshTerrain();
            var nodes = new Dictionary<long, NodeState>();
            foreach (var n in job.Nodes.Values)
            {
                // the roads have reshaped the terrain since the import, so match on x/z and take the nearest height
                var pos = new float3((float)n.X, w.TerrainHeight(n.X, n.Z) + (float)n.PositionHeight, (float)n.Z);
                float3 at;
                var e = idx.Find(pos, 1.0f, 30.0f, out at);
                nodes[n.Id] = new NodeState { Job = n, Pos = e != Entity.Null ? at : pos, Entity = e, Failed = e == Entity.Null };
            }
            var attempted = new List<SegmentJob>();
            foreach (var s in job.Segments) if (prefabs.ByKey[s.PrefabKey].Found) attempted.Add(s);
            var v = Verify(w, job, prefabs, nodes, attempted);
            var failures = (List<Issue>)v["_failures"];
            v.Remove("_failures");
            v["op"] = "verify";
            v["failures_by_reason"] = ImportPlanner.CountByKind(failures);
            v["failures"] = ImportPlanner.Samples(failures, 10);
            v["temp"] = w.TempCounts();
            return v;
        }

        /// <summary>
        /// For every attempted segment, walks from its start node's entity along real edges (through intermediate CS2
        /// nodes that are not plan nodes and join exactly two edges, in case CS2 split a long course) until it reaches the
        /// end node's entity. Also counts elevated/tunnel compositions and edges whose prefab differs from the plan.
        /// </summary>
        static Dictionary<string, object> Verify(GameWorld w, ImportJob job, PrefabTable prefabs, Dictionary<long, NodeState> nodes, List<SegmentJob> attempted)
        {
            var failures = new List<Issue>();
            // re-resolve nodes whose entity went away (CS2 can replace a node when edges attach)
            var idx = w.IndexRealNodes();
            var planNodeEntities = new HashSet<Entity>();
            int nodesOk = 0;
            foreach (var ns in nodes.Values)
            {
                if (ns.Entity == Entity.Null || !w.IsReal(ns.Entity))
                {
                    float3 at;
                    ns.Entity = idx.Find(ns.Pos, 1.0f, 3.0f, out at);
                }
                if (ns.Entity != Entity.Null) { planNodeEntities.Add(ns.Entity); nodesOk++; }
            }
            int built = 0, pieces = 0, wrongPrefab = 0, elevatedEdges = 0, tunnelEdges = 0, wantElevated = 0, wantTunnel = 0, raisedSides = 0, loweredSides = 0;
            var builtEdges = new HashSet<Entity>();
            foreach (var s in attempted)
            {
                var a = nodes[s.Start.Id].Entity;
                var b = nodes[s.End.Id].Entity;
                // only CS1 bridges/elevated and tunnels are expected to get those compositions; terraformed ground roads
                // (trenches, embankments) become retaining walls or quays in CS2, which is what they were in CS1
                string kind = s.Source.Kind;
                bool wantsElev = (kind == "elevated" || kind == "bridge") && s.MiddleElevation >= 8 && s.Start.Elevation > 0 && s.End.Elevation > 0;
                bool wantsTunnel = kind == "tunnel" && s.MiddleElevation <= -8 && s.Start.Elevation < 0 && s.End.Elevation < 0;
                if (wantsElev) wantElevated++;
                if (wantsTunnel) wantTunnel++;
                if (a == Entity.Null || b == Entity.Null)
                {
                    failures.Add(new Issue("error", "node-missing", "segment " + s.Id + ": no CS2 node at its " + (a == Entity.Null ? "start" : "end")) { Segment = s.Id });
                    continue;
                }
                var path = FindPath(w, a, b, planNodeEntities, builtEdges);
                if (path == null)
                {
                    failures.Add(new Issue("error", "edge-missing", "segment " + s.Id + ": no CS2 edge between nodes " + s.Start.Id + " and " + s.End.Id) { Segment = s.Id });
                    continue;
                }
                built++;
                pieces += path.Count;
                var expected = prefabs.ByKey[s.PrefabKey].Entity;
                bool prefabOk = true, elev = false, tun = false;
                foreach (var e in path)
                {
                    builtEdges.Add(e);
                    if (w.Em.GetComponentData<Game.Prefabs.PrefabRef>(e).m_Prefab != expected) prefabOk = false;
                    var g = w.EdgeGeneralFlags(e);
                    if ((g & Game.Prefabs.CompositionFlags.General.Elevated) != 0) elev = true;
                    if ((g & Game.Prefabs.CompositionFlags.General.Tunnel) != 0) tun = true;
                    var sides = w.EdgeSideFlags(e);
                    if ((sides & Game.Prefabs.CompositionFlags.Side.Raised) != 0) raisedSides++;
                    if ((sides & Game.Prefabs.CompositionFlags.Side.Lowered) != 0) loweredSides++;
                }
                if (!prefabOk) { wrongPrefab++; failures.Add(new Issue("warning", "wrong-prefab", "segment " + s.Id + ": the CS2 edge has a different prefab than planned") { Segment = s.Id }); }
                if (elev) elevatedEdges++;
                if (tun) tunnelEdges++;
                if (wantsElev && !elev) failures.Add(new Issue("warning", "not-elevated", "segment " + s.Id + " should be a bridge/elevated road but has no elevated composition") { Segment = s.Id });
                if (wantsTunnel && !tun) failures.Add(new Issue("warning", "not-tunnel", "segment " + s.Id + " should be a tunnel but has no tunnel composition") { Segment = s.Id });
            }
            // CS2 edges in the area that do not belong to any plan segment
            int extra = 0;
            if (job.Nodes.Count > 0)
            {
                int owned;
                foreach (var e in w.EdgesInBox(job.MinX - 5, job.MinZ - 5, job.MaxX + 5, job.MaxZ + 5, out owned))
                    if (!builtEdges.Contains(e)) extra++;
            }
            return new Dictionary<string, object>
            {
                { "segments_planned", job.Segments.Count }, { "segments_attempted", attempted.Count }, { "segments_built", built },
                { "segments_failed", attempted.Count - built }, { "cs2_edges_for_plan", pieces }, { "nodes_found", nodesOk }, { "nodes_planned", nodes.Count },
                { "wrong_prefab", wrongPrefab }, { "elevated_planned", wantElevated }, { "elevated_built", elevatedEdges },
                { "tunnel_planned", wantTunnel }, { "tunnel_built", tunnelEdges }, { "other_cs2_edges_in_area", extra },
                // edges with retaining-wall sides: buildings can't connect to a Raised/Lowered side
                { "edges_raised_sides", raisedSides }, { "edges_lowered_sides", loweredSides },
                { "_failures", failures },
            };
        }

        static List<Entity> FindPath(GameWorld w, Entity from, Entity to, HashSet<Entity> planNodes, HashSet<Entity> used)
        {
            foreach (var first in w.EdgesOf(from))
            {
                if (used.Contains(first)) continue; // parallel segments: each claims its own edge
                var path = new List<Entity> { first };
                Entity prevEdge = first;
                Entity cur = w.OtherEnd(first, from);
                for (int step = 0; step < 32; step++)
                {
                    if (cur == to) return path;
                    if (planNodes.Contains(cur)) break;
                    var next = w.EdgesOf(cur);
                    if (next.Count != 2) break;
                    var e = next[0] == prevEdge ? next[1] : next[0];
                    path.Add(e);
                    prevEdge = e;
                    cur = w.OtherEnd(e, cur);
                }
            }
            // a parallel segment whose edge was already claimed: accept a shared edge rather than reporting it missing
            if (used.Count > 0)
                foreach (var first in w.EdgesOf(from))
                    if (used.Contains(first) && w.OtherEnd(first, from) == to) return new List<Entity> { first };
            return null;
        }

        static string PosKey(float3 p) { return Math.Round(p.x, 2) + "," + Math.Round(p.y, 1) + "," + Math.Round(p.z, 2); }

        /// <summary>A node not built yet whose position equals an already built node's: take over that node.</summary>
        static NodeState Twin(NodeState ns, Dictionary<string, NodeState> builtAt)
        {
            NodeState t;
            if (ns.Entity == Entity.Null && !ns.Failed && builtAt.TryGetValue(PosKey(ns.Pos), out t) && t != ns && t.Entity != Entity.Null)
            {
                ns.Entity = t.Entity;
                ns.Pos = t.Pos;
            }
            return ns;
        }

        static V3 V(float3 p) { return new V3(p.x, p.y, p.z); }
        static double R(double d) { return Math.Round(d, 2); }
    }
}
