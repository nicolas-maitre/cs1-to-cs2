using System;
using System.Collections;
using System.IO;
using ColossalFramework;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>
    /// CS1 mod entry: Options → Mod Settings → "CS1 → CS2 Exporter".
    /// The export only reads game state; it never saves the city. While it runs (a few seconds) the simulation is
    /// paused and resumed afterwards.
    /// </summary>
    public sealed class Mod : IUserMod
    {
        public string Name => ModInfo.Name + " " + ModInfo.Version;
        public string Description => "Exports the loaded city (roads, terrain, water, zoning, districts, transit, TM:PE, Node Controller, buildings) to a .cs1x file for the CS1 → CS2 converter.";

        public Mod() { Settings.Register(); }

        public void OnSettingsUI(UIHelperBase helper)
        {
            try { ExportUi.Build(helper); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>The button handler without the UI wiring; public so a script can run it (exec ... --type Cs1Exporter.Mod --method ExportWithDialog).</summary>
        public static string ExportWithDialog()
        {
            ExportUi.StartExport();
            return "export started (" + ExportRunner.Status + ")";
        }
    }

    /// <summary>Settings in the mod's own Cs1Exporter.cgs (ColossalFramework settings file; nothing else is touched).</summary>
    public static class Settings
    {
        public const string FileName = "Cs1Exporter";
        public const string DefaultWebTool = "https://nicolas-maitre.github.io/cs1-to-cs2/";
        /// <summary>The default before 1.2.2 (the dev server); a saved value equal to it follows the new default.</summary>
        private const string OldDefaultWebTool = "http://localhost:5178";

        public static void Register()
        {
            try
            {
                if (GameSettings.FindSettingsFileByName(FileName) == null)
                    GameSettings.AddSettingsFile(new SettingsFile { fileName = FileName });
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static SavedString s_OutputDir, s_WebTool;

        public static string OutputDir
        {
            get
            {
                try
                {
                    if (s_OutputDir == null) s_OutputDir = new SavedString("outputDir", FileName, "", true);
                    var v = s_OutputDir.value;
                    return string.IsNullOrEmpty(v) ? Exporter.DefaultOutputDir : v;
                }
                catch { return Exporter.DefaultOutputDir; }
            }
            set { try { if (s_OutputDir == null) s_OutputDir = new SavedString("outputDir", FileName, "", true); s_OutputDir.value = value ?? ""; } catch (Exception e) { Debug.LogException(e); } }
        }

        public static string WebTool
        {
            get
            {
                try
                {
                    if (s_WebTool == null) s_WebTool = new SavedString("webToolUrl", FileName, DefaultWebTool, true);
                    var v = s_WebTool.value;
                    return string.IsNullOrEmpty(v) || v.TrimEnd('/') == OldDefaultWebTool ? DefaultWebTool : v;
                }
                catch { return DefaultWebTool; }
            }
            set { try { if (s_WebTool == null) s_WebTool = new SavedString("webToolUrl", FileName, DefaultWebTool, true); s_WebTool.value = value ?? ""; } catch (Exception e) { Debug.LogException(e); } }
        }

        public static bool Include(string section)
        {
            try { return new SavedBool("include_" + section, FileName, true, true).value; } catch { return true; }
        }

        public static void SetInclude(string section, bool on)
        {
            try { new SavedBool("include_" + section, FileName, true, true).value = on; } catch (Exception e) { Debug.LogException(e); }
        }

        public static ExportOptions Options()
        {
            var o = new ExportOptions { PauseSimulation = true };
            foreach (var s in ExportOptions.Optional) if (!Include(s)) o.Excluded.Add(s);
            return o;
        }
    }

    /// <summary>Options panel content and dialogs.</summary>
    public static class ExportUi
    {
        private static UILabel s_Status;
        private static UIButton s_ExportButton;
        private static UITextField s_Folder;

        private static readonly string[][] SectionLabels =
        {
            new[] { "zones", "Zoning (zone blocks and cells)" },
            new[] { "water", "Water (sea level, sources, water depth)" },
            new[] { "districts", "Districts and park / industry / campus areas" },
            new[] { "transit", "Transport lines and stops" },
            new[] { "tmpe", "TM:PE settings (if TM:PE is installed)" },
            new[] { "nodecontroller", "Node Controller settings (if installed)" },
            new[] { "buildings", "Buildings" },
            new[] { "assets", "Asset catalogue (prefabs in use)" },
            new[] { "props", "Free-standing props" },
            new[] { "trees", "Trees" },
        };

        public static void Build(UIHelperBase helper)
        {
            var main = helper.AddGroup(ModInfo.Name + " " + ModInfo.Version);
            s_ExportButton = main.AddButton("Export city", StartExport) as UIButton;
            s_Status = AddLabel(main, ExportRunner.Status, 700);
            AddLabel(main, "Writes a .cs1x file with the loaded city's data. The city itself is never saved or changed; the simulation is paused while exporting (a few seconds).", 700);

            var output = helper.AddGroup("Output");
            s_Folder = output.AddTextfield("Export folder", Settings.OutputDir, _ => { }, v => { Settings.OutputDir = v; }) as UITextField;
            if (s_Folder != null) s_Folder.width = 640;
            output.AddButton("Open export folder", () =>
            {
                try { var d = Settings.OutputDir; Directory.CreateDirectory(d); Application.OpenURL("file:///" + d.Replace('\\', '/')); }
                catch (Exception e) { Dialog("Can't open the folder", Report.Describe(e), true); }
            });
            output.AddButton("Use the default folder", () =>
            {
                Settings.OutputDir = "";
                if (s_Folder != null) s_Folder.text = Settings.OutputDir;
            });
            var web = output.AddTextfield("Web tool address (to convert the file)", Settings.WebTool, _ => { }, v => { Settings.WebTool = v; }) as UITextField;
            if (web != null) web.width = 640;
            output.AddButton("Open web tool", () =>
            {
                try { Application.OpenURL(Settings.WebTool); }
                catch (Exception e) { Dialog("Can't open the web tool", Report.Describe(e), true); }
            });

            var sections = helper.AddGroup("Include in the export");
            AddLabel(sections, "Roads, network types, IMT markings and terrain are always included.", 700);
            foreach (var pair in SectionLabels)
            {
                var key = pair[0];
                sections.AddCheckbox(pair[1], Settings.Include(key), on => Settings.SetInclude(key, on));
            }

            var about = helper.AddGroup("About");
            AddLabel(about, "Version " + ModInfo.Version + " (build " + typeof(Exporter).Assembly.GetName().Version + ").\n" +
                "Privacy: the .cs1x file contains only data about this city (map, networks, buildings, settings of the traffic mods) and the list of enabled mods. " +
                "Nothing is uploaded anywhere; you decide where the file goes.", 700);
        }

        private static UILabel AddLabel(UIHelperBase group, string text, float width)
        {
            var panel = (group as UIHelper) != null ? (group as UIHelper).self as UIComponent : null;
            if (panel == null) return null;
            var l = panel.AddUIComponent<UILabel>();
            l.wordWrap = true;
            l.autoHeight = true;
            l.width = width;
            l.textScale = 0.9f;
            l.text = text;
            return l;
        }

        internal static void SetStatus(string text)
        {
            try { if (s_Status != null) s_Status.text = text; } catch { }
            try { if (s_ExportButton != null) s_ExportButton.isEnabled = !ExportRunner.Running; } catch { }
        }

        public static void StartExport()
        {
            try
            {
                if (ExportRunner.Running) return;
                if (!GameInfo.CityLoaded())
                {
                    Dialog("No city loaded", "Load a city first, then open Options → Mod Settings and press Export city again.", false);
                    return;
                }
                if (s_Folder != null && !string.IsNullOrEmpty(s_Folder.text) && s_Folder.text != Settings.OutputDir) Settings.OutputDir = s_Folder.text;
                var dir = Settings.OutputDir;
                ExportRunner.Start(Exporter.DefaultPath(dir), Settings.Options());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Dialog("Export failed", Report.Describe(e) + "\n\n(see output_log.txt)", true);
            }
        }

        internal static void Finished(Exporter.Result r)
        {
            int n, ok = 0, absent = 0;
            foreach (var v in r.Sections.Values) { if (v == "ok") ok++; else if (v == "absent") absent++; }
            var msg = "Exported " + (GameInfo.CityName() ?? "the city") + " (" + (r.Bytes / 1048576.0).ToString("F1") + " MB, " + r.Seconds.ToString("F1") + " s) to:\n" + r.Path + "\n\n";
            msg += "Road and track segments: " + r.Segments + "\n";
            if (r.Counts.TryGetValue("buildings", out n)) msg += "Buildings: " + n + "\n";
            if (r.Counts.TryGetValue("zoneBlocks", out n)) msg += "Zone blocks: " + n + "\n";
            if (r.Counts.TryGetValue("districts", out n)) msg += "Districts: " + n + (r.Counts.TryGetValue("parkAreas", out int pa) ? ", park / industry areas: " + pa : "") + "\n";
            if (r.Counts.TryGetValue("transitLines", out n)) msg += "Transport lines: " + n + "\n";
            if (r.ImtFillers > 0) msg += "IMT fills: " + r.ImtFillers + "\n";
            msg += "\n" + ok + " parts exported" + (absent > 0 ? ", " + absent + " skipped (turned off or mod not installed)" : "") + ".";
            if (r.Failed > 0) msg += "\n" + r.Failed + " part(s) failed; the rest of the file is fine. Details are in report.json inside the file and in output_log.txt.";
            msg += "\n\nNext step: open the CS1 → CS2 web tool (" + Settings.WebTool + ") and drop this file on it.";
            Dialog(ModInfo.Name + ": export finished", msg, false);
        }

        internal static void Failed(Exception e)
        {
            Dialog(ModInfo.Name + ": export failed",
                Report.Describe(e) + "\n\nNothing was written. Check the export folder in the options (it must be writable), then try again. Details are in output_log.txt.", true);
        }

        internal static void Dialog(string title, string message, bool error)
        {
            Debug.Log("[Cs1Exporter] " + title + ": " + message);
            try { UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel").SetMessage(title, message, error); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    /// <summary>Advances Exporter.ExportRoutine once per frame so the game stays responsive and the status updates.</summary>
    public sealed class ExportRunner : MonoBehaviour
    {
        private static ExportRunner s_Instance;
        private IEnumerator _routine;
        private ExportProgress _progress;

        public static bool Running { get { return s_Instance != null && s_Instance._routine != null; } }
        public static string Status { get; private set; } = "Ready.";

        public static void Start(string path, ExportOptions options)
        {
            if (s_Instance == null)
            {
                var go = new GameObject("Cs1ExporterRunner");
                DontDestroyOnLoad(go);
                s_Instance = go.AddComponent<ExportRunner>();
            }
            s_Instance._progress = new ExportProgress();
            s_Instance._routine = Exporter.ExportRoutine(path, options, s_Instance._progress);
            SetStatus("Exporting…");
        }

        private static void SetStatus(string s)
        {
            Status = s;
            ExportUi.SetStatus(s);
        }

        private void Update()
        {
            if (_routine == null) return;
            bool more;
            try { more = _routine.MoveNext(); }
            catch (Exception e)
            {
                _routine = null;
                Debug.LogException(e);
                SetStatus("Export failed: " + Report.Describe(e));
                ExportUi.Failed(e);
                return;
            }
            if (more)
            {
                SetStatus(string.Format("Exporting… {0}/{1}: {2}", _progress.Step, _progress.Total, _progress.Label));
                return;
            }
            _routine = null;
            var r = _progress.Result;
            if (r == null) { SetStatus("Export stopped."); return; }
            SetStatus("Last export: " + Path.GetFileName(r.Path) + " (" + (r.Bytes / 1048576.0).ToString("F1") + " MB, " + r.Seconds.ToString("F1") + " s" + (r.Failed > 0 ? ", " + r.Failed + " section(s) failed" : "") + ")");
            ExportUi.Finished(r);
        }

        private void OnDestroy()
        {
            // Game quitting or the mod reloading mid-export: dispose the routine so its finally block cleans up.
            var d = _routine as IDisposable;
            _routine = null;
            if (d != null) { try { d.Dispose(); } catch { } }
            if (s_Instance == this) s_Instance = null;
        }
    }
}
