// Testing the deployed importer mod by script (no mouse), as exec op "mod" of the importer library: sets Options page
// values and presses its buttons through the same Actions the settings UI calls.
//
//   gamectl cs2 exec cs2-importer/bin/Release/net48/Cs2Importer.dll
//     --args '{"op":"mod","set":{"PackageFolder":"...","ClearArea":true},"press":"Check"}'
//   ... '{"op":"mod","press":"Import","wait":true}' --phase tool --pump tool    (waits for the import to finish)
//
// The exec library is a separate assembly ("Cs2Importer"), so it reaches the loaded mod ("Cs2ImporterMod") by reflection.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Cs2Importer
{
    public static class ModScript
    {
        /// <summary>
        /// args: set {property: value}, press "Check" | "Import" | "InstallRoadBuilderConfigs", wait (bool, Import: wait for
        /// the result). Returns the settings, the status text and the action's result.
        /// </summary>
        public static object Drive(Dictionary<string, object> args)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Cs2ImporterMod" && a.GetType("Cs2ImporterMod.Mod")?.GetProperty("Instance")?.GetValue(null) != null);
            if (asm == null) throw new InvalidOperationException("the Cs2ImporterMod mod is not loaded");
            var mod = asm.GetType("Cs2ImporterMod.Mod").GetProperty("Instance").GetValue(null);
            var setting = mod.GetType().GetProperty("Settings").GetValue(mod);
            var st = setting.GetType();

            if (args != null && args.TryGetValue("set", out object setObj) && setObj is Dictionary<string, object> set)
                foreach (var kv in set)
                {
                    var p = st.GetProperty(kv.Key) ?? throw new ArgumentException("no setting " + kv.Key);
                    if (p.PropertyType == typeof(bool) && !p.CanRead) throw new ArgumentException(kv.Key + " is a button: use \"press\"");
                    p.SetValue(setting, Convert.ChangeType(kv.Value, p.PropertyType, System.Globalization.CultureInfo.InvariantCulture));
                }

            object pressed = null;
            string press = args != null && args.TryGetValue("press", out object pr) ? pr as string : null;
            if (press != null)
            {
                var m = asm.GetType("Cs2ImporterMod.Actions").GetMethod(press) ?? throw new ArgumentException("no action " + press);
                pressed = m.Invoke(null, new[] { setting });
            }
            bool wait = press == "Import" && args.TryGetValue("wait", out object w) && w is bool wb && wb;
            if (wait) return WaitForImport(asm, setting, pressed);
            return Snapshot(setting, press, pressed);
        }

        static IEnumerator WaitForImport(Assembly asm, object setting, object started)
        {
            var busy = asm.GetType("Cs2ImporterMod.ImportSystem").GetProperty("Busy");
            var progress = new List<object>();
            string last = null;
            int frames = 0;
            while ((bool)busy.GetValue(null) && frames++ < 200000)
            {
                var text = (string)setting.GetType().GetProperty("StatusText").GetValue(setting);
                if (text != last) { progress.Add(text); last = text; }
                yield return null;
            }
            var snap = Snapshot(setting, "Import", started);
            snap["progress_seen"] = progress;
            snap["frames"] = frames;
            yield return snap;
        }

        static Dictionary<string, object> Snapshot(object setting, string press, object pressed)
        {
            var st = setting.GetType();
            var d = new Dictionary<string, object> { { "pressed", press }, { "result", pressed } };
            foreach (var n in new[] { "PackageFolder", "ClearArea", "TerrainIsConverted", "OffsetX", "OffsetZ", "StatusText", "LastReportPath" })
            {
                var v = st.GetProperty(n).GetValue(setting);
                d[n] = v is float f ? (object)(double)f : v;
            }
            return d;
        }
    }
}
