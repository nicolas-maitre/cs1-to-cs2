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
    }
}
