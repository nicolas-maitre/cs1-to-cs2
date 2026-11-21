// Left-hand plans (DECISIONS-importer.md #55): courses are built end → start, the plan frame stays.
using Cs2Importer.Core;

namespace CoreTests
{
    public static class LeftHandTests
    {
        static string PlanJson(string source)
        {
            return @"{ ""format"": ""cs1conv-plan"", ""formatVersion"": 1, ""source"": " + source + @",
  ""prefabs"": { ""v:R"": { ""type"": ""RoadPrefab"", ""name"": ""Small Road"" } },
  ""nodes"": [ { ""id"": 1, ""x"": 0, ""z"": 0, ""y"": 10, ""heightAboveTerrain"": 0 }, { ""id"": 2, ""x"": 100, ""z"": 0, ""y"": 10, ""heightAboveTerrain"": 0 } ],
  ""segments"": [ { ""id"": 7, ""prefab"": ""v:R"", ""start"": 1, ""end"": 2, ""bezier"": [[0,10,0],[30,10,5],[70,10,5],[100,10,0]], ""kind"": ""ground"" } ] }";
        }

        public static void TestLeftHandPlanBuildsReversed()
        {
            var plan = PlanParser.Parse(PlanJson(@"{ ""city"": ""C"", ""leftHandTraffic"": true }"));
            Assert.True(plan.LeftHandTraffic == true, "parsed");
            var job = ImportPlanner.Build(plan, new ImportOptions());
            var s = job.Segments[0];
            Assert.True(s.BuildReversed, "built end → start");
            Assert.Eq(1L, s.Start.Id, "the plan frame keeps its start");
            var c = ImportPlanner.BuildCurve(s, new V3(0, 10, 0), new V3(100, 10, 0));
            Assert.Near(100, c[0].X, 1e-9, "the course starts at the plan end");
            Assert.Near(0, c[3].X, 1e-9, "and ends at the plan start");
            Assert.Near(70, c[1].X, 1e-9, "inner points reversed too");
        }

        public static void TestRightHandAndOlderPlansUnchanged()
        {
            foreach (var src in new[] { @"{ ""city"": ""C"", ""leftHandTraffic"": false }", @"{ ""city"": ""C"" }" })
            {
                var job = ImportPlanner.Build(PlanParser.Parse(PlanJson(src)), new ImportOptions());
                Assert.True(!job.Segments[0].BuildReversed, "not reversed: " + src);
                var c = ImportPlanner.BuildCurve(job.Segments[0], new V3(0, 10, 0), new V3(100, 10, 0));
                Assert.Near(0, c[0].X, 1e-9, "starts at the plan start");
            }
        }

        public static void TestCanBeTurnedOff()
        {
            var job = ImportPlanner.Build(PlanParser.Parse(PlanJson(@"{ ""leftHandTraffic"": true }")), new ImportOptions { ReverseLeftHand = false });
            Assert.True(!job.Segments[0].BuildReversed, "off");
        }
    }
}
