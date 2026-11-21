// English texts of the Options page. The status line is a label that Mod.SetStatus rewrites, then reloads the locale.
using System.Collections.Generic;
using Colossal;

namespace Cs2ImporterMod
{
    public class LocaleEN : IDictionarySource
    {
        readonly Setting m_S;
        public string Status = "Pick a package, then press Check.";

        public LocaleEN(Setting s) { m_S = s; }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var s = m_S;
            return new Dictionary<string, string>
            {
                { s.GetSettingsLocaleID(), "CS1 → CS2 importer" },
                { s.GetOptionTabLocaleID(Setting.Section), "Import" },
                { s.GetOptionGroupLocaleID(Setting.PackageGroup), "Package" },
                { s.GetOptionGroupLocaleID(Setting.ImportGroup), "Import into this city" },
                { s.GetOptionGroupLocaleID(Setting.ResultGroup), "Status" },

                { s.GetOptionLabelLocaleID(nameof(Setting.PackageFolder)), "Package folder" },
                { s.GetOptionDescLocaleID(nameof(Setting.PackageFolder)), "The import package written by the cs1conv web tool or CLI: the folder that holds cs1conv-plan.json and roadbuilder\\." },
                { s.GetOptionLabelLocaleID(nameof(Setting.FoundPackage)), "Packages on this PC" },
                { s.GetOptionDescLocaleID(nameof(Setting.FoundPackage)), "Packages found under %LOCALAPPDATA%\\cs1-to-cs2. Picking one fills in the folder and checks it." },
                { s.GetOptionLabelLocaleID(nameof(Setting.Check)), "Check" },
                { s.GetOptionDescLocaleID(nameof(Setting.Check)), "Reads the package and checks that every road type it uses is available in this game, including its Road Builder roads." },
                { s.GetOptionLabelLocaleID(nameof(Setting.InstallRoadBuilderConfigs)), "Install Road Builder configs" },
                { s.GetOptionDescLocaleID(nameof(Setting.InstallRoadBuilderConfigs)), "Copies the package's generated Road Builder roads (*-cs1conv.json) to ModsData\\RoadBuilder. Road Builder reads them only at startup, so restart the game afterwards." },
                { s.GetOptionWarningLocaleID(nameof(Setting.InstallRoadBuilderConfigs)), "Copy the package's Road Builder configs? Existing configs with the same names are replaced; you'll need to restart the game." },

                { s.GetOptionLabelLocaleID(nameof(Setting.ClearArea)), "Clear the area first" },
                { s.GetOptionDescLocaleID(nameof(Setting.ClearArea)), "Bulldozes the roads already in the package's area (plus 50 m) before building. Roads owned by buildings are kept. Imported roads can't cross existing ones." },
                { s.GetOptionLabelLocaleID(nameof(Setting.TerrainIsConverted)), "The terrain is the converted CS1 terrain" },
                { s.GetOptionDescLocaleID(nameof(Setting.TerrainIsConverted)), "On when this city's terrain was converted from the same CS1 city: every junction is placed at its exact CS1 height. Off: roads follow this map's own terrain." },
                { s.GetOptionLabelLocaleID(nameof(Setting.Import)), "Import into this city" },
                { s.GetOptionDescLocaleID(nameof(Setting.Import)), "Builds the package's roads in the loaded city. There is no undo: save first." },
                { s.GetOptionWarningLocaleID(nameof(Setting.Import)), "Build the package's roads in this city now? There is no undo." },

                { s.GetOptionLabelLocaleID(nameof(Setting.Status)), Status },
                { s.GetOptionDescLocaleID(nameof(Setting.Status)), "" },
            };
        }

        public void Unload() { }
    }
}
