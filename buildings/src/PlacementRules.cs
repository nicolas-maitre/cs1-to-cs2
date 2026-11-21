// Game-free placement rules of the placer (Placement.cs), kept apart so buildings/tests/RulesTests runs them without the
// game's DLLs (CI). No Unity or Game types here.
//   - DECISIONS.md #106 (user): never moved to free land by default; a smaller alternative only.
//   - DECISIONS-buildings #36 / #37: stations stay at their CS1 spot (short slides), roads above / below don't block.
using System;

namespace Cs2Buildings
{
    public static class PlacementRules
    {
        public const string DefaultFallback = "skip";
        public const float DefaultStationMaxSlide = 8f;

        /// <summary>The fallback an item gets: stations always "skip"; else its own, else the run's (default "skip").</summary>
        public static string Fallback(bool station, string itemFallback, string runFallback)
        {
            if (station) return "skip";
            return itemFallback ?? runFallback ?? DefaultFallback;
        }

        /// <summary>
        /// Whether an alternative prefab is tried after the first usable one. With a free-land fallback, any alternative;
        /// without one ("skip"), only one whose lot area is at most the first one's: it may fit where the match didn't.
        /// </summary>
        public static bool TryAlternative(string fallback, int firstLotArea, int alternativeLotArea)
        {
            return fallback != "skip" || alternativeLotArea <= firstLotArea;
        }

        /// <summary>Whether a slide along the road is tried: up to maxSlide, and for stations up to stationMaxSlide.</summary>
        public static bool SlideAllowed(bool station, float slide, float maxSlide, float stationMaxSlide)
        {
            float s = Math.Abs(slide);
            if (s > maxSlide + 0.01f) return false;
            return !station || s <= stationMaxSlide + 0.01f;
        }

        /// <summary>Whether a road edge can block a lot: tunnels never do, elevated roads don't block stations.</summary>
        public static bool RoadCanBlock(bool station, bool tunnel, bool elevated)
        {
            if (tunnel) return false;
            if (station && elevated) return false;
            return true;
        }

        /// <summary>A position rounded to metres: "(x, z)".</summary>
        public static string Position(double x, double z)
        {
            return "(" + Math.Round(x).ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Math.Round(z).ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        /// <summary>
        /// Where to place a building yourself, in the web report's words: "CS1 (x, z) = CS2 (x, z)". A plan item's x/z is
        /// in plan coordinates (CS1 + transform dx/dz); the CS2 position adds the import offset.
        /// </summary>
        public static string Where(double planX, double planZ, double dx, double dz, double offsetX, double offsetZ)
        {
            return "CS1 " + Position(planX - dx, planZ - dz) + " = CS2 " + Position(planX + offsetX, planZ + offsetZ);
        }

        /// <summary>One report line for a building that wasn't placed: "Hospital: place it yourself at CS1 (…) = CS2 (…) (why)".</summary>
        public static string PlaceYourself(string name, string where, string why)
        {
            return (string.IsNullOrEmpty(name) ? "Building" : name) + ": place it yourself at " + where + (string.IsNullOrEmpty(why) ? "" : " (" + why + ")");
        }
    }
}
