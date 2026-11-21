// Turns a parsed plan into an import job: validated segments, one elevation per node, curves and spatial batches.
// Pure logic (no game types); the game side only supplies terrain heights and creates the definitions.
using System;
using System.Collections.Generic;

namespace Cs2Importer.Core
{
    public sealed class NodeJob
    {
        public long Id;
        public double X, Z;
        /// <summary>Height above the CS2 terrain (metres): 0 for ground nodes, &gt; 0 elevated, &lt; 0 tunnel.</summary>
        public double Elevation;
        public double Cs1HeightAboveTerrain;
        public int Uses;
    }

    public sealed class SegmentJob
    {
        public int Index;              // position in ImportJob.Segments
        public PlanSegment Source;
        public long Id { get { return Source.Id; } }
        public string PrefabKey { get { return Source.Prefab; } }
        public NodeJob Start, End;
        public double MiddleElevation;
        public double ChordLength;
    }

    public sealed class ImportOptions
    {
        public int BatchSize = 400;
        public double CellSize = 400;           // metres, spatial batching grid
        public double GroundSnap = 1.0;         // |height above terrain| below this is ground (0)
        public double TerrainFeature = 4.0;     // a ground node at least this far above/below CS1's terrain keeps that offset
        public double FeatureMin = -11.5;       // ...clamped so CS2 keeps it an open cut (a tunnel starts at -12 m = -3 x limit)
        public double FeatureMax = 7.5;         // ...and a quay/embankment (a bridge with pillars starts at +8 m = 2 x limit)
        public double MapHalfSize = 7168;       // CS2 playable area is 14 336 m square, centred on the origin
        public double MinLength = 0.5;          // shorter segments are skipped
        public double ShortLength = 4.0;        // shorter segments are built but reported
        public double MergeDistance = 0.05;     // distinct CS1 nodes closer than this end up as one CS2 node
        public double OffsetX, OffsetZ;         // test placement: shifts the whole plan (0 = the plan's own CS2 coordinates)
    }

    public sealed class ImportJob
    {
        public Plan Plan;
        public ImportOptions Options;
        public readonly Dictionary<long, NodeJob> Nodes = new Dictionary<long, NodeJob>();
        public readonly List<SegmentJob> Segments = new List<SegmentJob>();
        public readonly List<List<SegmentJob>> Batches = new List<List<SegmentJob>>();
        public readonly List<Issue> Skipped = new List<Issue>();   // one per segment not attempted
        public readonly List<Issue> Warnings = new List<Issue>();
        public double MinX = double.MaxValue, MaxX = double.MinValue, MinZ = double.MaxValue, MaxZ = double.MinValue;
        public readonly HashSet<string> UsedPrefabKeys = new HashSet<string>();
    }

    public static class ImportPlanner
    {
        public static ImportJob Build(Plan plan, ImportOptions opt)
        {
            opt = opt ?? new ImportOptions();
            var job = new ImportJob { Plan = plan, Options = opt };
            job.Warnings.AddRange(plan.ParseIssues);

            var pairs = new Dictionary<string, long>();
            foreach (var s in plan.Segments)
            {
                PlanNode a, b;
                if (!plan.Nodes.TryGetValue(s.Start, out a) || !plan.Nodes.TryGetValue(s.End, out b))
                {
                    Skip(job, s, "missing-node", "segment " + s.Id + " references node " + (plan.Nodes.ContainsKey(s.Start) ? s.End : s.Start) + ", which is not in the plan");
                    continue;
                }
                if (s.Prefab == null || !plan.Prefabs.ContainsKey(s.Prefab))
                {
                    Skip(job, s, "unknown-prefab-key", "segment " + s.Id + " uses prefab key '" + s.Prefab + "', which is not in plan.prefabs");
                    continue;
                }
                if (s.Start == s.End) { Skip(job, s, "degenerate", "segment " + s.Id + " starts and ends at node " + s.Start); continue; }
                if (Outside(a, opt) || Outside(b, opt))
                {
                    Skip(job, s, "outside-cs2-map", "segment " + s.Id + " has an end outside the CS2 map (|x| or |z| > " + opt.MapHalfSize + " m)");
                    continue;
                }
                double chord = new V3(b.X - a.X, 0, b.Z - a.Z).LengthXZ;
                if (chord < opt.MinLength) { Skip(job, s, "too-short", "segment " + s.Id + " is " + chord.ToString("0.00") + " m long"); continue; }
                if (chord < opt.ShortLength)
                    job.Warnings.Add(new Issue("info", "short-segment", "segment " + s.Id + " is only " + chord.ToString("0.0") + " m long; CS2 may merge or drop it") { Segment = s.Id });

                string key = Math.Min(s.Start, s.End) + "-" + Math.Max(s.Start, s.End);
                long other;
                if (pairs.TryGetValue(key, out other))
                    job.Warnings.Add(new Issue("warning", "parallel-segments", "segments " + other + " and " + s.Id + " join the same two nodes") { Segment = s.Id });
                else pairs[key] = s.Id;

                var seg = new SegmentJob { Index = job.Segments.Count, Source = s, Start = NodeFor(job, a), End = NodeFor(job, b), ChordLength = chord };
                seg.Start.Uses++;
                seg.End.Uses++;
                job.Segments.Add(seg);
                job.UsedPrefabKeys.Add(s.Prefab);
            }

            AssignElevations(job);
            foreach (var n in job.Nodes.Values)
            {
                job.MinX = Math.Min(job.MinX, n.X); job.MaxX = Math.Max(job.MaxX, n.X);
                job.MinZ = Math.Min(job.MinZ, n.Z); job.MaxZ = Math.Max(job.MaxZ, n.Z);
            }
            FindMergedNodes(job);
            MakeBatches(job);
            return job;
        }

        static bool Outside(PlanNode n, ImportOptions o) { return Math.Abs(n.X + o.OffsetX) > o.MapHalfSize || Math.Abs(n.Z + o.OffsetZ) > o.MapHalfSize; }

        static void Skip(ImportJob job, PlanSegment s, string kind, string msg)
        {
            job.Skipped.Add(new Issue("warning", kind, msg) { Segment = s.Id });
        }

        static NodeJob NodeFor(ImportJob job, PlanNode n)
        {
            NodeJob j;
            if (job.Nodes.TryGetValue(n.Id, out j)) return j;
            j = new NodeJob { Id = n.Id, X = n.X + job.Options.OffsetX, Z = n.Z + job.Options.OffsetZ, Cs1HeightAboveTerrain = n.HeightAboveTerrain };
            job.Nodes[n.Id] = j;
            return j;
        }

        /// <summary>
        /// One elevation per node, from the segment ends that use it (elevationStart/End) and, for big offsets only, the
        /// node's CS1 heightAboveTerrain:
        /// - CS1 ground roads often sit a metre or two above or below CS1's terrain (smoothing, small embankments); on CS2's
        ///   terrain those offsets would sink or float the road, so they count as ground.
        /// - Offsets of TerrainFeature (4 m) or more are deliberate terraforming: a highway in an 11 m trench, a road on an
        ///   embankment. They are kept, so CS2 builds retaining walls or quays and roads that cross over them stay
        ///   grade-separated (otherwise CS2 joins them with an unplanned junction).
        /// - The node takes the candidate with the largest magnitude. Segment ends that disagree by more than 1 m are reported.
        /// </summary>
        static void AssignElevations(ImportJob job)
        {
            var ends = new Dictionary<long, List<double>>();
            foreach (var s in job.Segments)
            {
                Add(ends, s.Start.Id, NormalizeElevation(s.Source.ElevationStart, job.Options.GroundSnap));
                Add(ends, s.End.Id, NormalizeElevation(s.Source.ElevationEnd, job.Options.GroundSnap));
            }
            foreach (var n in job.Nodes.Values)
            {
                List<double> l;
                if (!ends.TryGetValue(n.Id, out l)) continue;
                double feature = Math.Abs(n.Cs1HeightAboveTerrain) >= job.Options.TerrainFeature
                    ? Math.Max(job.Options.FeatureMin, Math.Min(job.Options.FeatureMax, Math.Round(n.Cs1HeightAboveTerrain, 2))) : 0;
                double best = 0, lo = double.MaxValue, hi = double.MinValue;
                foreach (var e in l)
                {
                    if (Math.Abs(e) > Math.Abs(best)) best = e;
                    if (e != 0) { lo = Math.Min(lo, e); hi = Math.Max(hi, e); }
                }
                if (Math.Abs(feature) > Math.Abs(best)) best = feature;
                n.Elevation = best;
                if (hi - lo > 1)
                    job.Warnings.Add(new Issue("info", "elevation-mismatch", "segments at node " + n.Id + " disagree on its elevation (" + lo.ToString("0.0") + " .. " + hi.ToString("0.0") + " m); using " + best.ToString("0.0")) { Node = n.Id });
            }
            foreach (var s in job.Segments) s.MiddleElevation = (s.Start.Elevation + s.End.Elevation) / 2;
        }

        static void Add(Dictionary<long, List<double>> d, long k, double v)
        {
            List<double> l;
            if (!d.TryGetValue(k, out l)) d[k] = l = new List<double>();
            l.Add(v);
        }

        /// <summary>Elevations under the snap distance count as ground (0).</summary>
        public static double NormalizeElevation(double h, double snap)
        {
            if (double.IsNaN(h) || Math.Abs(h) < snap) return 0;
            return Math.Round(h, 2);
        }

        /// <summary>Distinct plan nodes this close together become one CS2 node (positions merge in the same frame).</summary>
        static void FindMergedNodes(ImportJob job)
        {
            double d = job.Options.MergeDistance;
            if (d <= 0) return;
            var grid = new Dictionary<long, List<NodeJob>>();
            double cell = Math.Max(d * 4, 1);
            foreach (var n in job.Nodes.Values)
            {
                long cx = (long)Math.Floor(n.X / cell), cz = (long)Math.Floor(n.Z / cell);
                for (long ix = cx - 1; ix <= cx + 1; ix++)
                    for (long iz = cz - 1; iz <= cz + 1; iz++)
                    {
                        List<NodeJob> l;
                        if (!grid.TryGetValue(ix * 1000003 + iz, out l)) continue;
                        foreach (var m in l)
                        {
                            double dx = m.X - n.X, dz = m.Z - n.Z;
                            if (dx * dx + dz * dz <= d * d && Math.Abs(m.Elevation - n.Elevation) < 2)
                                job.Warnings.Add(new Issue("warning", "merged-nodes", "nodes " + m.Id + " and " + n.Id + " are " + Math.Sqrt(dx * dx + dz * dz).ToString("0.000") + " m apart and will become one CS2 node") { Node = n.Id });
                        }
                    }
                List<NodeJob> own;
                long k = cx * 1000003 + cz;
                if (!grid.TryGetValue(k, out own)) grid[k] = own = new List<NodeJob>();
                own.Add(n);
            }
        }

        /// <summary>
        /// Spatial batches: segments ordered by grid cell (rows of CellSize, serpentine), then cut into BatchSize chunks.
        /// Neighbouring segments land in the same batch, so most shared nodes merge in one frame and only batch
        /// boundaries need the node-entity map.
        /// </summary>
        static void MakeBatches(ImportJob job)
        {
            double cell = job.Options.CellSize;
            var order = new List<KeyValuePair<long[], SegmentJob>>();
            foreach (var s in job.Segments)
            {
                double mx = (s.Start.X + s.End.X) / 2, mz = (s.Start.Z + s.End.Z) / 2;
                long row = (long)Math.Floor(mz / cell), col = (long)Math.Floor(mx / cell);
                if ((row & 1) != 0) col = -col;
                order.Add(new KeyValuePair<long[], SegmentJob>(new[] { row, col, s.Id }, s));
            }
            order.Sort((p, q) =>
            {
                for (int i = 0; i < 3; i++) { int c = p.Key[i].CompareTo(q.Key[i]); if (c != 0) return c; }
                return 0;
            });
            int size = Math.Max(1, job.Options.BatchSize);
            List<SegmentJob> cur = null;
            foreach (var kv in order)
            {
                if (cur == null || cur.Count >= size) { cur = new List<SegmentJob>(); job.Batches.Add(cur); }
                cur.Add(kv.Value);
            }
        }

        /// <summary>
        /// The CS2 curve for a segment, given the final world positions of its two nodes. X/Z of the inner control points
        /// come from the plan's bezier (shifted with the end points). Y is the straight line between the two ends; elevated,
        /// bridge and tunnel segments also keep the CS1 curve's vertical bulge (arches). Without a bezier the curve is straight.
        /// </summary>
        public static V3[] Curve(SegmentJob s, V3 start, V3 end)
        {
            var b = s.Source.Bezier;
            V3 p1, p2;
            if (b == null)
            {
                p1 = start + (end - start) * (1.0 / 3);
                p2 = start + (end - start) * (2.0 / 3);
                return new[] { start, p1, p2, end };
            }
            // x/z: move each inner point with its nearer end point (the plan's ends should equal the node positions)
            V3 da = new V3(start.X - b[0].X, 0, start.Z - b[0].Z), dd = new V3(end.X - b[3].X, 0, end.Z - b[3].Z);
            p1 = new V3(b[1].X + da.X, 0, b[1].Z + da.Z);
            p2 = new V3(b[2].X + dd.X, 0, b[2].Z + dd.Z);
            bool keepBulge = s.Source.Kind != "ground" && (s.Start.Elevation != 0 || s.End.Elevation != 0 || s.Source.Kind == "bridge");
            double bulge1 = keepBulge ? b[1].Y - Lerp(b[0].Y, b[3].Y, 1.0 / 3) : 0;
            double bulge2 = keepBulge ? b[2].Y - Lerp(b[0].Y, b[3].Y, 2.0 / 3) : 0;
            p1.Y = Lerp(start.Y, end.Y, 1.0 / 3) + bulge1;
            p2.Y = Lerp(start.Y, end.Y, 2.0 / 3) + bulge2;
            return new[] { start, p1, p2, end };
        }

        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        /// <summary>Counts per kind, for the report.</summary>
        public static Dictionary<string, object> CountByKind(IEnumerable<Issue> issues)
        {
            var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var i in issues) { int n; d.TryGetValue(i.Kind, out n); d[i.Kind] = n + 1; }
            var r = new Dictionary<string, object>();
            foreach (var kv in d) r[kv.Key] = kv.Value;
            return r;
        }

        public static List<object> Samples(IEnumerable<Issue> issues, int perKind)
        {
            var seen = new Dictionary<string, int>();
            var r = new List<object>();
            foreach (var i in issues)
            {
                int n;
                seen.TryGetValue(i.Kind, out n);
                if (n >= perKind) continue;
                seen[i.Kind] = n + 1;
                r.Add(i.ToJson());
            }
            return r;
        }
    }
}
