// Public entry points for mods. Compile buildings/src/*.cs into a mod (as cs2-importer/mod does with its own src/) and
// register the steps with the importer's step hook:
//
//   Cs2Buildings.Steps.RegisterAll(Cs2ImporterMod.ImportSteps.Register);
//
// Each step takes the import's args and returns a coroutine (one MoveNext per frame, from a ToolUpdate system); the last
// yielded value is its result, a Dictionary for the import report ("skipped" / "error" keys as the hook expects).
//
// Args used (all optional except plan):
//   plan        path of cs1conv-plan.json (or its folder). Sections are read from it: "buildings" (written by the web
//               tool), and "objects" / "districts" / "surfaces" when present; otherwise from a cs1conv-objects.json next
//               to the plan, or the path in `objectsPlan`.
//   offsetX/Z   the importer's offset (same as for the roads)
//   utilities.* options of the "utilities" step (power lines and pipes from plan.utilities, buildings decision #40)
//   buildings.* / objects.* run options, e.g. "fallback", "nearbyRadius", "batchSize" (see buildings/README.md), passed
//               through unchanged: the step args are the import args plus these keys without the prefix.
//
// Nothing here depends on the exec bridge: no statics survive between calls anyway, everything works through the
// EntityManager from the main thread, and the placer creates no end-frame command buffers (so it also runs from exec).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameBridge;

namespace Cs2Buildings
{
    public static class Steps
    {
        public const string Version = "0.3.3";

        /// <summary>Registers "buildings", "objects", "districts", "surfaces", "utilities" and "powerhook" (in that order) with a step hook.</summary>
        public static void RegisterAll(Action<string, Func<Dictionary<string, object>, IEnumerator>> register)
        {
            register("buildings", Buildings);
            register("objects", Objects);
            register("districts", Districts);
            register("surfaces", Surfaces);
            // after the buildings: CS1 power lines and pipes end at plants, pumps and outlets, and snap to their markers
            register("utilities", UtilityNets);
            register("powerhook", PowerHookStep);
        }

        /// <summary>Places plan.buildings.items (or a standalone cs1conv-buildings.json given as `buildingsPlan`).</summary>
        public static IEnumerator Buildings(Dictionary<string, object> args)
        {
            var a = Prefixed(args, "buildings.");
            string file = P.Str(args, "buildingsPlan", null) ?? PlanFile(args);
            if (file == null || !HasSection(file, "buildings", true)) return Skipped("no buildings in this package");
            a["plan"] = file;
            return (IEnumerator)Placement.Place(a);
        }

        public static IEnumerator Objects(Dictionary<string, object> args) { return Section(args, "objects", "objects.", a => (IEnumerator)Cs2Buildings.Objects.Place(a)); }

        public static IEnumerator Districts(Dictionary<string, object> args)
        {
            return Section(args, "districts", "districts.", a => { a["section"] = "districts"; return (IEnumerator)Cs2Buildings.Objects.Areas(a); });
        }

        /// <summary>The player's off-road CS1 power lines and pipes (plan.utilities, web 0.8.0+): DECISIONS.md #115.</summary>
        public static IEnumerator UtilityNets(Dictionary<string, object> args)
        {
            var a = Prefixed(args, "utilities.");
            string file = PlanFile(args);
            if (file == null || !HasSection(file, "utilities", false)) return Skipped("none in this package");
            a["plan"] = file;
            return (IEnumerator)Utilities.Place(a);
        }

        /// <summary>Connects placed power plants and wind turbines to the grid (decision #44): transformers + lines.</summary>
        public static IEnumerator PowerHookStep(Dictionary<string, object> args)
        {
            return (IEnumerator)PowerHook.Start(Prefixed(args, "powerhook."));
        }

        public static IEnumerator Surfaces(Dictionary<string, object> args)
        {
            return Section(args, "surfaces", "surfaces.", a => { a["section"] = "surfaces"; return (IEnumerator)Cs2Buildings.Objects.Areas(a); });
        }

        // ---------------------------------------------------------------- helpers

        static IEnumerator Section(Dictionary<string, object> args, string section, string prefix, Func<Dictionary<string, object>, IEnumerator> run)
        {
            var file = ObjectsFile(args, section);
            if (file == null) return Skipped("none in this package");
            var a = Prefixed(args, prefix);
            a["plan"] = file;
            return run(a);
        }

        /// <summary>The import args, plus "&lt;prefix&gt;key" options as plain "key" (step-specific options).</summary>
        static Dictionary<string, object> Prefixed(Dictionary<string, object> args, string prefix)
        {
            var a = new Dictionary<string, object>(args ?? new Dictionary<string, object>());
            foreach (var kv in args ?? new Dictionary<string, object>())
                if (kv.Key.StartsWith(prefix, StringComparison.Ordinal)) a[kv.Key.Substring(prefix.Length)] = kv.Value;
            return a;
        }

        static string PlanFile(Dictionary<string, object> args)
        {
            var p = P.Str(args, "plan", null);
            if (string.IsNullOrEmpty(p)) return null;
            p = Environment.ExpandEnvironmentVariables(p);
            if (Directory.Exists(p)) p = Path.Combine(p, "cs1conv-plan.json");
            return File.Exists(p) ? p : null;
        }

        /// <summary>The plan if it has the section, else objectsPlan / cs1conv-objects.json next to the plan if that has it.</summary>
        static string ObjectsFile(Dictionary<string, object> args, string section)
        {
            var plan = PlanFile(args);
            if (plan != null && HasSection(plan, section, false)) return plan;
            var other = P.Str(args, "objectsPlan", null);
            if (other == null && plan != null) other = Path.Combine(Path.GetDirectoryName(plan), "cs1conv-objects.json");
            if (other != null)
            {
                other = Environment.ExpandEnvironmentVariables(other);
                if (File.Exists(other) && HasSection(other, section, false)) return other;
            }
            return null;
        }

        /// <summary>True when the JSON file has the section (and, for buildings, an items array; a standalone buildings file counts).</summary>
        static bool HasSection(string file, string section, bool allowStandalone)
        {
            try
            {
                var root = MiniJson.Parse(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (root == null) return false;
                object v;
                if (root.TryGetValue(section, out v) && v is Dictionary<string, object>) return true;
                return allowStandalone && root.ContainsKey("items");
            }
            catch { return false; }
        }

        static IEnumerator Skipped(string why)
        {
            yield return new Dictionary<string, object> { { "skipped", why } };
        }
    }
}
