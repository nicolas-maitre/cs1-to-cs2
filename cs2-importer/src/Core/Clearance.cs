// Vertical clearance at crossings without a shared node (DECISIONS-importer.md #43). CS2 joins two courses that cross
// with too little room between them; CS1 allowed less. The importer's own elevation rules can also take clearance away:
// trenches are clamped to -11.5 m so CS2 keeps them open cuts, which lifted 16-22 m deep CS1 ramps under other ramps.
//
// Heights are estimated as CS2 will build them with flat heights on the converted terrain: node height = CS1 terrain
// (y - heightAboveTerrain) + Elevation, a straight line between the ends plus the CS1 bulge where Curve() keeps it.
using System;
using System.Collections.Generic;

namespace Cs2Importer.Core
{
    public sealed class Crossing
    {
        public SegmentJob A, B;
        public double Ta, Tb;           // curve parameters at the crossing
        public double X, Z;
        public double Cs1Clearance;     // |CS1 height A - CS1 height B| at the crossing (plan bezier y)
        public double Cs2Clearance;     // the same with the planned CS2 heights
        public bool AIsUpper;           // by CS1 heights
        internal object Tag;            // the two sampled curves
    }

    public static class Clearance
    {
        const int Pieces = 16;
        const double Tolerance = 0.25;  // deficits below this are rounding

        /// <summary>Restores clearance where it's short, then reports the crossings that are still short.</summary>
        public static void Keep(ImportJob job)
        {
            var opt = job.Options;
            var crossings = Find(job);
            crossings.Sort((p, q) => (Target(q, opt) - q.Cs2Clearance).CompareTo(Target(p, opt) - p.Cs2Clearance));
            foreach (var c in crossings)
            {
                Measure(c);
                double need = Target(c, opt) - c.Cs2Clearance;
                if (need <= Tolerance) continue;
                var upper = c.AIsUpper ? c.A : c.B;
                var lower = c.AIsUpper ? c.B : c.A;
                double tu = c.AIsUpper ? c.Ta : c.Tb, tl = c.AIsUpper ? c.Tb : c.Ta;
                double before = c.Cs2Clearance;
                // first the lower road down (towards its own CS1 depth), then the upper one up (elevated nodes only)
                need -= Move(lower, tl, need, -1);
                if (need > Tolerance) need -= Move(upper, tu, need, +1);
                Measure(c);
                if (c.Cs2Clearance > before + 0.01)
                    job.Warnings.Add(new Issue("info", "clearance-restored", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "segments {0} and {1} cross at ({2:0}, {3:0}): CS2 clearance {4:0.0} → {5:0.0} m (CS1 {6:0.0} m)",
                        upper.Id, lower.Id, c.X, c.Z, before, c.Cs2Clearance, c.Cs1Clearance)) { Segment = lower.Id });
            }
            foreach (var c in crossings)
            {
                Measure(c);
                if (Target(c, opt) - c.Cs2Clearance > Tolerance)
                    job.Warnings.Add(new Issue("warning", "shallow-crossing", string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "segments {0} and {1} cross at ({2:0}, {3:0}) with {4:0.0} m clearance (CS1 {5:0.0} m); CS2 may join them",
                        c.A.Id, c.B.Id, c.X, c.Z, c.Cs2Clearance, c.Cs1Clearance)) { Segment = c.A.Id });
            }
        }

        /// <summary>Clearance to aim for: CS1's, up to MinClearance (CS1 roads that crossed with less had a reason).</summary>
        static double Target(Crossing c, ImportOptions o) { return Math.Min(o.MinClearance, c.Cs1Clearance); }

        /// <summary>
        /// Moves the segment's end nodes by up to `need` at parameter t (dir -1 = down, +1 = up), the nearer end first.
        /// A node only moves towards its CS1 height above terrain: down if it is below ground in CS1, up if it's elevated
        /// in CS2 and higher in CS1. Returns how far the crossing point moved.
        /// </summary>
        static double Move(SegmentJob s, double t, double need, int dir)
        {
            double moved = 0;
            var ends = t >= 0.5 ? new[] { s.End, s.Start } : new[] { s.Start, s.End };
            foreach (var n in ends)
            {
                double w = n == s.End ? t : 1 - t;
                if (w < 0.05 || need - moved <= Tolerance) continue;
                double room = Room(n, dir);
                if (room <= 0) continue;
                double d = Math.Min(room, (need - moved) / w);
                n.Elevation = Math.Round(n.Elevation + dir * d, 2);
                moved += d * w;
            }
            return moved;
        }

        static double Room(NodeJob n, int dir)
        {
            double hat = n.Cs1HeightAboveTerrain;
            if (double.IsNaN(hat)) return 0;
            if (dir < 0) return n.Elevation <= 0 && hat < n.Elevation ? n.Elevation - hat : 0;
            return n.Elevation > 0 && hat > n.Elevation ? hat - n.Elevation : 0;
        }

        // ------------------------------------------------------------------ geometry

        sealed class Poly
        {
            public SegmentJob S;
            public double[] X = new double[Pieces + 1], Z = new double[Pieces + 1], Y1 = new double[Pieces + 1], Bulge = new double[Pieces + 1];
            public double MinX = double.MaxValue, MaxX = double.MinValue, MinZ = double.MaxValue, MaxZ = double.MinValue;
        }

        public static List<Crossing> Find(ImportJob job)
        {
            var polys = new List<Poly>();
            foreach (var s in job.Segments) polys.Add(Build(s));
            const double cell = 128;
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < polys.Count; i++)
            {
                var p = polys[i];
                for (long cx = (long)Math.Floor(p.MinX / cell); cx <= (long)Math.Floor(p.MaxX / cell); cx++)
                    for (long cz = (long)Math.Floor(p.MinZ / cell); cz <= (long)Math.Floor(p.MaxZ / cell); cz++)
                    {
                        List<int> l;
                        long k = cx * 1000003 + cz;
                        if (!grid.TryGetValue(k, out l)) grid[k] = l = new List<int>();
                        l.Add(i);
                    }
            }
            var seen = new HashSet<long>();
            var res = new List<Crossing>();
            foreach (var l in grid.Values)
                for (int a = 0; a < l.Count; a++)
                    for (int b = a + 1; b < l.Count; b++)
                    {
                        int i = Math.Min(l[a], l[b]), j = Math.Max(l[a], l[b]);
                        if (!seen.Add((long)i * polys.Count + j)) continue;
                        var c = Cross(polys[i], polys[j]);
                        if (c != null) res.Add(c);
                    }
            return res;
        }

        static Poly Build(SegmentJob s)
        {
            var p = new Poly { S = s };
            var b = s.Source.Bezier;
            V3 b0, b1, b2, b3;
            if (b != null) { b0 = b[0]; b1 = b[1]; b2 = b[2]; b3 = b[3]; }
            else
            {
                b0 = new V3(s.Start.X, s.Start.Cs1Y, s.Start.Z); b3 = new V3(s.End.X, s.End.Cs1Y, s.End.Z);
                b1 = b0 + (b3 - b0) * (1.0 / 3); b2 = b0 + (b3 - b0) * (2.0 / 3);
            }
            for (int i = 0; i <= Pieces; i++)
            {
                double t = (double)i / Pieces, u = 1 - t;
                double k0 = u * u * u, k1 = 3 * u * u * t, k2 = 3 * u * t * t, k3 = t * t * t;
                // x/z: the plan bezier moved with the node positions (the importer's test offset), as Curve() does
                p.X[i] = k0 * b0.X + k1 * b1.X + k2 * b2.X + k3 * b3.X + (s.Start.X - b0.X) * (u) + (s.End.X - b3.X) * t;
                p.Z[i] = k0 * b0.Z + k1 * b1.Z + k2 * b2.Z + k3 * b3.Z + (s.Start.Z - b0.Z) * (u) + (s.End.Z - b3.Z) * t;
                p.Y1[i] = k0 * b0.Y + k1 * b1.Y + k2 * b2.Y + k3 * b3.Y;
                p.Bulge[i] = p.Y1[i] - (b0.Y + (b3.Y - b0.Y) * t);
                p.MinX = Math.Min(p.MinX, p.X[i]); p.MaxX = Math.Max(p.MaxX, p.X[i]);
                p.MinZ = Math.Min(p.MinZ, p.Z[i]); p.MaxZ = Math.Max(p.MaxZ, p.Z[i]);
            }
            return p;
        }

        static Crossing Cross(Poly A, Poly B)
        {
            if (A.MaxX < B.MinX || B.MaxX < A.MinX || A.MaxZ < B.MinZ || B.MaxZ < A.MinZ) return null;
            var sa = A.S; var sb = B.S;
            if (sa.Start == sb.Start || sa.Start == sb.End || sa.End == sb.Start || sa.End == sb.End) return null;
            for (int i = 0; i < Pieces; i++)
                for (int j = 0; j < Pieces; j++)
                {
                    double px = A.X[i], pz = A.Z[i], qx = A.X[i + 1], qz = A.Z[i + 1], rx = B.X[j], rz = B.Z[j], sx = B.X[j + 1], sz = B.Z[j + 1];
                    double d = (qx - px) * (sz - rz) - (qz - pz) * (sx - rx);
                    if (Math.Abs(d) < 1e-9) continue;
                    double u = ((rx - px) * (sz - rz) - (rz - pz) * (sx - rx)) / d, v = ((rx - px) * (qz - pz) - (rz - pz) * (qx - px)) / d;
                    if (u < 0 || u > 1 || v < 0 || v > 1) continue;
                    var c = new Crossing
                    {
                        A = sa, B = sb, Ta = (i + u) / Pieces, Tb = (j + v) / Pieces, X = px + (qx - px) * u, Z = pz + (qz - pz) * u,
                    };
                    double ya = A.Y1[i] + (A.Y1[i + 1] - A.Y1[i]) * u, yb = B.Y1[j] + (B.Y1[j + 1] - B.Y1[j]) * v;
                    c.Cs1Clearance = Math.Abs(ya - yb);
                    c.AIsUpper = ya >= yb;
                    c.Tag = new[] { A, B };
                    Measure(c);
                    return c; // one crossing per pair is enough to act on
                }
            return null;
        }

        /// <summary>Planned CS2 heights at the crossing, from the current node elevations.</summary>
        static void Measure(Crossing c)
        {
            var polys = (Poly[])c.Tag;
            double ha = Height(polys[0], c.Ta), hb = Height(polys[1], c.Tb);
            c.Cs2Clearance = c.AIsUpper ? ha - hb : hb - ha;
        }

        static double Height(Poly p, double t)
        {
            var s = p.S;
            double h0 = s.Start.Cs1Y - s.Start.Cs1HeightAboveTerrain + s.Start.Elevation;
            double h1 = s.End.Cs1Y - s.End.Cs1HeightAboveTerrain + s.End.Elevation;
            bool keepBulge = s.Source.Kind != "ground" && (s.Start.Elevation != 0 || s.End.Elevation != 0 || s.Source.Kind == "bridge");
            double f = t * Pieces;
            int i = Math.Min(Pieces - 1, (int)Math.Floor(f));
            double bulge = p.Bulge[i] + (p.Bulge[i + 1] - p.Bulge[i]) * (f - i);
            return h0 + (h1 - h0) * t + (keepBulge ? bulge : 0);
        }
    }
}
