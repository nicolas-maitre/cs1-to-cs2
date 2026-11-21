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
    }
}
