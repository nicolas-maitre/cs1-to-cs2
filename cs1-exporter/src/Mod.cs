using System;
using System.IO;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>
    /// CS1 mod entry. Options → Mod Settings → "CS1 → CS2 Exporter" → "Export city" writes
    /// %LOCALAPPDATA%\Colossal Order\Cities_Skylines\Cs1Exporter\&lt;city&gt;-&lt;timestamp&gt;.cs1x.
    /// The export only reads game state; it never saves the city.
    /// </summary>
    public sealed class Mod : IUserMod
    {
        public string Name => "CS1 → CS2 Exporter";
        public string Description => "Exports the loaded city (roads, NetInfos, IMT fills, terrain) to a .cs1x file for the CS2 converter.";

        public void OnSettingsUI(UIHelperBase helper)
        {
            var group = helper.AddGroup("CS1 → CS2 Exporter");
            group.AddButton("Export city", () => ExportWithDialog());
            group.AddButton("Open export folder", () =>
            {
                try
                {
                    Directory.CreateDirectory(Exporter.DefaultOutputDir);
                    Application.OpenURL("file:///" + Exporter.DefaultOutputDir.Replace('\\', '/'));
                }
                catch (Exception e) { Debug.LogException(e); }
            });
        }

        /// <summary>The button handler. Public so it can be exercised by script: gamectl cs1 exec ... --type Cs1Exporter.Mod --method ExportWithDialog</summary>
        public static string ExportWithDialog()
        {
            string title = "CS1 → CS2 Exporter", message;
            try
            {
                if (!GameInfo.CityLoaded())
                {
                    message = "Load a city first, then press Export city again.";
                }
                else
                {
                    var r = Exporter.Export(Exporter.DefaultPath());
                    message = "Exported to:\n" + r.Path + "\n\n" + r.Summary();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                message = "Export FAILED: " + Report.Describe(e) + "\n(see output_log.txt)";
            }

            Debug.Log("[Cs1Exporter] " + message);
            try
            {
                UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel").SetMessage(title, message, false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            return message;
        }
    }
}
