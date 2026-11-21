using System;
using System.IO;
using System.Linq;
using Cs2Importer.Core;

namespace CoreTests
{
    public static class PlanM2Tests
    {
        const string Json = @"{""format"":""cs1conv-plan"",""formatVersion"":1,
          ""zoning"":{""theme"":""NA"",""cellSize"":8,""prefabs"":[""NA Residential Low"",""Office High""],""cells"":[[1.5,-2.5,0],[10,20,1],[3,4]]},
          ""traffic"":{""nodes"":[{""id"":1,""trafficLights"":true},{""id"":2,""trafficLights"":false,""allWayStop"":true},{""id"":3}],
            ""segmentEnds"":[{""segment"":7,""node"":1,""priority"":""stop"",""forbid"":[""left"",""right""],""crosswalk"":false,""uturn"":true},{""segment"":8,""node"":2}],
            ""laneConnections"":[{},{}],""laneArrows"":[{}]}}";

        public static void TestParseM2()
        {
            var m = PlanM2.Parse(Json);
            Assert.Eq(2, m.Zoning.Prefabs.Count, "zone prefabs");
            Assert.Eq(2, m.Zoning.Cells.Count, "cells (a short row is skipped)");
            Assert.Eq(1, m.Zoning.Cells[1].Prefab, "cell prefab index");
            Assert.Near(-2.5, m.Zoning.Cells[0].Z, 1e-6, "cell z");
            Assert.Eq(3, m.Traffic.Nodes.Count, "traffic nodes");
            Assert.Eq(true, m.Traffic.Nodes[0].TrafficLights, "lights");
            Assert.True(m.Traffic.Nodes[2].TrafficLights == null, "no light state");
            Assert.True(m.Traffic.Nodes[1].AllWayStop, "all-way stop");
            var e = m.Traffic.SegmentEnds[0];
            Assert.Eq("stop", e.Priority, "priority");
            Assert.Eq("left,right", string.Join(",", e.Forbid), "forbid");
            Assert.Eq(false, e.Crosswalk, "crosswalk");
            Assert.True(m.Traffic.SegmentEnds[1].Crosswalk == null && m.Traffic.SegmentEnds[1].Priority == null, "empty end");
            Assert.Eq(2, m.Traffic.LaneConnections, "lane connections counted");
            Assert.Eq(1, m.Traffic.LaneArrows, "lane arrows counted");
        }

        public static void TestPlanWithoutM2()
        {
            var m = PlanM2.Parse(@"{""format"":""cs1conv-plan"",""formatVersion"":1}");
            Assert.True(m.Zoning == null && m.Traffic == null, "no sections");
        }

        public static void TestRealM2Sample()
        {
            var dir = Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "cs1-to-cs2", "samples", "skibidi-plan-m2", "cs1conv-plan.json");
            if (!File.Exists(dir)) return; // optional: only on a machine with the sample
            var m = PlanM2.Parse(File.ReadAllText(dir));
            Assert.True(m.Zoning.Cells.Count > 1000 && m.Zoning.Cells.All(c => c.Prefab >= 0 && c.Prefab < m.Zoning.Prefabs.Count), "zoning cells index valid prefabs");
            Assert.True(m.Traffic.Nodes.Count > 0 && m.Traffic.SegmentEnds.Count > 0, "traffic present");
        }
    }
}
