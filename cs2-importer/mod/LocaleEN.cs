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
                { s.GetOptionGroupLocaleID(Setting.MapGroup), "New city" },
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

                { s.GetOptionLabelLocaleID(nameof(Setting.PrepareMap)), "Prepare map" },
                { s.GetOptionDescLocaleID(nameof(Setting.PrepareMap)), "For a new city, before the import: removes the map's own roads, tracks and buildings, then replaces its terrain and surroundings with the package's and sets the CS1 sea level and water sources." },
                { s.GetOptionWarningLocaleID(nameof(Setting.PrepareMap)), "Remove every road, track and building of this map and replace its terrain and water with the package's? There is no undo." },

                { s.GetOptionLabelLocaleID(nameof(Setting.ClearArea)), "Clear the area first" },
                { s.GetOptionDescLocaleID(nameof(Setting.ClearArea)), "Bulldozes the roads already in the package's area (plus 50 m) before building. Roads owned by buildings are kept. Imported roads can't cross existing ones." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyZoning)), "Zoning" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyZoning)), "After the roads, paints the CS1 zones onto the new zone blocks." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyTrafficSettings)), "Traffic lights, stops, turn bans, crosswalks" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyTrafficSettings)), "After the roads, applies the CS1 junction settings with CS2's own road upgrades." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyTrafficMod)), "Priority signs (Traffic mod)" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyTrafficMod)), "After the roads, sets the TM:PE priority signs through the Traffic mod. Skipped when the Traffic mod isn't installed." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyBuildings)), "Buildings, trees, districts" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyBuildings)), "After the roads and zones, places the CS1 service buildings, parks and landmarks, the trees and props, and the districts with their policies." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyTransit)), "Transit lines" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyTransit)), "After the buildings, creates the CS1 bus and tram stops and the transit lines with their names, numbers and colours. Metro and train lines need the stations from the buildings step." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyConnections)), "Outside connections" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyConnections)), "Last, turns the ends of CS1 highways and train tracks at the edge of the package's area into CS2 outside connections, so traffic and trains can come and go." },
                { s.GetOptionLabelLocaleID(nameof(Setting.Import)), "Import into this city" },
                { s.GetOptionDescLocaleID(nameof(Setting.Import)), "Builds the package's roads in the loaded city, then runs the steps turned on above, in that order. There is no undo: save first." },
                { s.GetOptionWarningLocaleID(nameof(Setting.Import)), "Build the package in this city now? There is no undo." },

                { s.GetOptionLabelLocaleID(nameof(Setting.Status)), Status },
                { s.GetOptionDescLocaleID(nameof(Setting.Status)), "" },
            };
        }

        public void Unload() { }
    }
}
