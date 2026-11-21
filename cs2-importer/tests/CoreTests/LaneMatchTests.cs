// LaneMatch: plan lane references → CS2 lanes, arrows, U-turn lanes.
using System.Collections.Generic;
using Cs2Importer.Core;

namespace CoreTests
{
    public static class LaneMatchTests
    {
        // a 2+2 road at its end node, seen in the plan's direction: two lanes leaving on the left, two arriving on the right
        static List<EdgeLaneAtNode> TwoPlusTwo()
        {
            return new List<EdgeLaneAtNode>
            {
                new EdgeLaneAtNode { Offset = 5.5, Index = 3, Inbound = true },
                new EdgeLaneAtNode { Offset = -5.5, Index = 0, Inbound = false },
                new EdgeLaneAtNode { Offset = 2, Index = 2, Inbound = true },
                new EdgeLaneAtNode { Offset = -2, Index = 1, Inbound = false },
            };
        }

        public static void TestPickByOrdinalLeftToRight()
        {
            string p;
            Assert.Eq(0, LaneMatch.Pick(TwoPlusTwo(), 0, 4, false, out p).Value, "ordinal 0 = leftmost");
            Assert.Eq(2, LaneMatch.Pick(TwoPlusTwo(), 2, 4, true, out p).Value, "ordinal 2 = first right-hand lane");
            Assert.Eq(3, LaneMatch.Pick(TwoPlusTwo(), 3, 4, true, out p).Value, "ordinal 3 = rightmost");
        }

        public static void TestPickRejectsWrongCountOrDirection()
        {
            string p;
            Assert.True(LaneMatch.Pick(TwoPlusTwo(), 1, 3, false, out p) == null && p == "lane_count_differs", "count");
            Assert.True(LaneMatch.Pick(TwoPlusTwo(), 5, 4, false, out p) == null && p == "ordinal_out_of_range", "range");
            Assert.True(LaneMatch.Pick(TwoPlusTwo(), 0, 4, true, out p) == null && p == "source_lane_leaves_the_node", "a source must enter");
            Assert.True(LaneMatch.Pick(TwoPlusTwo(), 3, 4, false, out p) == null && p == "target_lane_enters_the_node", "a target must leave");
        }

        public static void TestMirrorForOtherHandedness()
        {
            string p;
            // a left-hand-traffic plan's ordinal 3 (its rightmost lane, an outbound one there) is CS2's leftmost here
            Assert.Eq(0, LaneMatch.Mirror(3, 4), "mirror");
            Assert.True(LaneMatch.Pick(TwoPlusTwo(), 3, 4, false, out p) == null, "plain: the rightmost lane enters the node here");
            Assert.Eq(0, LaneMatch.Pick(TwoPlusTwo(), LaneMatch.Mirror(3, 4), 4, false, out p).Value, "mirrored: the leftmost lane leaves");
            Assert.True(LaneMatch.ChooseMirrored(3, 370) && !LaneMatch.ChooseMirrored(120, 130) && !LaneMatch.ChooseMirrored(300, 0), "only when clearly better");
        }

        public static void TestArrows()
        {
            var lr = new[] { "left", "straight" };
            Assert.True(LaneMatch.Allows(lr, Turn.Left) && LaneMatch.Allows(lr, Turn.Straight) && !LaneMatch.Allows(lr, Turn.Right), "left+straight");
            Assert.True(LaneMatch.Allows(lr, Turn.UTurnLeft) && !LaneMatch.Allows(new[] { "straight" }, Turn.UTurnLeft), "a U-turn needs its side's arrow");
        }

        public static void TestInnermost()
        {
            Assert.Eq(2, LaneMatch.Innermost(TwoPlusTwo(), true).Value, "innermost inbound");
            Assert.Eq(1, LaneMatch.Innermost(TwoPlusTwo(), false).Value, "innermost outbound");
            Assert.True(LaneMatch.Innermost(new List<EdgeLaneAtNode>(), true) == null, "none");
        }

        public static void TestCompare()
        {
            Assert.Eq("match", LaneMatch.Compare(new[] { "a", "b" }, new[] { "b", "a" }), "match");
            Assert.Eq("missing", LaneMatch.Compare(new[] { "a", "b" }, new[] { "a" }), "missing");
            Assert.Eq("extra", LaneMatch.Compare(new[] { "a" }, new[] { "a", "b" }), "extra");
            Assert.Eq("none", LaneMatch.Compare(new[] { "a" }, new string[0]), "none");
            Assert.Eq("different", LaneMatch.Compare(new[] { "a" }, new[] { "b" }), "different");
        }
    
        /// <summary>#60: the independent arm check compares directions, not our mapping.</summary>
        public static void TestArmAngle()
        {
            Assert.Near(0, LaneMatch.AngleDeg(1, 0, 5, 0), 1e-9, "same direction");
            Assert.Near(90, LaneMatch.AngleDeg(1, 0, 0, 3), 1e-9, "a neighbouring arm of a 4-way junction");
            Assert.Near(60, LaneMatch.AngleDeg(1, 0, 0.5, System.Math.Sqrt(3) / 2), 1e-9, "a neighbouring arm of a 6-way junction");
            Assert.True(double.IsNaN(LaneMatch.AngleDeg(0, 0, 1, 0)), "degenerate");
        }

        /// <summary>#60: the lane nearest the CS1 lane's position across the road, among lanes flowing the wanted way.</summary>
        public static void TestPickByPosition()
        {
            var lanes = new[] {
                new EdgeLaneAtNode { Offset = -5.25, Index = 0, Inbound = false }, new EdgeLaneAtNode { Offset = -1.75, Index = 1, Inbound = false },
                new EdgeLaneAtNode { Offset = 1.75, Index = 2, Inbound = true }, new EdgeLaneAtNode { Offset = 5.25, Index = 3, Inbound = true } };
            // CS1: 16 m half-width road, curb lane at +11.5 m; CS2 half-width 8 m
            Assert.Eq(3, LaneMatch.PickByPosition(lanes, 11.5 / 16, 8, true).Value, "curb lane");
            Assert.Eq(2, LaneMatch.PickByPosition(lanes, 3.5 / 16, 8, true).Value, "inner lane");
            Assert.Eq(1, LaneMatch.PickByPosition(lanes, -3.5 / 16, 8, false).Value, "outbound inner lane");
            Assert.True(LaneMatch.PickByPosition(lanes, double.NaN, 8, true) == null, "no CS1 offset");
        }

        /// <summary>#126: the written junction lanes must leave towards CS1's target arms, and reach all of them.</summary>
        public static void TestTurnCheck()
        {
            // a 6-arm junction: arms every 60°; CS1 says this lane goes straight (east) and right (south-east)
            var east = new[] { 1.0, 0.0 };
            var southEast = new[] { 0.5, -System.Math.Sqrt(3) / 2 };
            var northEast = new[] { 0.5, System.Math.Sqrt(3) / 2 };
            string why;
            Assert.True(LaneMatch.TurnCheck(new[] { east, southEast }, new[] { new[] { 0.98, 0.1 }, new[] { 0.45, -0.85 } }, 30, out why), "both arms, slightly bent lanes");
            Assert.True(!LaneMatch.TurnCheck(new[] { east, southEast }, new[] { east, northEast }, 30, out why) && why == "extra_arm", "the neighbouring arm on the other side: wrong");
            Assert.True(!LaneMatch.TurnCheck(new[] { east, southEast }, new[] { east }, 30, out why) && why == "missing_arm", "one target arm not reached");
            Assert.True(!LaneMatch.TurnCheck(new[] { east }, new double[0][], 30, out why) && why == "none", "nothing written");
            Assert.True(LaneMatch.TurnCheck(new[] { east, southEast }, new[] { east }, 30, out why, false), "arrows: reaching only some allowed arms is fine");
        }

        /// <summary>#126, CacaoCity node 17774: an arm that leaves the node at −66° and bends north; CS2's lanes join it a
        /// few metres out, at −34°. Compared at the node it looks like another arm; compared where the lane ends it matches.</summary>
        public static void TestTurnCheckCurvedArm()
        {
            // arm 28183 relative to the node: (0,0) → (−16.15, 7.04) → (−20.47, 40.35) → (−14.42, 40.35)
            var arm = new[] { new[] { 0.0, 0.0 }, new[] { -16.15, 7.04 }, new[] { -20.47, 40.35 }, new[] { -14.42, 40.35 } };
            var p = LaneMatch.TangentNear(arm, arm[0][0], arm[0][1]);
            Assert.Near(-66, System.Math.Atan2(p[0], p[1]) * 180 / System.Math.PI, 1, "at the node: −66°");
            // a CS2 lane ending on the arm ~10 m out, leaving along it
            var q = LaneMatch.TangentNear(arm, -9.5, 7.5);
            var exit = new[] { q[0], q[1] };
            string why;
            Assert.True(LaneMatch.TurnCheckCurves(new[] { arm }, new[] { exit }, new[] { new[] { -9.5, 7.5 } }, 30, out why), "matches the arm where the lane ends");
            var east = new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 }, new[] { 20.0, 0.0 }, new[] { 30.0, 0.0 } };
            Assert.True(!LaneMatch.TurnCheckCurves(new[] { east }, new[] { exit }, new[] { new[] { -9.5, 7.5 } }, 30, out why) && why == "extra_arm", "but not another arm");
        }

        /// <summary>#126: TM:PE's turn by geometry (node 3163: arms S, SW, W, NW, N, E).</summary>
        public static void TestCs1Turn()
        {
            double[] dir(double compassDeg) { double r = compassDeg * System.Math.PI / 180; return new[] { System.Math.Sin(r), System.Math.Cos(r) }; }
            var east = dir(91); // a westbound lane comes in from the east arm
            Assert.Eq(Turn.Left, LaneMatch.Cs1Turn(east, dir(-179)), "westbound into south = left");
            Assert.Eq(Turn.Straight, LaneMatch.Cs1Turn(east, dir(-85)), "into the west arm = straight");
            Assert.Eq(Turn.Right, LaneMatch.Cs1Turn(east, dir(-45)), "into the north-west ramp = right (45°)");
            Assert.Eq(Turn.Right, LaneMatch.Cs1Turn(east, dir(1)), "into the north arm = right");
            Assert.True(LaneMatch.Cs1Turn(east, dir(95)) == Turn.UTurnLeft || LaneMatch.Cs1Turn(east, dir(95)) == Turn.UTurnRight, "back into its own arm = U-turn");
        }
}
}
