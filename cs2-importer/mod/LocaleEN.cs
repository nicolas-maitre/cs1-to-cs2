// English texts of the Options page. The status line and the About line are labels that Mod.SetStatus rewrites (then it
// reloads the locale). The web tool's guide test reads the `Get…LocaleID(…), "label"` pairs in this file: keep that form.
// French: LocaleFR.cs, same keys. Status and error messages: Strings.cs.
using System.Collections.Generic;
using Colossal;

namespace Cs2ImporterMod
{
    /// <summary>What both languages share: the live status text and the About line.</summary>
    public abstract class ModLocale : IDictionarySource
    {
        protected readonly Setting m_S;
        public static string Status;

        protected ModLocale(Setting s) { m_S = s; }

        protected abstract bool French { get; }
        protected abstract Dictionary<string, string> Entries(Setting s);

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var s = m_S;
            var d = Entries(s);
            d[s.GetOptionLabelLocaleID(nameof(Setting.Status))] = string.IsNullOrEmpty(Status) ? T.In(French, "status.initial") : Status;
            d[s.GetOptionDescLocaleID(nameof(Setting.Status))] = "";
            d[s.GetOptionLabelLocaleID(nameof(Setting.About))] = T.In(French, "about", Mod.Version,
                T.In(French, Actions.ModLoaded("RoadBuilder") ? "about.running" : "about.missing"), T.In(French, Actions.ModLoaded("Traffic") ? "about.running" : "about.missing"));
            d[s.GetOptionDescLocaleID(nameof(Setting.About))] = "";
            return d;
        }

        public void Unload() { }
    }

    public class LocaleEN : ModLocale
    {
        public LocaleEN(Setting s) : base(s) { }
        protected override bool French { get { return false; } }

        protected override Dictionary<string, string> Entries(Setting s)
        {
            return new Dictionary<string, string>
            {
                { s.GetSettingsLocaleID(), "CS1 → CS2 importer" },
                { s.GetOptionTabLocaleID(Setting.Section), "Import" },
                { s.GetOptionGroupLocaleID(Setting.PackageGroup), "Package" },
                { s.GetOptionGroupLocaleID(Setting.MapGroup), "New city" },
                { s.GetOptionGroupLocaleID(Setting.ImportGroup), "Import into this city" },
                { s.GetOptionGroupLocaleID(Setting.ResultGroup), "Status" },

                { s.GetOptionLabelLocaleID(nameof(Setting.PackageFolder)), "Package folder" },
                { s.GetOptionDescLocaleID(nameof(Setting.PackageFolder)), "The import package from the cs1conv web tool: its folder, its cs1conv-plan.json or the downloaded .zip (unpacked under %LOCALAPPDATA%\\cs1-to-cs2\\packages). Paste a path, with or without quotes, or use Browse…." },
                { s.GetOptionLabelLocaleID(nameof(Setting.FoundPackage)), "Packages on this PC" },
                { s.GetOptionDescLocaleID(nameof(Setting.FoundPackage)), "The packages you used last, then the ones found under %LOCALAPPDATA%\\cs1-to-cs2. Picking one fills in the folder and checks it." },
                { s.GetOptionLabelLocaleID(nameof(Setting.Browse)), "Browse…" },
                { s.GetOptionDescLocaleID(nameof(Setting.Browse)), "Opens Windows' file dialog: choose a package's cs1conv-plan.json or the .zip the web tool downloaded. The game waits while the dialog is open." },
                { s.GetOptionLabelLocaleID(nameof(Setting.OpenPackagesFolder)), "Open packages folder" },
                { s.GetOptionDescLocaleID(nameof(Setting.OpenPackagesFolder)), "Opens %LOCALAPPDATA%\\cs1-to-cs2\\packages in Explorer. Packages unzipped there show up in \"Packages on this PC\"." },
                { s.GetOptionLabelLocaleID(nameof(Setting.Check)), "Check" },
                { s.GetOptionDescLocaleID(nameof(Setting.Check)), "Reads the package and checks that every road type it uses is available in this game, including its Road Builder roads, and whether this city was prepared for it." },
                { s.GetOptionLabelLocaleID(nameof(Setting.InstallRoadBuilderConfigs)), "Install Road Builder configs" },
                { s.GetOptionDescLocaleID(nameof(Setting.InstallRoadBuilderConfigs)), "Copies the package's generated Road Builder roads (*-cs1conv.json) to ModsData\\RoadBuilder. Road Builder reads them only at startup, so restart the game afterwards." },
                { s.GetOptionWarningLocaleID(nameof(Setting.InstallRoadBuilderConfigs)), "Copy the package's Road Builder configs? Existing configs with the same names are replaced; you'll need to restart the game." },

                { s.GetOptionLabelLocaleID(nameof(Setting.PrepareMap)), "Prepare map" },
                { s.GetOptionDescLocaleID(nameof(Setting.PrepareMap)), "For a new city, before the import: removes the map's own roads, tracks and buildings, then replaces its terrain and surroundings with the package's and sets the CS1 sea level and water sources." },
                { s.GetOptionWarningLocaleID(nameof(Setting.PrepareMap)), "Remove every road, track and building of this map and replace its terrain and water with the package's? There is no undo." },

                { s.GetOptionLabelLocaleID(nameof(Setting.ClearArea)), "Clear the area first" },
                { s.GetOptionDescLocaleID(nameof(Setting.ClearArea)), "Bulldozes the roads already in the package's area (plus 50 m) before building. Roads owned by buildings are kept. Imported roads can't cross existing ones." },
                { s.GetOptionLabelLocaleID(nameof(Setting.KeepMapTerrain)), "Keep this map's terrain" },
                { s.GetOptionDescLocaleID(nameof(Setting.KeepMapTerrain)), "Import into a city that wasn't prepared with the package's terrain. Roads then follow this map's ground; only cuts of 4 m or more keep their depth. Off: Import refuses a city whose terrain isn't the package's." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyZoning)), "Zoning" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyZoning)), "After the roads, paints the CS1 zones onto the new zone blocks." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyTrafficSettings)), "Traffic lights, stops, turn bans, crosswalks" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyTrafficSettings)), "After the roads, applies the CS1 junction settings with CS2's own road upgrades." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyTrafficMod)), "Priority signs (Traffic mod)" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyTrafficMod)), "After the roads, sets the TM:PE priority signs through the Traffic mod. Skipped when the Traffic mod isn't installed." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyLaneConnections)), "Lane connections (Traffic mod)" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyLaneConnections)), "After the priority signs, recreates the TM:PE lane connections, lane arrows and U-turn rules through the Traffic mod, exactly at junctions. Where a road just continues into another, CS2 keeps its own lane continuation. Skipped when the Traffic mod isn't installed." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyBuildings)), "Buildings, trees, districts" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyBuildings)), "After the roads and zones, places the CS1 service buildings, parks and landmarks, the trees and props, and the districts with their policies." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyTransit)), "Transit lines" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyTransit)), "After the buildings, creates the CS1 bus and tram stops and the transit lines with their names, numbers and colours. Metro and train lines need the stations from the buildings step." },
                { s.GetOptionLabelLocaleID(nameof(Setting.ApplyConnections)), "Outside connections" },
                { s.GetOptionDescLocaleID(nameof(Setting.ApplyConnections)), "Last, turns the ends of CS1 highways and train tracks at the edge of the package's area into CS2 outside connections, so traffic and trains can come and go." },
                { s.GetOptionLabelLocaleID(nameof(Setting.Import)), "Import into this city" },
                { s.GetOptionDescLocaleID(nameof(Setting.Import)), "Builds the package's roads in the loaded city, then runs the steps turned on above, in that order. \"Remove imported\" can delete what it creates." },
                { s.GetOptionWarningLocaleID(nameof(Setting.Import)), "Build the package in this city now?" },
                { s.GetOptionLabelLocaleID(nameof(Setting.RemoveImported)), "Remove imported" },
                { s.GetOptionDescLocaleID(nameof(Setting.RemoveImported)), "Deletes everything the imports into this city created: roads, buildings, trees and props, districts and areas, transit lines and outside connections (zoning and traffic settings go with the roads). It still works after saving and loading. The base map removed by Prepare map doesn't come back." },
                { s.GetOptionWarningLocaleID(nameof(Setting.RemoveImported)), "Delete everything the imports created in this city?" },

                { s.GetOptionLabelLocaleID(nameof(Setting.Cancel)), "Cancel" },
                { s.GetOptionDescLocaleID(nameof(Setting.Cancel)), "Stops Prepare map, the import or Remove imported after the current step or road batch. What's done so far stays; \"Remove imported\" deletes an import's part." },
            };
        }
    }
}
