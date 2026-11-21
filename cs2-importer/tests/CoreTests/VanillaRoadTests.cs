// Vanilla CS2 road targets (DECISIONS.md #100): the planner treats a RoadPrefab like a Road Builder road everywhere.
using System.Linq;
using Cs2Importer.Core;

namespace CoreTests
{
    public static class VanillaRoadTests
    {
        const string Json = @"{ ""format"": ""cs1conv-plan"", ""formatVersion"": 1, ""source"": { ""city"": ""C"", ""leftHandTraffic"": true },
  ""prefabs"": { ""v:Medium Road"": { ""type"": ""RoadPrefab"", ""name"": ""Medium Road"", ""source"": ""Medium Road"" },
                 ""rb:1"": { ""type"": ""RoadBuilderPrefab"", ""name"": ""r1-cs1conv"", ""source"": ""Workshop Road"" } },
  ""nodes"": [ { ""id"": 1, ""x"": 0, ""z"": 0, ""y"": 10, ""heightAboveTerrain"": 0 }, { ""id"": 2, ""x"": 100, ""z"": 0, ""y"": 10, ""heightAboveTerrain"": 0 },
               { ""id"": 3, ""x"": 200, ""z"": 0, ""y"": 10, ""heightAboveTerrain"": 0 } ],
  ""segments"": [ { ""id"": 1, ""prefab"": ""v:Medium Road"", ""start"": 1, ""end"": 2, ""kind"": ""ground"" },
                  { ""id"": 2, ""prefab"": ""rb:1"", ""start"": 2, ""end"": 3, ""kind"": ""ground"" } ] }";

        public static void TestVanillaAndRoadBuilderTargetsPlanAlike()
        {
            var plan = PlanParser.Parse(Json);
            Assert.Eq("RoadPrefab", plan.Prefabs["v:Medium Road"].Type, "vanilla type kept");
            var job = ImportPlanner.Build(plan, new ImportOptions());
            Assert.Eq(2, job.Segments.Count, "both built");
            Assert.True(job.UsedPrefabKeys.Contains("v:Medium Road") && job.UsedPrefabKeys.Contains("rb:1"), "both prefabs looked up");
            Assert.True(job.Segments.All(s => s.BuildReversed), "the left-hand reversal applies to vanilla roads too");
            Assert.Eq(1, job.Batches.Count, "one batch");
        }
    }
}
