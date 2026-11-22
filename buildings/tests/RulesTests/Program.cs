// Tests of the placer's game-free rules (PlacementRules). Minimal runner, no NuGet: every public static void Test*.
using System;
using System.Linq;
using System.Reflection;
using Cs2Buildings;

namespace RulesTests
{
    public static class Program
    {
        public static int Main()
        {
            int failed = 0, passed = 0;
            foreach (var m in typeof(Program).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name.StartsWith("Test", StringComparison.Ordinal)).OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                try { m.Invoke(null, null); passed++; Console.WriteLine("ok   " + m.Name); }
                catch (TargetInvocationException e) { failed++; Console.WriteLine("FAIL " + m.Name + ": " + e.InnerException.Message); }
            }
            Console.WriteLine(passed + " passed, " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }

        static void Eq<T>(T want, T got, string what) { if (!Equals(want, got)) throw new Exception(what + ": want " + want + ", got " + got); }

        // DECISIONS.md #106: no fallback anywhere means "skip" (never moved to free land); an explicit choice still wins.
        public static void TestFallbackDefaultsToSkip()
        {
            Eq("skip", PlacementRules.DefaultFallback, "default");
            Eq("skip", PlacementRules.Fallback(false, null, null), "no item, no run option");
            Eq("skip", PlacementRules.Fallback(false, null, PlacementRules.DefaultFallback), "plan without fallback");
            Eq("nearby", PlacementRules.Fallback(false, "nearby", "skip"), "item opts in");
            Eq("anywhere", PlacementRules.Fallback(false, null, "anywhere"), "run opts in");
        }

        // Stations never move, whatever the item or run says (DECISIONS-buildings #36).
        public static void TestStationsAlwaysSkip()
        {
            Eq("skip", PlacementRules.Fallback(true, "nearby", "anywhere"), "station");
        }

        // Without free land only a smaller (or equal) alternative; with an opt-in fallback any alternative.
        public static void TestOnlySmallerAlternativesWithoutFallback()
        {
            Eq(true, PlacementRules.TryAlternative("skip", 84, 36), "smaller");
            Eq(true, PlacementRules.TryAlternative("skip", 84, 84), "same size");
            Eq(false, PlacementRules.TryAlternative("skip", 36, 84), "larger");
            Eq(true, PlacementRules.TryAlternative("nearby", 36, 84), "larger, nearby");
            Eq(true, PlacementRules.TryAlternative("anywhere", 36, 84), "larger, anywhere");
        }

        // Slides: up to maxSlide; stations only up to stationMaxSlide (DECISIONS-buildings #37).
        public static void TestSlides()
        {
            Eq(true, PlacementRules.SlideAllowed(false, 24, 24, 8), "building 24 m");
            Eq(false, PlacementRules.SlideAllowed(false, -28, 24, 8), "building 28 m");
            Eq(true, PlacementRules.SlideAllowed(true, -8, 24, 8), "station 8 m");
            Eq(false, PlacementRules.SlideAllowed(true, 12, 24, 8), "station 12 m");
            Eq(true, PlacementRules.SlideAllowed(true, 0, 24, 0), "station at its spot");
        }

        // Tunnels never block a lot; elevated roads block buildings but not stations (they sit under road bridges).
        public static void TestRoadsThatBlock()
        {
            Eq(true, PlacementRules.RoadCanBlock(false, false, false), "ground road, building");
            Eq(true, PlacementRules.RoadCanBlock(true, false, false), "ground road, station");
            Eq(false, PlacementRules.RoadCanBlock(false, true, false), "tunnel, building");
            Eq(true, PlacementRules.RoadCanBlock(false, false, true), "elevated, building");
            Eq(false, PlacementRules.RoadCanBlock(true, false, true), "elevated, station");
        }

        public static void TestPositionText()
        {
            Eq("(-2421, 439)", PlacementRules.Position(-2421.27, 439.4), "rounded");
        }

        // Decision 128: warn only when the CS1 city had them and none was placed.
        public static void TestSupplyWarnings()
        {
            Eq(0, PlacementRules.SupplyWarnings(7, 1, 4, 2).Count, "some placed");
            Eq(0, PlacementRules.SupplyWarnings(0, 0, 0, 0).Count, "none in CS1");
            var w = PlacementRules.SupplyWarnings(7, 0, 4, 0);
            Eq(2, w.Count, "both missing");
            Eq(true, w[0].StartsWith("This city has no power plant placed (7 in CS1): place them yourself, or re-run the package with 'let utilities move nearby'"), "power text");
            Eq(true, w[1].StartsWith("This city has no water supply placed (4 in CS1)"), "water text");
            Eq(1, PlacementRules.SupplyWarnings(0, 0, 3, 0).Count, "water only");
            Eq(true, PlacementRules.IsPower("power") && PlacementRules.IsWater("waterPump") && !PlacementRules.IsWater("sewage"), "families");
        }

        // DECISIONS-buildings #42: surface pumps keep, slide toward water, swap to groundwater, or are placed and reported dry.
        public static void TestPumpWater()
        {
            Eq("keep", PlacementRules.PumpWaterAction(20, 24, 30, false), "water under the lot");
            Eq("slide", PlacementRules.PumpWaterAction(50, 24, 30, false), "water 26 m past the lot");
            Eq("groundwater", PlacementRules.PumpWaterAction(float.PositiveInfinity, 24, 30, true), "no water, groundwater");
            Eq("dry", PlacementRules.PumpWaterAction(60, 24, 30, false), "no water, dry ground: placed as planned, reported");
            Eq(true, PlacementRules.NeedsRoad("waterPump") && PlacementRules.NeedsRoad("sewage") && !PlacementRules.NeedsRoad("park"), "families");
            Eq("Water Pump at CS2 (1, 2) is not connected to a road, so it gets no electricity: connect a road to it", PlacementRules.ConnectRoad("Water Pump", "(1, 2)"), "line");
            Eq(true, PlacementRules.NeedsTransformer("CoalPowerPlant01", "(1, 2)").StartsWith("CoalPowerPlant01 at CS2 (1, 2) feeds only the high-voltage grid: place a transformer station near it"), "transformer line");
        }

        // RC3: a depot type with none placed gets a warning (its lines run no vehicles).
        public static void TestDepotWarnings()
        {
            Eq("bus", PlacementRules.DepotKind("BusDepot01"), "bus");
            Eq("train", PlacementRules.DepotKind("RailYard02"), "rail yard");
            Eq("subway", PlacementRules.DepotKind("SubwayYard01"), "subway yard");
            Eq(null, PlacementRules.DepotKind("Hospital01"), "not a depot");
            Eq(true, PlacementRules.DepotWarning("bus", 1).StartsWith("This city has no bus depot placed (1 in CS1): its bus lines get no vehicles"), "text");
        }

        // Decision #152: per bus / tram kind with none placed, its first strict CS1 depot is retried with "nearby".
        public static void TestDepotRetries()
        {
            var kinds = new[] { "bus", null, "bus", "train", "tram", "tram", "taxi" };
            var placed = new[] { false, false, false, false, false, true, false };
            var strict = new[] { true, true, true, true, true, true, true };
            var r = PlacementRules.DepotRetries(kinds, placed, strict);
            Eq(1, r.Count, "one retry: bus (tram has one placed; train / taxi never)");
            Eq(0, r[0], "the first bus depot");
            Eq(0, PlacementRules.DepotRetries(new[] { "bus" }, new[] { false }, new[] { false }).Count, "already nearby (a default depot): no retry");
            Eq(0, PlacementRules.DepotRetries(new[] { "subway", "train" }, new[] { false, false }, new[] { true, true }).Count, "yards keep #83");
            Eq(2, PlacementRules.DepotRetries(new[] { "tram", "bus" }, new[] { false, false }, new[] { true, true }).Count, "one per kind");
        }

        // Decision #48: the station pose puts the platform on the CS1 track, keeping the facing.
        public static void TestAnchorPose()
        {
            double px, pz, fx, fz;
            // platform along local x, 10 m in front of the centre; CS1 track along world x at z = -5; facing +z
            PlacementRules.AnchorPose(1, 0, 0, 10, 1, 0, 0, -5, 0, 0, 0, 1, out px, out pz, out fx, out fz);
            Eq("0.00 1.00", fx.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " " + fz.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), "facing kept");
            Eq("-15.00", pz.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), "platform on the track: centre 15 m south");
            Eq("0.00", Math.Abs(px).ToString("F2", System.Globalization.CultureInfo.InvariantCulture), "no shift along");
            // the facing points the other way (-z): the station turns around, platform still on the track
            PlacementRules.AnchorPose(1, 0, 0, 10, 1, 0, 0, -5, 0, 0, 0, -1, out px, out pz, out fx, out fz);
            Eq("-1.00", fz.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), "facing -z");
            Eq("5.00", pz.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), "platform 10 m behind the centre now: centre at z = 5");
            // platform off-centre along its axis (local x = 12): centre moves 12 m back so the platform centre is level with CS1's
            PlacementRules.AnchorPose(1, 0, 12, 0, 1, 0, 0, 0, 0, 0, 0, 1, out px, out pz, out fx, out fz);
            Eq("-12.00", px.ToString("F2", System.Globalization.CultureInfo.InvariantCulture), "along");
        }

        // Decision #48: a single platform line goes onto the CS1 line nearest it, not between two.
        public static void TestNearestLines()
        {
            Eq(-9f, PlacementRules.NearestLines(new float[] { -25f, -9f }, 1, 20f), "1 platform, 2 CS1 tracks: the nearer one");
            Eq(-17f, PlacementRules.NearestLines(new float[] { -25f, -9f }, 2, 20f), "2 and 2: both");
            Eq(-9f, PlacementRules.NearestLines(new float[] { -9f }, 2, 0f), "fewer CS1 lines: their mean");
            Eq(5f, PlacementRules.NearestLines(new float[0], 1, 5f), "none: unchanged");
        }

        // The web report's wording: CS1 = plan − transform, CS2 = plan + import offset.
        public static void TestPlaceYourselfText()
        {
            var where = PlacementRules.Where(-551.3, -274.2, -1870, 165, 0, 0);
            Eq("CS1 (1319, -439) = CS2 (-551, -274)", where, "where");
            Eq("CS1 (0, 0) = CS2 (2100, -3900)", PlacementRules.Where(100, 100, 100, 100, 2000, -4000), "with an offset");
            Eq("Hospital: place it yourself at " + where + " (at its CS1 spot the lot overlaps a road)",
                PlacementRules.PlaceYourself("Hospital", where, "at its CS1 spot the lot overlaps a road"), "line");
            Eq("Building: place it yourself at " + where, PlacementRules.PlaceYourself(null, where, null), "no name, no reason");
        }
    }
}
