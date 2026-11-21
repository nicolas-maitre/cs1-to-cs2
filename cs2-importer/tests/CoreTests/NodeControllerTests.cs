// Node Controller shifts (docs/DECISIONS.md #107, DECISIONS-nodecontroller.md): an offNode segment's curve ends keep
// their plan offset from the node centres; without offNode they sit on the nodes as before.
using Cs2Importer.Core;

namespace CoreTests
{
    public static class NodeControllerTests
    {
        // nodes 1 (0,0) and 2 (100,0); the bezier starts 1.775 m left of node 1 (+z) and ends on node 2
        static string PlanJson(string source, bool offNode)
        {
            return @"{ ""format"": ""cs1conv-plan"", ""formatVersion"": 1, ""source"": " + source + @",
  ""prefabs"": { ""v:R"": { ""type"": ""RoadPrefab"", ""name"": ""Highway Oneway - 2 lanes"" } },
  ""nodes"": [ { ""id"": 1, ""x"": 0, ""z"": 0, ""y"": 10, ""heightAboveTerrain"": 0 }, { ""id"": 2, ""x"": 100, ""z"": 0, ""y"": 10, ""heightAboveTerrain"": 0 } ],
  ""segments"": [ { ""id"": 7, ""prefab"": ""v:R"", ""start"": 1, ""end"": 2, ""bezier"": [[0,10,1.775],[30,10,1.2],[70,10,0.4],[100,10,0]], ""kind"": ""ground""" +
                (offNode ? @", ""offNode"": true, ""ncShift"": [1.775, 0]" : "") + @" } ] }";
        }

        public static void TestOffNodeParsed()
        {
            Assert.True(PlanParser.Parse(PlanJson(@"{ ""city"": ""C"" }", true)).Segments[0].OffNode, "offNode parsed");
            Assert.True(!PlanParser.Parse(PlanJson(@"{ ""city"": ""C"" }", false)).Segments[0].OffNode, "absent = false");
        }

        public static void TestOffNodeCurveKeepsTheShift()
        {
            var job = ImportPlanner.Build(PlanParser.Parse(PlanJson(@"{ ""city"": ""C"" }", true)), new ImportOptions { ApplyOffNode = true });
            var s = job.Segments[0];
            // the importer's final node positions (terrain height, and a node that moved 0.2 m when it was resolved)
            var c = ImportPlanner.Curve(s, new V3(0.2, 55, 0), new V3(100, 56, 0));
            Assert.Near(0.2, c[0].X, 1e-9, "start x: node + offset");
            Assert.Near(1.775, c[0].Z, 1e-9, "start z keeps the 1.775 m shift");
            Assert.Near(55, c[0].Y, 1e-9, "start y = the node's height");
            Assert.Near(100, c[3].X, 1e-9, "end on its node");
            Assert.Near(0, c[3].Z, 1e-9, "end on its node (no shift there)");
            Assert.Near(30.2, c[1].X, 1e-9, "inner point moves with its end");
            Assert.Near(1.2, c[1].Z, 1e-9, "inner point keeps its plan z");
        }

        public static void TestOffNodeIsOffByDefault()
        {
            // #59: until the in-game node lookup handles shifted ends, a plan's offNode is ignored unless asked for
            var job = ImportPlanner.Build(PlanParser.Parse(PlanJson(@"{ ""city"": ""C"" }", true)), new ImportOptions());
            Assert.True(!job.Segments[0].OffNode, "not applied by default");
            var c = ImportPlanner.Curve(job.Segments[0], new V3(0, 55, 0), new V3(100, 56, 0));
            Assert.Near(0, c[0].Z, 1e-9, "the start sits on its node");
        }

        public static void TestWithoutOffNodeTheEndsSnapToTheNodes()
        {
            var job = ImportPlanner.Build(PlanParser.Parse(PlanJson(@"{ ""city"": ""C"" }", false)), new ImportOptions());
            var c = ImportPlanner.Curve(job.Segments[0], new V3(0, 55, 0), new V3(100, 56, 0));
            Assert.Near(0, c[0].Z, 1e-9, "older plans: the start sits on the node");
            Assert.Near(1.2 - 1.775, c[1].Z, 1e-9, "inner point moved with the snapped end, as before");
        }

        public static void TestOffNodeShiftIgnoresTheTestOffset()
        {
            // OffsetX/Z moves the nodes, not the bezier: the shift must not pick up the offset
            var job = ImportPlanner.Build(PlanParser.Parse(PlanJson(@"{ ""city"": ""C"" }", true)), new ImportOptions { ApplyOffNode = true, OffsetX = 2000, OffsetZ = -4000 });
            var s = job.Segments[0];
            Assert.Near(2000, s.Start.X, 1e-9, "node moved by the offset");
            var c = ImportPlanner.Curve(s, new V3(2000, 55, -4000), new V3(2100, 56, -4000));
            Assert.Near(2000, c[0].X, 1e-9, "start x on the offset node");
            Assert.Near(-4000 + 1.775, c[0].Z, 1e-9, "start keeps only the 1.775 m shift");
            Assert.Near(-4000, c[3].Z, 1e-9, "end on its node");
        }

        public static void TestOffNodeCentresAreTheMeanOfTheEnds()
        {
            // CS2 puts a node at the mean of its edges' curve ends: node 1 has one end at +1.775 → centre z = 1.775 (one edge);
            // node 2 has no shifted end → not listed
            var opt = new ImportOptions { ApplyOffNode = true, OffsetX = 10 };
            var c = ImportPlanner.OffNodeCentres(ImportPlanner.Build(PlanParser.Parse(PlanJson(@"{ ""city"": ""C"" }", true)), opt));
            Assert.Eq(1, c.Count, "only the shifted node");
            Assert.Near(10, c[1][0], 1e-9, "x (with the test offset)");
            Assert.Near(1.775, c[1][1], 1e-9, "z = the only end");
            Assert.Eq(0, ImportPlanner.OffNodeCentres(ImportPlanner.Build(PlanParser.Parse(PlanJson(@"{ ""city"": ""C"" }", true)), new ImportOptions())).Count, "offNode off: none");
            var ends = ImportPlanner.OffNodeEnds(ImportPlanner.Build(PlanParser.Parse(PlanJson(@"{ ""city"": ""C"" }", true)), opt));
            Assert.Eq(1, ends.Count, "only node 1 has a shifted end");
            Assert.Near(10, ends[1][0][0], 1e-9, "end x (with the test offset)");
            Assert.Near(1.775, ends[1][0][1], 1e-9, "end z");
        }

        public static void TestOffNodeLeftHandBuildsReversedWithTheShift()
        {
            var job = ImportPlanner.Build(PlanParser.Parse(PlanJson(@"{ ""city"": ""C"", ""leftHandTraffic"": true }", true)), new ImportOptions { ApplyOffNode = true });
            var s = job.Segments[0];
            Assert.True(s.BuildReversed, "left-hand: built end → start");
            var c = ImportPlanner.BuildCurve(s, new V3(0, 55, 0), new V3(100, 56, 0));
            Assert.Near(100, c[0].X, 1e-9, "the course starts at the plan end node");
            Assert.Near(0, c[0].Z, 1e-9, "which has no shift");
            Assert.Near(0, c[3].X, 1e-9, "and ends at the plan start");
            Assert.Near(1.775, c[3].Z, 1e-9, "keeping the start's shift");
        }
    }
}
