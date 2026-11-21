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
