// The clearance pass (src/Core/Clearance.cs, DECISIONS-importer.md #43) on two straight roads crossing at (0, 0).
using System;
using System.Globalization;
using System.Linq;
using Cs2Importer.Core;

namespace CoreTests
{
    public static class ClearanceTests
    {
        /// <summary>Road A (nodes 1-2) along x, road B (nodes 3-4) along z; every node is on terrain at 100 m in CS1.</summary>
        static ImportJob Cross(double hatA, string kindA, double elevA, double hatB, string kindB, double elevB, ImportOptions o = null, bool shareNode = false, double humpA = 0)
        {
            Func<double, string> f = v => v.ToString(CultureInfo.InvariantCulture);
            string node(int id, double x, double z, double hat) => "{\"id\":" + id + ",\"x\":" + f(x) + ",\"z\":" + f(z) + ",\"y\":" + f(100 + hat) + ",\"heightAboveTerrain\":" + f(hat) + ",\"elevation\":0}";
            string seg(int id, int a, int b, string kind, double e) => "{\"id\":" + id + ",\"prefab\":\"v:R\",\"start\":" + a + ",\"end\":" + b + ",\"elevationStart\":" + f(e) + ",\"elevationEnd\":" + f(e) + ",\"kind\":\"" + kind + "\"}";
            // road A's CS1 curve, with a hump of humpA metres in the middle (control points 4/3 as high)
            double ya = 100 + hatA, k = humpA * 4 / 3;
            string segA = "{\"id\":10,\"prefab\":\"v:R\",\"start\":1,\"end\":2,\"elevationStart\":" + f(elevA) + ",\"elevationEnd\":" + f(elevA) + ",\"kind\":\"" + kindA + "\",\"bezier\":[[-60," + f(ya) + ",0],[-20," + f(ya + k) + ",0],[20," + f(ya + k) + ",0],[60," + f(ya) + ",0]]}";
            var json = "{\"format\":\"cs1conv-plan\",\"formatVersion\":1,\"prefabs\":{\"v:R\":{\"type\":\"RoadPrefab\",\"name\":\"Small Road\"}},\"nodes\":["
                + node(1, -60, 0, hatA) + "," + node(2, 60, 0, hatA) + "," + node(3, 0, -60, hatB) + "," + node(4, 0, 60, hatB) + "],\"segments\":["
                + segA + "," + seg(11, 3, shareNode ? 1 : 4, kindB, elevB) + "]}";
            return ImportPlanner.Build(PlanParser.Parse(json), o ?? new ImportOptions());
        }

        static double Cs2Clearance(ImportJob j)
        {
            var c = Clearance.Find(j).Single();
            return c.Cs2Clearance;
        }

        public static void TestLowersAClampedTrench()
        {
            // A in a 5 m cut, B 15 m deep in CS1: the -11.5 m trench clamp leaves 6.5 m, CS1 had 10 m
            var j = Cross(-5, "ground", 0, -15, "ground", 0);
            Assert.True(Cs2Clearance(j) >= 8 - 0.3, "clearance restored to 8 m, got " + Cs2Clearance(j));
            Assert.True(j.Nodes[3].Elevation >= -15 && j.Nodes[4].Elevation >= -15, "never deeper than CS1");
            Assert.True(Math.Min(j.Nodes[3].Elevation, j.Nodes[4].Elevation) < -11.5, "the lower road went below the clamp");
            Assert.Eq(-5.0, j.Nodes[1].Elevation, "the upper road is unchanged");
            Assert.True(j.Warnings.Any(w => w.Kind == "clearance-restored" && w.Segment == 11), "reported");
            Assert.True(!j.Warnings.Any(w => w.Kind == "shallow-crossing"), "nothing left short");
        }

        public static void TestRaisesAnElevatedRoad()
        {
            // B on 6 m pillars in the plan's segment ends but 9 m above terrain in CS1, A on the ground
            var j = Cross(0, "ground", 0, 9, "elevated", 6);
            Assert.True(Cs2Clearance(j) >= 8 - 0.3, "clearance restored, got " + Cs2Clearance(j));
            Assert.True(j.Nodes[3].Elevation <= 9 && j.Nodes[4].Elevation <= 9, "never higher than CS1");
            Assert.Eq(0.0, j.Nodes[1].Elevation, "the ground road stays on the ground");
        }

        public static void TestReportsWhatItCannotFix()
        {
            // A is a ground road with a 6 m hump over B in CS1; CS2 builds ground roads straight, and neither road has
            // CS1 height to move into
            var j = Cross(0, "ground", 0, 0, "ground", 0, null, false, 6);
            Assert.Eq(0.0, j.Nodes[3].Elevation, "nothing to move");
            Assert.True(j.Warnings.Any(w => w.Kind == "shallow-crossing"), "the shortfall is reported");
        }

        public static void TestLeavesCs1sOwnSmallClearance()
        {
            // CS1 had them 1.5 m apart (e.g. a metro tunnel under a road tunnel): nothing to restore, nothing to report
            var j = Cross(-9, "tunnel", -9, -10.5, "tunnel", -10.5);
            Assert.Eq(-10.5, j.Nodes[3].Elevation, "unchanged");
            Assert.True(!j.Warnings.Any(w => w.Kind == "shallow-crossing" || w.Kind == "clearance-restored"), "no warnings");
        }

        public static void TestSharedNodeIsNoCrossing()
        {
            var j = Cross(-5, "ground", 0, -15, "ground", 0, null, true);
            Assert.Eq(0, Clearance.Find(j).Count, "segments that share a node don't cross");
        }

        public static void TestCanBeTurnedOff()
        {
            var j = Cross(-5, "ground", 0, -15, "ground", 0, new ImportOptions { MinClearance = 0 });
            Assert.Eq(-11.5, j.Nodes[3].Elevation, "clamped trench as before");
            Assert.True(Math.Abs(Cs2Clearance(j) - 6.5) < 0.01, "6.5 m as before, got " + Cs2Clearance(j));
        }
    }
}
