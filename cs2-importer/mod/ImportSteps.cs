// Hook for other mods' import steps (transit, buildings): they run after the roads and the built-in steps, in the same
// ToolUpdate coroutine, and their result lands in the combined import report. A mod without a compile-time reference
// registers by reflection:
//
//   var t = Type.GetType("Cs2ImporterMod.ImportSteps, Cs2ImporterMod");
//   t?.GetMethod("Register").Invoke(null, new object[] { "transit", (Func<Dictionary<string, object>, IEnumerator>)MyStep });
//
// The step gets the import's args (plan, offsetX/Z, heights, ...) and returns a coroutine whose last yielded value is
// its result (a Dictionary for the report; "error" / "skipped" keys are shown as such).
using System;
using System.Collections;
using System.Collections.Generic;

namespace Cs2ImporterMod
{
    public static class ImportSteps
    {
        static readonly List<KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>>> s_Steps =
            new List<KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>>>();

        public static void Register(string name, Func<Dictionary<string, object>, IEnumerator> step)
        {
            s_Steps.RemoveAll(kv => kv.Key == name);
            s_Steps.Add(new KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>>(name, step));
            Mod.Log.Info("import step registered: " + name);
        }

        internal static IEnumerable<KeyValuePair<string, Func<Dictionary<string, object>, IEnumerator>>> Registered { get { return s_Steps.ToArray(); } }
    }
}
