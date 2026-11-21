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
        /// <summary>CS1 height of the node (plan y); y - heightAboveTerrain is CS1's terrain there, which the converted
        /// CS2 terrain reproduces. Used to estimate CS2 heights for the clearance check.</summary>
        public double Cs1Y;
        /// <summary>A conform segment meets here: the package's terrain at this node is the node's CS1 height.</summary>
        public bool Conformed;
        /// <summary>A walls node with only ground/slope segments (#61): its elevation is CS1's height above the terrain,
        /// clamped to CS2's wall limits, so it never becomes a tunnel or pillars.</summary>
        public bool WallsGround;
        /// <summary>Height above the CS2 terrain at which the node is placed: Elevation ("flat" heights), or the full CS1
        /// heightAboveTerrain ("exact" heights, once the CS1 terrain has been applied to CS2).</summary>
        public double PositionHeight;
        public int Uses;
        /// <summary>The elevation before DeepenTunnels lowered it (NaN = not deepened): CS1's tunnel depth.</summary>
        public double DeepenedFrom = double.NaN;
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
        /// <summary>
        /// Build the course end → start (DECISIONS-importer.md #55). In a left-hand CS1 city every lane keeps its place but
        /// drives the other way; building a road the other way round mirrors and reverses it in the plan frame, which is
        /// exactly that for one-way and asymmetric roads and changes nothing for symmetric two-way ones. The plan frame
        /// (Start/End, lane references, sides) stays as it is for every other step.
        /// </summary>
        public bool BuildReversed;
        /// <summary>Height mode of this ground segment: "conform", "follow", "walls", or null for plans without modes (the
        /// M2 rules: ±4 m features, trenches kept, embankments snapped). See NOTES-road-heights.md.</summary>
        public string HeightMode;
        public bool IsGround { get { return Source.Kind == "ground" || Source.Kind == "slope"; } }
        /// <summary>The plan's offNode (Node Controller shift) is applied: ImportOptions.ApplyOffNode and segments[].offNode.</summary>
        public bool OffNode;
        /// <summary>With OffNode: the bezier's start / end x/z minus its plan node's (the Node Controller shift), independent of
        /// the test offset (OffsetX/Z moves the nodes, not the bezier).</summary>
        public double OffStartX, OffStartZ, OffEndX, OffEndZ;
        /// <summary>The bezier this job builds: the plan's, or one half of it when a walls segment was split at its CS1
        /// profile (#62). Null without a plan bezier.</summary>
        public V3[] Bezier;
        /// <summary>-1 = not split; 0 = the first half (plan start → split node), 1 = the second (split node → plan end).</summary>
        public int Half = -1;
    }

    public sealed class ImportOptions
    {
        public int BatchSize = 400;
        public double CellSize = 400;           // metres, spatial batching grid
        public double GroundSnap = 1.0;         // |height above terrain| below this is ground (0)
        public double TerrainFeature = 4.0;     // a ground node at least this far above/below CS1's terrain keeps that offset
        public double FeatureMin = -11.5;       // ...clamped so CS2 keeps it an open cut (a tunnel starts at -12 m = -3 x limit)
        public double FeatureMax = 7.5;         // ...and a quay/embankment (a bridge with pillars starts at +8 m = 2 x limit)
        /// <summary>Keep embankments (positive terrain features) too. Off by default: CS2 gives those edges Raised sides
        /// (retaining walls), and buildings can't connect to them (M3 buildings worker). Trenches are always kept: they
        /// are what keeps crossing roads grade-separated.</summary>
        public bool KeepEmbankments = false;
        public double MapHalfSize = 7168;       // CS2 playable area is 14 336 m square, centred on the origin
        public double MinLength = 0.5;          // shorter segments are skipped
        public double ShortLength = 4.0;        // shorter segments are built but reported
        public double MergeDistance = 0.05;     // distinct CS1 nodes closer than this end up as one CS2 node
        public double OffsetX, OffsetZ;         // test placement: shifts the whole plan (0 = the plan's own CS2 coordinates)
        /// <summary>
        /// "flat": nodes sit at CS2 terrain + Elevation (M1; the CS2 terrain is a stand-in for CS1's).
        /// "exact": the CS1 terrain has been applied to CS2 (terrain/), so CS2 terrain + CS1 heightAboveTerrain is the CS1
        /// node height itself; every node is placed there. Elevation still drives CS2's composition choice.
        /// </summary>
        public string Heights = "flat";
        /// <summary>"exact" heights: a node whose CS1 height differs from its composition elevation by more than this keeps
        /// the composition elevation as its position too (a clamped trench). See DECISIONS-importer.md M2.</summary>
        public double ExactLimit = 4.5;
        /// <summary>Build every course end → start when the plan says the CS1 city drove on the left (#55).</summary>
        public bool ReverseLeftHand = true;
        /// <summary>
        /// Crossings without a shared node: CS2 joins two courses that cross with too little vertical room (the whole-city
        /// run). Where the planned CS2 clearance is below min(MinClearance, CS1's clearance there), the lower road's
        /// below-ground nodes go deeper, or the upper road's elevated nodes higher, but never past the node's own CS1
        /// height above terrain. 0 turns the pass off. See DECISIONS-importer.md #43.
        /// </summary>
        public double MinClearance = 8.0;
        /// <summary>
        /// CS1 tunnels shallower than CS2's tunnel rule are deepened (DECISIONS-importer.md #58; DECISIONS.md #124). CS2
        /// builds a tunnel composition only when every elevation of the course is at most -2L and the deepest at most -3L
        /// (L = the prefab's NetGeometryData.m_ElevationLimit; `NetCompositionHelpers.GetElevationFlags`). Margin below
        /// those limits; 0 or less turns the pass off.
        /// </summary>
        public double TunnelMargin = 0.5;
        /// <summary>m_ElevationLimit by prefab type (CS2 1.6.4f1: roads and tracks 4, PathwayPrefab 2); others use 4.
        /// Prefab keys can override it (ElevationLimits).</summary>
        public double DefaultElevationLimit = 4.0, PathwayElevationLimit = 2.0;
        public Dictionary<string, double> ElevationLimits = new Dictionary<string, double>();
        /// <summary>Steepest slope to a deepened portal before the descent is extended along the slope (CS2's road tool
        /// limit, m_MaxSlopeSteepness 0.2).</summary>
        public double MaxGrade = 0.2;
        /// <summary>
        /// Keep Node Controller shifts (segments[].offNode, web 0.9.0+; DECISIONS-nodecontroller.md) as off-node curve
        /// ends. Off by default (DECISIONS-importer.md #59): on a web 0.9.0 Skibidi crop the importer's node lookup missed
        /// the nodes CS2 made at shifted ends (33 nodes not created, 56 segments without a node). Off = the ends snap to
        /// their nodes, as before 0.9.0.
        /// </summary>
        public bool ApplyOffNode = false;
        /// <summary>
        /// A walls ground segment whose CS1 vertical profile leaves the straight line between its ends by more than this
        /// (m) is split there, with a node at the CS1 height (#62): CS2 lays a course between two ground-level ends on the
        /// terrain whatever its curve's y or middle elevation (CacaoCity 25425, 8.8 m low in a dip). 0 turns it off.
        /// </summary>
        public double ProfileSplit = 1.5;
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
        /// <summary>Plan segments split at their CS1 profile (#62).</summary>
        public int SplitSegments;

        /// <summary>The job segment of plan segment `id` that touches plan node `node` (a split segment has two); else the
        /// first one; null when the plan segment isn't in the job.</summary>
        public SegmentJob SegmentAt(long id, long node)
        {
            SegmentJob first = null;
            foreach (var s in Segments)
            {
                if (s.Id != id) continue;
                if (first == null) first = s;
                if (s.Start.Id == node || s.End.Id == node) return s;
            }
            return first;
        }

        /// <summary>Walls nodes whose CS1 height is beyond CS2's wall limits over the real terrain (#61): they stay at the limit.</summary>
        public readonly List<Dictionary<string, object>> WallsShort = new List<Dictionary<string, object>>();
        /// <summary>One row per CS1 tunnel segment deepened to CS2's tunnel rule (#58): id, CS1 and CS2 end elevations.</summary>
        public readonly List<Dictionary<string, object>> TunnelsDeepened = new List<Dictionary<string, object>>();
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

                var seg = new SegmentJob { Index = job.Segments.Count, Source = s, Start = NodeFor(job, a), End = NodeFor(job, b), ChordLength = chord, BuildReversed = opt.ReverseLeftHand && plan.LeftHandTraffic == true, HeightMode = ModeOf(plan, s), OffNode = opt.ApplyOffNode && s.OffNode };
                if (seg.OffNode && s.Bezier != null)
                {
                    seg.OffStartX = s.Bezier[0].X - a.X; seg.OffStartZ = s.Bezier[0].Z - a.Z;
                    seg.OffEndX = s.Bezier[3].X - b.X; seg.OffEndZ = s.Bezier[3].Z - b.Z;
                }
                seg.Start.Uses++;
                seg.End.Uses++;
                seg.Bezier = s.Bezier;
                job.Segments.Add(seg);
                job.UsedPrefabKeys.Add(s.Prefab);
            }

            SplitProfiles(job, null);
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

        static readonly string[] Modes = { "conform", "follow", "walls" };

        /// <summary>The segment's height mode: its prefab's heightMode, else conform on a road-conformed package, else null.</summary>
        static string ModeOf(Plan plan, PlanSegment s)
        {
            PlanPrefab pf;
            string m = plan.Prefabs.TryGetValue(s.Prefab ?? "", out pf) ? pf.HeightMode : null;
            if (m != null && Array.IndexOf(Modes, m) < 0) m = null;
            m = m ?? (plan.RoadConformed ? "conform" : null);
            // a tunnel's slope is never conformed (#58): CS2 builds the tunnel only with the ground at the portal 8-12 m
            // above the road, so the slope keeps its CS1 height in the natural terrain, as walls (a lowered cut)
            if (m == "conform" && s.Kind == "slope") m = "walls";
            return m;
        }

        static bool Outside(PlanNode n, ImportOptions o) { return Math.Abs(n.X + o.OffsetX) > o.MapHalfSize || Math.Abs(n.Z + o.OffsetZ) > o.MapHalfSize; }

        static void Skip(ImportJob job, PlanSegment s, string kind, string msg)
        {
            job.Skipped.Add(new Issue("warning", kind, msg) { Segment = s.Id });
        }

        /// <summary>
        /// #62: walls ground segments whose CS1 profile leaves the straight end-to-end line by more than ProfileSplit are
        /// split at the farthest point (16 samples in t), each half at least 8 m long, with a node there at the CS1 height
        /// (id = −segment id; heightAboveTerrain estimated against the ends' natural terrain, then taken from the real
        /// terrain in game). Both halves keep the plan segment's id; lane connections, traffic and verify find the half
        /// at a node (ImportJob.SegmentAt) or join the two.
        /// </summary>
        /// <summary>
        /// In game, on the package's terrain (#62): the same split where CS1's road height leaves the line CS2 would lay
        /// it on (the real terrain plus the line between the end elevations) by more than ProfileSplit, e.g. a CS1 highway
        /// straight across a 9 m dip between two ground-level ends (CacaoCity 25425). The split node takes CS1's height
        /// above the real terrain, within the wall limits. Re-batches when anything was split; returns the count.
        /// </summary>
        public static int SplitOverTerrain(ImportJob job, Func<double, double, double> terrainAt)
        {
            int before = job.SplitSegments;
            SplitProfiles(job, terrainAt);
            int n = job.SplitSegments - before;
            if (n > 0) { job.Batches.Clear(); MakeBatches(job); }
            return n;
        }

        static void SplitProfiles(ImportJob job, Func<double, double, double> terrainAt)
        {
            var o = job.Options;
            if (o.ProfileSplit <= 0) return;
            var result = new List<SegmentJob>();
            int splitHere = 0;
            foreach (var s in job.Segments)
            {
                result.Add(s);
                var b = s.Bezier;
                if (b == null || s.HeightMode != "walls" || s.Source.Kind != "ground" || s.OffNode || s.Half >= 0 || job.Nodes.ContainsKey(-s.Id)) continue;
                double bestT = 0, bestDev = 0;
                for (int i = 1; i < 16; i++)
                {
                    double t = i / 16.0;
                    double y = Cubic(b[0].Y, b[1].Y, b[2].Y, b[3].Y, t), dev;
                    if (terrainAt == null) dev = y - Lerp(b[0].Y, b[3].Y, t);
                    else
                    {
                        double x = Cubic(b[0].X, b[1].X, b[2].X, b[3].X, t) + o.OffsetX, z = Cubic(b[0].Z, b[1].Z, b[2].Z, b[3].Z, t) + o.OffsetZ;
                        dev = y - (terrainAt(x, z) + Lerp(s.Start.Elevation, s.End.Elevation, t));
                    }
                    if (Math.Abs(dev) > Math.Abs(bestDev)) { bestDev = dev; bestT = t; }
                }
                if (Math.Abs(bestDev) <= o.ProfileSplit) continue;
                var p = SplitBezier(b, bestT);
                var left = p[0];
                var right = p[1];
                var m = left[3];
                double la = Dist(left[0], m), lb = Dist(m, right[3]);
                if (la < 8 || lb < 8) continue;
                double natA = s.Start.Cs1Y - (double.IsNaN(s.Start.Cs1HeightAboveTerrain) ? 0 : s.Start.Cs1HeightAboveTerrain);
                double natB = s.End.Cs1Y - (double.IsNaN(s.End.Cs1HeightAboveTerrain) ? 0 : s.End.Cs1HeightAboveTerrain);
                var mid = new NodeJob { Id = -s.Id, X = m.X + o.OffsetX, Z = m.Z + o.OffsetZ, Cs1Y = m.Y, Cs1HeightAboveTerrain = m.Y - Lerp(natA, natB, bestT), Uses = 2, WallsGround = true };
                if (terrainAt != null)
                {
                    // in game: CS1's height above the real terrain at the split point, within the wall limits (#61)
                    double hat = m.Y - terrainAt(mid.X, mid.Z);
                    mid.Cs1HeightAboveTerrain = hat;
                    mid.Elevation = Math.Abs(hat) < o.GroundSnap ? 0 : Math.Round(Math.Max(o.FeatureMin, Math.Min(o.FeatureMax, hat)), 2);
                    mid.PositionHeight = mid.Elevation;
                }
                job.Nodes[mid.Id] = mid;
                var second = new SegmentJob
                {
                    Source = s.Source, Start = mid, End = s.End, ChordLength = lb, BuildReversed = s.BuildReversed, HeightMode = s.HeightMode,
                    OffNode = false, Bezier = right, Half = 1,
                };
                s.End = mid; s.Bezier = left; s.Half = 0; s.ChordLength = la;
                s.MiddleElevation = (s.Start.Elevation + mid.Elevation) / 2;
                second.MiddleElevation = (mid.Elevation + second.End.Elevation) / 2;
                result.Add(second);
                job.SplitSegments++;
                splitHere++;
            }
            if (splitHere == 0) return;
            job.Segments.Clear();
            for (int i = 0; i < result.Count; i++) { result[i].Index = i; job.Segments.Add(result[i]); }
        }

        /// <summary>De Casteljau split of a cubic bezier at t: [left, right].</summary>
        public static V3[][] SplitBezier(V3[] b, double t)
        {
            Func<V3, V3, V3> L = (p, q) => new V3(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t, p.Z + (q.Z - p.Z) * t);
            V3 p01 = L(b[0], b[1]), p12 = L(b[1], b[2]), p23 = L(b[2], b[3]);
            V3 p012 = L(p01, p12), p123 = L(p12, p23);
            V3 m = L(p012, p123);
            return new[] { new[] { b[0], p01, p012, m }, new[] { m, p123, p23, b[3] } };
        }

        static double Dist(V3 a, V3 b) { double dx = b.X - a.X, dz = b.Z - a.Z; return Math.Sqrt(dx * dx + dz * dz); }

        static NodeJob NodeFor(ImportJob job, PlanNode n)
        {
            NodeJob j;
            if (job.Nodes.TryGetValue(n.Id, out j)) return j;
            j = new NodeJob { Id = n.Id, X = n.X + job.Options.OffsetX, Z = n.Z + job.Options.OffsetZ, Cs1HeightAboveTerrain = n.HeightAboveTerrain, Cs1Y = n.Y };
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
            // the height modes of the ground segments at each node (null = no mode: the M2 rules)
            var modes = new Dictionary<long, HashSet<string>>();
            // nodes of CS1 tunnel segments: never conformed, the tunnel needs ground above it (#58)
            var tunnelNodes = new HashSet<long>();
            // nodes with an elevated, bridge or tunnel segment: their composition comes from that segment (#61)
            var structureNodes = new HashSet<long>();
            foreach (var s in job.Segments)
            {
                if (s.Source.Kind == "tunnel") { tunnelNodes.Add(s.Start.Id); tunnelNodes.Add(s.End.Id); }
                if (!s.IsGround) { structureNodes.Add(s.Start.Id); structureNodes.Add(s.End.Id); }
                if (s.IsGround)
                    foreach (var id in new[] { s.Start.Id, s.End.Id })
                    {
                        HashSet<string> m;
                        if (!modes.TryGetValue(id, out m)) modes[id] = m = new HashSet<string>();
                        m.Add(s.HeightMode ?? "");
                    }
                Add(ends, s.Start.Id, NormalizeElevation(s.Source.ElevationStart, job.Options.GroundSnap));
                Add(ends, s.End.Id, NormalizeElevation(s.Source.ElevationEnd, job.Options.GroundSnap));
            }
            foreach (var n in job.Nodes.Values)
            {
                List<double> l;
                if (!ends.TryGetValue(n.Id, out l)) continue;
                double hat = n.Cs1HeightAboveTerrain;
                HashSet<string> nm;
                modes.TryGetValue(n.Id, out nm);
                double feature;
                // the node rule (NOTES-road-heights.md): conform wins (the terrain there is the CS1 height), then walls (CS1
                // height above the natural terrain, clamped to walls/quays), then follow (on the terrain); no modes: M2 rules
                if (nm != null && nm.Contains("conform") && !tunnelNodes.Contains(n.Id)) { feature = 0; n.Conformed = true; }
                else if (nm != null && nm.Contains("walls"))
                {
                    // walls = CS1 height above the terrain (#57), within CS2's wall limits (-11.5..+7.5 m): CS1's elevation
                    // kind decides the composition family (#61, DECISIONS.md #129), so a CS1 ground road is never a tunnel or
                    // on pillars. Beyond a limit the package's terrain has to be reshaped beside the road (the walls burn) for
                    // the road to keep its CS1 height; until then it stays at the limit and is reported
                    feature = double.IsNaN(hat) || Math.Abs(hat) < job.Options.GroundSnap ? 0 : Math.Round(hat, 2);
                    if (feature > job.Options.FeatureMax + job.Options.GroundSnap || feature < job.Options.FeatureMin - job.Options.GroundSnap)
                        job.Warnings.Add(new Issue("info", "walls-beyond-limits", "node " + n.Id + " is " + feature.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m from the natural terrain in walls mode: walls at the limit unless the package's terrain is reshaped beside it") { Node = n.Id });
                    feature = Math.Max(job.Options.FeatureMin, Math.Min(job.Options.FeatureMax, feature));
                    n.WallsGround = !structureNodes.Contains(n.Id);
                }
                else if (nm != null && !nm.Contains("")) feature = 0; // follow only
                else
                {
                    bool isFeature = hat <= -job.Options.TerrainFeature || (job.Options.KeepEmbankments && hat >= job.Options.TerrainFeature);
                    feature = isFeature ? Math.Max(job.Options.FeatureMin, Math.Min(job.Options.FeatureMax, Math.Round(hat, 2))) : 0;
                }
                double best = 0, lo = double.MaxValue, hi = double.MinValue;
                foreach (var e in l)
                {
                    if (Math.Abs(e) > Math.Abs(best)) best = e;
                    if (e != 0) { lo = Math.Min(lo, e); hi = Math.Max(hi, e); }
                }
                if (Math.Abs(feature) > Math.Abs(best)) best = feature;
                // a conformed node sits on terrain burned to its CS1 height: elevation 0 even where an elevated segment ends
                // (a ramp's embankment meets the bridge); tunnel nodes are never conformed
                if (n.Conformed) best = 0;
                n.Elevation = best;
                if (hi - lo > 1)
                    job.Warnings.Add(new Issue("info", "elevation-mismatch", "segments at node " + n.Id + " disagree on its elevation (" + lo.ToString("0.0") + " .. " + hi.ToString("0.0") + " m); using " + best.ToString("0.0")) { Node = n.Id });
            }
            DeepenTunnels(job, null);
            if (job.Options.MinClearance > 0) Clearance.Keep(job);
            bool exact = job.Options.Heights == "exact";
            foreach (var n in job.Nodes.Values)
                n.PositionHeight = exact && !double.IsNaN(n.Cs1HeightAboveTerrain) && Math.Abs(n.Cs1HeightAboveTerrain - n.Elevation) <= job.Options.ExactLimit
                    ? Math.Round(n.Cs1HeightAboveTerrain, 2) : n.Elevation;
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
        /// Where CS2 ends up putting each node that has an offNode (Node Controller) segment end: CS2 places a node at the
        /// mean of its edges' curve ends (NOTES-lane-alignment.md §3), so with every plan segment built that is the mean of
        /// node + end offset over the segments at the node. Only nodes with a shifted end are listed; x/z include the test
        /// offset like NodeJob.X/Z.
        /// </summary>
        /// <summary>The shifted (offNode) curve ends at each node, x/z with the test offset: what the edges built there end at,
        /// whatever CS2 does with the node's own position.</summary>
        public static Dictionary<long, List<double[]>> OffNodeEnds(ImportJob job)
        {
            var r = new Dictionary<long, List<double[]>>();
            Action<NodeJob, double, double> add = (n, ox, oz) =>
            {
                if (Math.Abs(ox) <= 0.05 && Math.Abs(oz) <= 0.05) return;
                List<double[]> l;
                if (!r.TryGetValue(n.Id, out l)) r[n.Id] = l = new List<double[]>();
                l.Add(new[] { n.X + ox, n.Z + oz });
            };
            foreach (var s in job.Segments)
            {
                if (!s.OffNode) continue;
                add(s.Start, s.OffStartX, s.OffStartZ);
                add(s.End, s.OffEndX, s.OffEndZ);
            }
            return r;
        }

        public static Dictionary<long, double[]> OffNodeCentres(ImportJob job)
        {
            var sum = new Dictionary<long, double[]>(); // id -> {sx, sz, n, shifted}
            Action<NodeJob, double, double> add = (n, ox, oz) =>
            {
                double[] t;
                if (!sum.TryGetValue(n.Id, out t)) sum[n.Id] = t = new double[4];
                t[0] += n.X + ox; t[1] += n.Z + oz; t[2]++;
                if (Math.Abs(ox) > 0.05 || Math.Abs(oz) > 0.05) t[3] = 1;
            };
            foreach (var s in job.Segments)
            {
                add(s.Start, s.OffNode ? s.OffStartX : 0, s.OffNode ? s.OffStartZ : 0);
                add(s.End, s.OffNode ? s.OffEndX : 0, s.OffNode ? s.OffEndZ : 0);
            }
            var r = new Dictionary<long, double[]>();
            foreach (var kv in sum) if (kv.Value[3] > 0) r[kv.Key] = new[] { kv.Value[0] / kv.Value[2], kv.Value[1] / kv.Value[2] };
            return r;
        }

        /// <summary>
        /// The CS2 curve for a segment, given the final world positions of its two nodes. X/Z of the inner control points
        /// come from the plan's bezier (shifted with the end points). Y is the straight line between the two ends; elevated,
        /// bridge and tunnel segments also keep the CS1 curve's vertical bulge (arches). Without a bezier the curve is straight.
        /// </summary>
        public static V3[] Curve(SegmentJob s, V3 start, V3 end)
        {
            var b = s.Bezier;
            V3 p1, p2;
            if (b == null)
            {
                p1 = start + (end - start) * (1.0 / 3);
                p2 = start + (end - start) * (2.0 / 3);
                return new[] { start, p1, p2, end };
            }
            // offNode (Node Controller shift, DECISIONS-nodecontroller.md): the curve ends keep their plan offset from the
            // node centre (CS2 road edges may end off their node: no StrictNodes); otherwise they sit on the nodes
            if (s.OffNode)
            {
                start = new V3(start.X + s.OffStartX, start.Y, start.Z + s.OffStartZ);
                end = new V3(end.X + s.OffEndX, end.Y, end.Z + s.OffEndZ);
            }
            // x/z: move each inner point with its nearer end point (the plan's ends should equal the node positions)
            V3 da = new V3(start.X - b[0].X, 0, start.Z - b[0].Z), dd = new V3(end.X - b[3].X, 0, end.Z - b[3].Z);
            p1 = new V3(b[1].X + da.X, 0, b[1].Z + da.Z);
            p2 = new V3(b[2].X + dd.X, 0, b[2].Z + dd.Z);
            // CS1's vertical profile: kept on elevated/bridge/tunnel courses, and on ground courses at CS1 height (conform: the
            // terrain has the same profile; walls: CS1 height by definition)
            bool keepBulge = (s.Source.Kind != "ground" && (s.Start.Elevation != 0 || s.End.Elevation != 0 || s.Source.Kind == "bridge"))
                || (s.IsGround && (s.HeightMode == "conform" || s.HeightMode == "walls"));
            double bulge1 = keepBulge ? b[1].Y - Lerp(b[0].Y, b[3].Y, 1.0 / 3) : 0;
            double bulge2 = keepBulge ? b[2].Y - Lerp(b[0].Y, b[3].Y, 2.0 / 3) : 0;
            p1.Y = Lerp(start.Y, end.Y, 1.0 / 3) + bulge1;
            p2.Y = Lerp(start.Y, end.Y, 2.0 / 3) + bulge2;
            return new[] { start, p1, p2, end };
        }

        static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        /// <summary>
        /// Road-conformed packages (DECISIONS-importer.md #57): the heightmap was reshaped around the conform roads, so
        /// "terrain + CS1 height above the natural terrain" no longer is the CS1 height where a burn cut or raised the
        /// ground under an elevated, bridge, tunnel or walls node (Skibidi: an elevated highway end 6.6 m low over a road
        /// in a cut). Each such node (not conformed, elevation not 0) gets its elevation back from the real terrain:
        /// elevation = (CS1 natural terrain + planned elevation) − terrain at the node. Conformed and ground nodes, plans
        /// without roadConformed and "exact" heights are left alone. Returns the number of nodes moved by 0.25 m or more.
        /// </summary>
        /// <summary>
        /// Whether the CS2 terrain is the package's (Prepare map applied it): the median |terrain − CS1 natural terrain| over
        /// up to 400 nodes is under `tolerance` (3 m). The in-game height passes (#57, #61) take CS1 heights against the
        /// real terrain, which is only right on the package's terrain; at a flat test spot or on a map that kept its own
        /// terrain they would turn ground roads into elevated roads or tunnels (worker-5's flat-spot verify: 8 + 24).
        /// </summary>
        public static bool TerrainIsThePlans(ImportJob job, Func<double, double, double> terrainAt, double tolerance = 3.0)
        {
            var d = new List<double>();
            int step = Math.Max(1, job.Nodes.Count / 400), i = 0;
            foreach (var n in job.Nodes.Values)
            {
                if (i++ % step != 0 || double.IsNaN(n.Cs1Y) || double.IsNaN(n.Cs1HeightAboveTerrain)) continue;
                d.Add(Math.Abs(terrainAt(n.X, n.Z) - (n.Cs1Y - n.Cs1HeightAboveTerrain)));
            }
            if (d.Count == 0) return false;
            d.Sort();
            return d[d.Count / 2] < tolerance;
        }

        public static int KeepHeightsOnConformedTerrain(ImportJob job, Func<double, double, double> terrainAt)
        {
            if (job.Plan == null || !job.Plan.RoadConformed || job.Options.Heights == "exact") return 0;
            int moved = 0;
            foreach (var n in job.Nodes.Values)
            {
                if (n.WallsGround && !double.IsNaN(n.Cs1Y))
                {
                    // #61: CS1's height over the real (possibly walls-burned) terrain, within the wall limits
                    double want = n.Cs1Y - terrainAt(n.X, n.Z);
                    double e2 = Math.Abs(want) < job.Options.GroundSnap ? 0 : Math.Round(Math.Max(job.Options.FeatureMin, Math.Min(job.Options.FeatureMax, want)), 2);
                    if (Math.Abs(want - e2) > job.Options.GroundSnap)
                        job.WallsShort.Add(new Dictionary<string, object> { { "node", n.Id }, { "x", Math.Round(n.X) }, { "z", Math.Round(n.Z) }, { "cs1_above_terrain", Math.Round(want, 2) }, { "elevation", e2 }, { "short_by", Math.Round(want - e2, 2) } });
                    if (Math.Abs(e2 - n.Elevation) >= 0.25) moved++;
                    n.Elevation = e2;
                    n.PositionHeight = e2;
                    continue;
                }
                if (n.Conformed || n.Elevation == 0 || double.IsNaN(n.Cs1HeightAboveTerrain) || double.IsNaN(n.Cs1Y)) continue;
                double target = n.Cs1Y - n.Cs1HeightAboveTerrain + n.Elevation;
                double e = Math.Round(target - terrainAt(n.X, n.Z), 2);
                if (Math.Abs(e - n.Elevation) >= 0.25) moved++;
                n.Elevation = e;
                n.PositionHeight = e;
            }
            foreach (var s in job.Segments) s.MiddleElevation = (s.Start.Elevation + s.End.Elevation) / 2;
            return moved;
        }

        /// <summary>
        /// walls keeps CS1's height along the segment too (#57): CS2 lays a ground course (elevation 0 at both ends) on the
        /// terrain whatever the curve's y, so a CS1 highway crossing a 9 m dip on an embankment between two ground-level
        /// ends came out down in the dip (CacaoCity segment 25425). The course's middle elevation (NetCourse.m_Elevation)
        /// becomes CS1's height at the curve's midpoint above the real terrain there, when it differs from the ends' mean
        /// by the ground snap or more; CS2 then builds raised or lowered sides (or pillars or a tunnel, by its own rule).
        /// Returns the number of segments changed.
        /// </summary>
        public static int KeepWallsProfile(ImportJob job, Func<double, double, double> terrainAt)
        {
            int n = 0;
            foreach (var s in job.Segments)
            {
                var b = s.Bezier;
                if (!s.IsGround || s.HeightMode != "walls" || b == null) continue;
                double x = Cubic(b[0].X, b[1].X, b[2].X, b[3].X, 0.5) + (s.Start.X - b[0].X + s.End.X - b[3].X) / 2;
                double z = Cubic(b[0].Z, b[1].Z, b[2].Z, b[3].Z, 0.5) + (s.Start.Z - b[0].Z + s.End.Z - b[3].Z) / 2;
                double mid = Math.Round(Cubic(b[0].Y, b[1].Y, b[2].Y, b[3].Y, 0.5) - terrainAt(x, z), 2);
                double mean = (s.Start.Elevation + s.End.Elevation) / 2;
                // within the wall limits (#61): ends at -11.5 m with a -20 m middle would make CS2 build a tunnel
                mid = Math.Max(job.Options.FeatureMin, Math.Min(job.Options.FeatureMax, mid));
                if (Math.Abs(mid - mean) < job.Options.GroundSnap) continue;
                s.MiddleElevation = mid;
                n++;
            }
            return n;
        }

        static double Cubic(double a, double b, double c, double d, double t)
        {
            double u = 1 - t;
            return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
        }

        /// <summary>CS2's m_ElevationLimit for a segment's prefab (see ImportOptions.DefaultElevationLimit).</summary>
        public static double ElevationLimitOf(ImportJob job, SegmentJob s)
        {
            double l;
            if (s.PrefabKey != null && job.Options.ElevationLimits.TryGetValue(s.PrefabKey, out l) && l > 0) return l;
            PlanPrefab pf;
            if (job.Plan != null && s.PrefabKey != null && job.Plan.Prefabs.TryGetValue(s.PrefabKey, out pf) && pf.Type == "PathwayPrefab")
                return job.Options.PathwayElevationLimit;
            return job.Options.DefaultElevationLimit;
        }

        /// <summary>CS2's tunnel rule (NetCompositionHelpers.GetElevationFlags): every elevation at most -2L, the deepest at most -3L.</summary>
        public static bool IsCs2Tunnel(double a, double b, double limit)
        {
            double m = (a + b) / 2;
            return Math.Max(a, Math.Max(b, m)) <= -2 * limit && Math.Min(a, Math.Min(b, m)) <= -3 * limit;
        }

        /// <summary>
        /// CS1 tunnels shallower than CS2's tunnel rule come out as open cuts (DECISIONS.md #124). Each CS1 tunnel segment
        /// (kind "tunnel") that fails the rule gets its deeper end lowered to -(3L + margin), then, if still needed, its
        /// shallower end to -(2L + margin) (and -(3L + margin) as a last resort): portals stay where CS1 has them and as
        /// shallow as the rule allows. Repeated until stable (nodes are shared). Then each slope or ground segment leading
        /// down to a deepened node is checked: steeper than MaxGrade (and than in CS1) → its other end goes down too, along
        /// below-ground, non-junction, non-conformed nodes ("tunnel-descent-extended"); what stays steep is reported
        /// ("tunnel-portal-steep").
        /// Planner pass: elevations relative to CS1's natural terrain (terrainAt null). In game, after
        /// KeepHeightsOnConformedTerrain, with the real terrain: the same rule on the elevations CS2 will see.
        /// Returns the number of segments deepened in this call; rows go to job.TunnelsDeepened.
        /// </summary>
        public static int DeepenTunnels(ImportJob job, Func<double, double, double> terrainAt)
        {
            var o = job.Options;
            if (o.TunnelMargin <= 0) return 0;
            var touched = new Dictionary<long, NodeJob>();
            var deepenedSegs = new List<SegmentJob>();
            var before = new Dictionary<long, double[]>();
            // every tunnel segment that fails the rule before any change is reported, also when a neighbour's deepening
            // (a shared node) is what fixes it
            foreach (var s in job.Segments)
                if (s.Source.Kind == "tunnel" && !IsCs2Tunnel(s.Start.Elevation, s.End.Elevation, ElevationLimitOf(job, s)))
                { before[s.Id] = new[] { s.Start.Elevation, s.End.Elevation }; deepenedSegs.Add(s); }
            if (deepenedSegs.Count == 0) return 0;
            for (int pass = 0; pass < 6; pass++)
            {
                bool changed = false;
                foreach (var s in job.Segments)
                {
                    if (s.Source.Kind != "tunnel") continue;
                    double L = ElevationLimitOf(job, s);
                    if (IsCs2Tunnel(s.Start.Elevation, s.End.Elevation, L)) continue;
                    var deep = s.Start.Elevation <= s.End.Elevation ? s.Start : s.End;
                    var shallow = deep == s.Start ? s.End : s.Start;
                    changed |= Lower(deep, -(3 * L + o.TunnelMargin), touched);
                    if (!IsCs2Tunnel(s.Start.Elevation, s.End.Elevation, L)) changed |= Lower(shallow, -(2 * L + o.TunnelMargin), touched);
                    if (!IsCs2Tunnel(s.Start.Elevation, s.End.Elevation, L)) changed |= Lower(shallow, -(3 * L + o.TunnelMargin), touched);
                }
                if (!changed) break;
            }
            if (touched.Count == 0) return 0;
            foreach (var n in touched.Values) n.PositionHeight = n.Elevation;
            ExtendDescents(job, touched, terrainAt);
            foreach (var s in job.Segments) s.MiddleElevation = (s.Start.Elevation + s.End.Elevation) / 2;
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var s in deepenedSegs)
            {
                var b = before[s.Id];
                double cs1 = Math.Min(b[0], b[1]), cs2 = Math.Min(s.Start.Elevation, s.End.Elevation);
                job.TunnelsDeepened.Add(new Dictionary<string, object> {
                    { "segment", s.Id }, { "prefab", s.PrefabKey }, { "cs1_info", s.Source.Cs1Info },
                    { "pass", terrainAt == null ? "plan" : "terrain" },
                    { "elevation_before", new[] { Math.Round(b[0], 2), Math.Round(b[1], 2) } },
                    { "elevation_cs2", new[] { Math.Round(s.Start.Elevation, 2), Math.Round(s.End.Elevation, 2) } },
                    { "deepest_before", Math.Round(cs1, 2) }, { "deepest_cs2", Math.Round(cs2, 2) }, { "delta", Math.Round(cs2 - cs1, 2) } });
                job.Warnings.Add(new Issue("info", "tunnel-deepened", string.Format(ic,
                    "tunnel segment {0} ({1}) at {2:0.0} / {3:0.0} m is too shallow for a CS2 tunnel: built at {4:0.0} / {5:0.0} m",
                    s.Id, s.Source.Cs1Info, b[0], b[1], s.Start.Elevation, s.End.Elevation)) { Segment = s.Id });
            }
            return deepenedSegs.Count;
        }

        static bool Lower(NodeJob n, double to, Dictionary<long, NodeJob> touched)
        {
            if (n.Elevation <= to) return false;
            if (double.IsNaN(n.DeepenedFrom)) n.DeepenedFrom = n.Elevation;
            n.Elevation = Math.Round(to, 2);
            n.Conformed = false;
            touched[n.Id] = n;
            return true;
        }

        /// <summary>Absolute height estimate of a node: the real terrain + elevation in game, else CS1's natural terrain + elevation.</summary>
        static double AbsHeight(NodeJob n, Func<double, double, double> terrainAt)
        {
            double t = terrainAt != null ? terrainAt(n.X, n.Z) : n.Cs1Y - (double.IsNaN(n.Cs1HeightAboveTerrain) ? 0 : n.Cs1HeightAboveTerrain);
            return t + n.Elevation;
        }

        static void ExtendDescents(ImportJob job, Dictionary<long, NodeJob> deepened, Func<double, double, double> terrainAt)
        {
            var o = job.Options;
            var byNode = new Dictionary<long, List<SegmentJob>>();
            foreach (var s in job.Segments)
                foreach (var id in new[] { s.Start.Id, s.End.Id })
                {
                    List<SegmentJob> l;
                    if (!byNode.TryGetValue(id, out l)) byNode[id] = l = new List<SegmentJob>();
                    l.Add(s);
                }
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var start in new List<NodeJob>(deepened.Values))
            {
                List<SegmentJob> firsts;
                if (!byNode.TryGetValue(start.Id, out firsts)) continue;
                foreach (var first in firsts)
                {
                    if (first.Source.Kind == "tunnel") continue;
                    var low = start;
                    var s = first;
                    for (int step = 0; step < 20 && s != null; step++)
                    {
                        var far = s.Start == low ? s.End : s.Start;
                        double len = Length(s);
                        if (len <= 0) break;
                        double grade = (AbsHeight(far, terrainAt) - AbsHeight(low, terrainAt)) / len;
                        double cs1Grade = Math.Abs(far.Cs1Y - low.Cs1Y) / len;
                        double allowed = Math.Max(o.MaxGrade, cs1Grade);
                        if (grade <= allowed + 1e-6) break;
                        bool canMove = !far.Conformed && far.Elevation < -o.GroundSnap && byNode[far.Id].Count == 2 && !deepened.ContainsKey(far.Id);
                        if (!canMove)
                        {
                            job.Warnings.Add(new Issue("info", "tunnel-portal-steep", string.Format(ic,
                                "segment {0} rises {1:0}% to the deepened tunnel node {2} (CS1 {3:0}%): steeper than {4:0}%, nothing left to lower",
                                s.Id, grade * 100, low.Id, cs1Grade * 100, o.MaxGrade * 100)) { Segment = s.Id });
                            break;
                        }
                        double target = AbsHeight(low, terrainAt) + allowed * len;
                        double from = far.Elevation;
                        if (double.IsNaN(far.DeepenedFrom)) far.DeepenedFrom = far.Elevation;
                        far.Elevation = Math.Round(far.Elevation - (AbsHeight(far, terrainAt) - target), 2);
                        far.PositionHeight = far.Elevation;
                        job.Warnings.Add(new Issue("info", "tunnel-descent-extended", string.Format(ic,
                            "node {0} lowered {1:0.0} -> {2:0.0} m so segment {3} reaches the deepened tunnel at {4:0}%",
                            far.Id, from, far.Elevation, s.Id, allowed * 100)) { Node = far.Id });
                        SegmentJob next = null;
                        foreach (var t in byNode[far.Id]) if (t != s) next = t;
                        low = far;
                        s = next != null && next.Source.Kind != "tunnel" ? next : null;
                    }
                }
            }
        }

        static double Length(SegmentJob s)
        {
            double dx = s.End.X - s.Start.X, dz = s.End.Z - s.Start.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>The curve in build order: Curve() start → end, or reversed for a BuildReversed segment.</summary>
        public static V3[] BuildCurve(SegmentJob s, V3 start, V3 end)
        {
            var c = Curve(s, start, end);
            return s.BuildReversed ? new[] { c[3], c[2], c[1], c[0] } : c;
        }

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
