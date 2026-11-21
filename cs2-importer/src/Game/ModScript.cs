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
using System.Runtime.InteropServices;
using System.Threading;

namespace Cs2Importer
{
    public static class ModScript
    {
        /// <summary>
        /// args:
        /// - set {property: value}: Options values;
        /// - locale "fr-FR" | "en-US": switches the game's language first (the Options page follows);
        /// - press: any public Actions method taking the Setting (Check, Browse, Import, PrepareMap, Cancel, RemoveImported…);
        /// - answerDialog "path": for Browse, a background thread fills in Windows' Open dialog with that path and presses
        ///   Open (or "" to press Cancel), since the game is blocked while the dialog is open;
        /// - wait (bool): wait until the job the press started is done;
        /// - labels [option names]: the active locale's labels for those options, to check translations;
        /// - setStatic {"Type.Field": value}: test hooks on the mod's static fields (e.g. Actions.TestRoadBuilderFolder);
        /// - census (bool): the counts Remove imported works on (Undo.Take: top-level roads, nodes, buildings, objects,
        ///   areas, routes); items (bool): the "Packages on this PC" entries.
        /// Returns the settings, the status text and the action's result.
        /// </summary>
        public static object Drive(Dictionary<string, object> args)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Cs2ImporterMod" && a.GetType("Cs2ImporterMod.Mod")?.GetProperty("Instance")?.GetValue(null) != null);
            if (asm == null) throw new InvalidOperationException("the Cs2ImporterMod mod is not loaded");
            var mod = asm.GetType("Cs2ImporterMod.Mod").GetProperty("Instance").GetValue(null);
            var setting = mod.GetType().GetProperty("Settings").GetValue(mod);
            var st = setting.GetType();

            if (args != null && args.TryGetValue("locale", out object loc) && loc is string locale)
                Game.SceneFlow.GameManager.instance.localizationManager.SetActiveLocale(locale);

            if (args != null && args.TryGetValue("setStatic", out object ss) && ss is Dictionary<string, object> statics)
                foreach (var kv in statics)
                {
                    int dot = kv.Key.LastIndexOf('.');
                    var f = asm.GetType("Cs2ImporterMod." + kv.Key.Substring(0, dot))?.GetField(kv.Key.Substring(dot + 1), BindingFlags.Public | BindingFlags.Static)
                        ?? throw new ArgumentException("no static field " + kv.Key);
                    f.SetValue(null, kv.Value);
                }

            if (args != null && args.TryGetValue("set", out object setObj) && setObj is Dictionary<string, object> set)
                foreach (var kv in set)
                {
                    var p = st.GetProperty(kv.Key) ?? throw new ArgumentException("no setting " + kv.Key);
                    if (p.PropertyType == typeof(bool) && !p.CanRead) throw new ArgumentException(kv.Key + " is a button: use \"press\"");
                    p.SetValue(setting, kv.Value == null ? null : Convert.ChangeType(kv.Value, p.PropertyType, System.Globalization.CultureInfo.InvariantCulture));
                }

            object pressed = null;
            string press = args != null && args.TryGetValue("press", out object pr) ? pr as string : null;
            Thread answer = null;
            if (press != null)
            {
                if (args.TryGetValue("answerDialog", out object ad) && ad is string path)
                {
                    var title = (string)asm.GetType("Cs2ImporterMod.Packages").GetProperty("DialogTitle").GetValue(null);
                    answer = new Thread(() => AnswerDialog(title, path)) { IsBackground = true };
                    answer.Start();
                }
                var m = asm.GetType("Cs2ImporterMod.Actions").GetMethod(press) ?? throw new ArgumentException("no action " + press);
                pressed = m.Invoke(null, new[] { setting });
            }
            var snap0 = Snapshot(setting, press, pressed);
            if (answer != null) snap0["dialog"] = s_DialogResult;
            AddLabels(args, setting, snap0);
            if (args != null && args.TryGetValue("census", out object ce) && ce is bool cb && cb) snap0["census"] = Census(asm);
            if (args != null && args.TryGetValue("items", out object it) && it is bool ib && ib)
            {
                var items = (Array)setting.GetType().GetMethod("GetPackageItems").Invoke(setting, null);
                var names = new List<object>();
                foreach (var x in items)
                {
                    var t = x.GetType();
                    var v = (object)t.GetField("displayName")?.GetValue(x) ?? t.GetProperty("displayName")?.GetValue(x);
                    names.Add(v?.ToString());
                }
                snap0["items"] = names;
            }
            bool wait = press != null && args.TryGetValue("wait", out object w) && w is bool wb && wb;
            if (wait) return WaitForJob(asm, setting, snap0);
            return snap0;
        }

        static void AddLabels(Dictionary<string, object> args, object setting, Dictionary<string, object> snap)
        {
            if (args == null || !args.TryGetValue("labels", out object lo) || !(lo is List<object> names)) return;
            var lm = Game.SceneFlow.GameManager.instance.localizationManager;
            var dict = lm.activeDictionary;
            var get = setting.GetType().GetMethod("GetOptionLabelLocaleID");
            var labels = new Dictionary<string, object> { { "_locale", lm.activeLocaleId } };
            foreach (var n in names.Cast<string>())
            {
                var id = (string)get.Invoke(setting, new object[] { n });
                labels[n] = dict.TryGetValue(id, out string text) ? text : null;
            }
            snap["labels"] = labels;
        }

        static Dictionary<string, object> Census(Assembly asm)
        {
            var snap = asm.GetType("Cs2ImporterMod.Undo").GetMethod("Take").Invoke(null, null);
            var sets = (IDictionary)snap.GetType().GetField("Sets").GetValue(snap);
            var r = new Dictionary<string, object>();
            foreach (DictionaryEntry kv in sets) r[(string)kv.Key] = (int)kv.Value.GetType().GetProperty("Count").GetValue(kv.Value); // HashSet<Entity>
            return r;
        }

        static IEnumerator WaitForJob(Assembly asm, object setting, Dictionary<string, object> started)
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
            var snap = Snapshot(setting, (string)started["pressed"], started["result"]);
            if (started.ContainsKey("census")) snap["census"] = Census(asm);
            snap["progress_seen"] = progress;
            snap["frames"] = frames;
            yield return snap;
        }

        static Dictionary<string, object> Snapshot(object setting, string press, object pressed)
        {
            var st = setting.GetType();
            var d = new Dictionary<string, object> { { "pressed", press }, { "result", pressed } };
            foreach (var n in new[] { "PackageFolder", "ClearArea", "KeepMapTerrain", "OffsetX", "OffsetZ", "StatusText", "LastReportPath", "RecentPackages", "LastUndo" })
            {
                var p = st.GetProperty(n);
                if (p == null) continue;
                var v = p.GetValue(setting);
                d[n] = v is float f ? (object)(double)f : v;
            }
            return d;
        }

        // ---------------------------------------------------------------- answering Windows' Open dialog (tests)

        static string s_DialogResult;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string cls, string title);
        [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr dlg, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageW(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr h, int msg, IntPtr w, IntPtr l);

        /// <summary>Waits up to 15 s for the dialog (class #32770, our title), types the path into its file-name box and
        /// presses Open (IDOK), or Cancel when the path is empty.</summary>
        static void AnswerDialog(string title, string path)
        {
            const int WM_SETTEXT = 0x000C, WM_COMMAND = 0x0111, IDOK = 1, IDCANCEL = 2, FileNameCombo = 0x047C;
            s_DialogResult = "not found";
            for (int i = 0; i < 150; i++)
            {
                Thread.Sleep(100);
                var dlg = FindWindowW("#32770", title);
                if (dlg == IntPtr.Zero) continue;
                Thread.Sleep(500); // let it finish building its controls
                if (string.IsNullOrEmpty(path))
                {
                    SendMessageW(dlg, WM_COMMAND, (IntPtr)IDCANCEL, IntPtr.Zero);
                    s_DialogResult = "cancelled";
                    return;
                }
                var combo = GetDlgItem(dlg, FileNameCombo);
                var edit = combo != IntPtr.Zero ? FindWindowExW(combo, IntPtr.Zero, "Edit", null) : IntPtr.Zero;
                if (edit == IntPtr.Zero && combo != IntPtr.Zero)
                {
                    var inner = FindWindowExW(combo, IntPtr.Zero, "ComboBox", null);
                    if (inner != IntPtr.Zero) edit = FindWindowExW(inner, IntPtr.Zero, "Edit", null);
                }
                if (edit == IntPtr.Zero) { s_DialogResult = "no file-name box"; SendMessageW(dlg, WM_COMMAND, (IntPtr)IDCANCEL, IntPtr.Zero); return; }
                SendMessageW(edit, WM_SETTEXT, IntPtr.Zero, path);
                SendMessageW(dlg, WM_COMMAND, (IntPtr)IDOK, IntPtr.Zero);
                s_DialogResult = "answered";
                return;
            }
        }
    }
}
