using System;
using System.IO;
using ColossalFramework.IO;
using ColossalFramework.UI;
using ICities;
using UnityEngine;

namespace M0NetworkDump
{
    /// <summary>
    /// M0 spike 2 entry point. Adds a button in Options → Mod Settings → "M0 Network Dump".
    /// Clicking it (while a city is loaded) writes a JSON dump of the road network,
    /// the NetInfo lane layouts in use and Intersection Marking Tool fill polygons to
    /// %LOCALAPPDATA%\Colossal Order\Cities_Skylines\M0NetworkDump\.
    /// </summary>
    public sealed class Mod : IUserMod
    {
        public string Name => "M0 Network Dump (CS1→CS2 spike)";
        public string Description => "Dumps NetManager nodes/segments, lane layouts and IMT fillers to JSON.";

        public static string OutputDir => Path.Combine(DataLocation.localApplicationData, "M0NetworkDump");

        public void OnSettingsUI(UIHelperBase helper)
        {
            var group = helper.AddGroup("M0 Network Dump");
            group.AddButton("Dump network now", OnDumpClicked);
            group.AddButton("Open output folder", () =>
            {
                Directory.CreateDirectory(OutputDir);
                Application.OpenURL("file:///" + OutputDir.Replace('\\', '/'));
            });
        }

        private static void OnDumpClicked()
        {
            string message;
            try
            {
                if (!LoadingTracker.InGame)
                {
                    message = "Load a city first, then press the button again.";
                }
                else
                {
                    var path = NetworkDumper.Dump(OutputDir);
                    message = "Network dump written to:\n" + path + "\n\n" + NetworkDumper.LastSummary;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                message = "Dump FAILED: " + e.GetType().Name + ": " + e.Message + "\n(see output_log.txt)";
            }

            Debug.Log("[M0NetworkDump] " + message);
            try
            {
                UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel")
                    .SetMessage("M0 Network Dump", message, false);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }

    /// <summary>Tracks whether a city (not the asset/map editor) is loaded.</summary>
    public sealed class LoadingTracker : LoadingExtensionBase
    {
        public static bool InGame { get; private set; }

        public override void OnLevelLoaded(LoadMode mode)
        {
            InGame = mode == LoadMode.LoadGame || mode == LoadMode.NewGame || mode == LoadMode.NewGameFromScenario;
        }

        public override void OnLevelUnloading() { InGame = false; }
    }
}
