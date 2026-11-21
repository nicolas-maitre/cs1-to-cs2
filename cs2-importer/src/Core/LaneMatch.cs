// Game-free part of the Traffic mod lane connections (src/Game/LaneOps.cs, DECISIONS-importer.md #52, #54): which CS2 lane
// a plan lane reference means, which junction lanes an arrow set allows, and which lanes a U-turn rule concerns.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cs2Importer.Core
{
    public enum Turn { Straight, Left, Right, UTurnLeft, UTurnRight }

    /// <summary>A car lane of an edge at one of its nodes.</summary>
    public struct EdgeLaneAtNode
    {
        /// <summary>Lateral position (m) at the node, + = right of the plan segment's start → end.</summary>
        public double Offset;
        /// <summary>The lane's index in the edge composition (its PathNode lane index at that node).</summary>
        public int Index;
        /// <summary>The lane ends at the node (traffic flows into the junction).</summary>
        public bool Inbound;
    }

    public static class LaneMatch
    {
        /// <summary>
        /// The CS2 lane a plan lane reference means: the edge's car lanes sorted left → right in the plan frame, the
        /// ordinal-th one. Null with a problem key when the count differs, the ordinal is out of range, or the lane flows
        /// the wrong way for its role (a source must enter the junction, a target leave it).
        /// </summary>
        public static int? Pick(IList<EdgeLaneAtNode> lanes, int ordinal, int count, bool wantInbound, out string problem)
        {
            problem = null;
            var sorted = lanes.OrderBy(l => l.Offset).ToList();
            if (sorted.Count != count) { problem = "lane_count_differs"; return null; }
            if (ordinal < 0 || ordinal >= sorted.Count) { problem = "ordinal_out_of_range"; return null; }
            var hit = sorted[ordinal];
            if (hit.Inbound != wantInbound) { problem = wantInbound ? "source_lane_leaves_the_node" : "target_lane_enters_the_node"; return null; }
            return hit.Index;
        }

        /// <summary>
        /// Angle (degrees, 0..180) between two horizontal directions. The independent arm check (#60, DECISIONS.md #126):
        /// the CS2 edge's tangent at the node against the CS1 segment's bezier tangent there, so a junction arm can't
        /// be swapped by the node/edge mapping without being noticed. NaN when either direction is degenerate.
        /// </summary>
        public static double AngleDeg(double ax, double az, double bx, double bz)
        {
            double la = Math.Sqrt(ax * ax + az * az), lb = Math.Sqrt(bx * bx + bz * bz);
            if (la < 1e-6 || lb < 1e-6) return double.NaN;
            double c = (ax * bx + az * bz) / (la * lb);
            return Math.Acos(Math.Max(-1, Math.Min(1, c))) * 180 / Math.PI;
        }

        /// <summary>
        /// The turn check (DECISIONS.md #126): geometry of what was written against CS1's. `want` = CS1's target arm
        /// directions for one source lane (leaving the node, from the CS1 beziers); `got` = exit directions of the CS2
        /// junction lanes that now leave that source lane (their curves' end tangents). Every CS2 lane must leave towards
        /// one of CS1's target arms and every CS1 target arm must be reached, within `tol` degrees. Independent of how plan
        /// segments were mapped to CS2 edges and lanes. `why` names the first failure ("extra_arm", "missing_arm", "none").
        /// </summary>
        public static bool TurnCheck(IList<double[]> want, IList<double[]> got, double tol, out string why, bool requireAll = true)
        {
            why = null;
            if (got.Count == 0) { why = "none"; return want.Count == 0; }
            foreach (var g in got)
                if (!want.Any(x => AngleDeg(x[0], x[1], g[0], g[1]) <= tol)) { why = "extra_arm"; return false; }
            if (requireAll)
                foreach (var x in want)
                    if (!got.Any(g => AngleDeg(x[0], x[1], g[0], g[1]) <= tol)) { why = "missing_arm"; return false; }
            return true;
        }

        /// <summary>
        /// The direction (x/z) of an arm's CS1 curve at its point nearest to p. `b` = 4 control points {x, z}, b[0] at the
        /// node. CS2 cuts a road back from the junction, so its junction lanes join an arm some metres out: on a curved arm
        /// the direction there, not at the node, is what a lane's exit must match.
        /// </summary>
        public static double[] TangentNear(double[][] b, double px, double pz)
        {
            double bestT = 0, bestD = double.MaxValue;
            for (int i = 0; i <= 64; i++)
            {
                double t = i / 64.0, u = 1 - t;
                double x = u * u * u * b[0][0] + 3 * u * u * t * b[1][0] + 3 * u * t * t * b[2][0] + t * t * t * b[3][0];
                double z = u * u * u * b[0][1] + 3 * u * u * t * b[1][1] + 3 * u * t * t * b[2][1] + t * t * t * b[3][1];
                double d = (x - px) * (x - px) + (z - pz) * (z - pz);
                if (d < bestD) { bestD = d; bestT = t; }
            }
            double s = bestT, v = 1 - s;
            double dx = 3 * v * v * (b[1][0] - b[0][0]) + 6 * v * s * (b[2][0] - b[1][0]) + 3 * s * s * (b[3][0] - b[2][0]);
            double dz = 3 * v * v * (b[1][1] - b[0][1]) + 6 * v * s * (b[2][1] - b[1][1]) + 3 * s * s * (b[3][1] - b[2][1]);
            if (dx * dx + dz * dz < 1e-9) { dx = b[3][0] - b[0][0]; dz = b[3][1] - b[0][1]; }
            return new[] { dx, dz };
        }

        /// <summary>
        /// TurnCheck against CS1 arm curves: a CS2 lane (exit direction + end point) leads into a CS1 arm when its exit
        /// matches the arm's direction at the arm point nearest to the lane's end (TangentNear), within `tol`.
        /// </summary>
        public static bool TurnCheckCurves(IList<double[][]> want, IList<double[]> gotExit, IList<double[]> gotEnd, double tol, out string why, bool requireAll = true)
        {
            why = null;
            if (gotExit.Count == 0) { why = "none"; return want.Count == 0; }
            Func<double[][], int, bool> into = (w, i) => { var d = TangentNear(w, gotEnd[i][0], gotEnd[i][1]); return AngleDeg(d[0], d[1], gotExit[i][0], gotExit[i][1]) <= tol; };
            for (int i = 0; i < gotExit.Count; i++)
                if (!want.Any(w => into(w, i))) { why = "extra_arm"; return false; }
            if (requireAll)
                foreach (var w in want)
                    if (!Enumerable.Range(0, gotExit.Count).Any(i => into(w, i))) { why = "missing_arm"; return false; }
            return true;
        }

        /// <summary>
        /// CS1's (TM:PE's) turn from one arm into another, by geometry: the source arm's and the target arm's directions
        /// leaving the node (x/z). Within 30° of straight on = Straight, 30–150° = Left / Right, beyond = U-turn (TM:PE's
        /// arrow rule: |cross| ≥ 0.5 is a turn, else the dot product decides). Right = clockwise seen from above (+x east,
        /// +z north).
        /// </summary>
        public static Turn Cs1Turn(double[] srcOut, double[] tgtOut)
        {
            double hx = -srcOut[0], hz = -srcOut[1]; // heading into the node
            double lh = Math.Sqrt(hx * hx + hz * hz), lt = Math.Sqrt(tgtOut[0] * tgtOut[0] + tgtOut[1] * tgtOut[1]);
            if (lh < 1e-9 || lt < 1e-9) return Turn.Straight;
            double sin = (hz * tgtOut[0] - hx * tgtOut[1]) / (lh * lt), cos = (hx * tgtOut[0] + hz * tgtOut[1]) / (lh * lt);
            if (Math.Abs(sin) >= 0.5) return sin > 0 ? Turn.Right : Turn.Left;
            return cos > 0 ? Turn.Straight : (sin >= 0 ? Turn.UTurnRight : Turn.UTurnLeft);
        }

        /// <summary>
        /// The lane picked by position instead of ordinal (independent lane check, #60): among the edge's lanes flowing
        /// the wanted way, the one whose offset as a fraction of the CS2 half-width is nearest to the CS1 lane's offset as a
        /// fraction of the CS1 half-width (sign: + = right of the plan segment). Null when nothing fits.
        /// </summary>
        public static int? PickByPosition(IList<EdgeLaneAtNode> lanes, double cs1Fraction, double cs2HalfWidth, bool wantInbound)
        {
            if (double.IsNaN(cs1Fraction) || !(cs2HalfWidth > 0)) return null;
            int? best = null;
            double bestD = double.MaxValue;
            foreach (var l in lanes)
            {
                if (l.Inbound != wantInbound) continue;
                double d = Math.Abs(l.Offset / cs2HalfWidth - cs1Fraction);
                if (d < bestD) { bestD = d; best = l.Index; }
            }
            return best;
        }

        /// <summary>
        /// The same lane counted from the other side: a CS1 city whose traffic handedness differs from the CS2 city's
        /// (left-hand CacaoCity into a right-hand CS2 city) has every lane where CS2 has it but flowing the other way, so
        /// a curb lane is matched with the CS2 curb lane on the other side of the road.
        /// </summary>
        public static int Mirror(int ordinal, int count) { return count - 1 - ordinal; }

        /// <summary>Mirrored matching for the whole plan only when it explains clearly more references than the plain one.</summary>
        public static bool ChooseMirrored(int okPlain, int okMirrored) { return okMirrored > okPlain && okMirrored >= 2 * Math.Max(1, okPlain); }

        /// <summary>TM:PE arrows (left / straight / right) allow a junction lane by its turn. U-turns count as the side
        /// they turn to, as in TM:PE (a U-turn needs the left arrow in right-hand traffic).</summary>
        public static bool Allows(ICollection<string> arrows, Turn turn)
        {
            switch (turn)
            {
                case Turn.Left: case Turn.UTurnLeft: return arrows.Contains("left");
                case Turn.Right: case Turn.UTurnRight: return arrows.Contains("right");
                default: return arrows.Contains("straight");
            }
        }

        /// <summary>The innermost lane (closest to the road's centre line) among the inbound or outbound ones; U-turns
        /// go from the innermost inbound lane to the innermost outbound lane of the same road.</summary>
        public static int? Innermost(IList<EdgeLaneAtNode> lanes, bool inbound)
        {
            var l = lanes.Where(x => x.Inbound == inbound).OrderBy(x => Math.Abs(x.Offset)).ToList();
            return l.Count == 0 ? (int?)null : l[0].Index;
        }

        /// <summary>
        /// How a source lane's lanes after the write compare with what it should have: "match", "missing" (some targets
        /// absent, nothing extra), "extra" (all targets there plus others), "none" (no lane at all) or "different".
        /// </summary>
        public static string Compare(ICollection<string> want, ICollection<string> got)
        {
            var w = new HashSet<string>(want);
            var g = new HashSet<string>(got);
            if (w.SetEquals(g)) return "match";
            if (g.Count == 0) return "none";
            if (g.IsSubsetOf(w)) return "missing";
            if (w.IsSubsetOf(g)) return "extra";
            return "different";
        }
    }
}
