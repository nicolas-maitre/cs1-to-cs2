// Options page of the importer mod (Options → Mods → CS1 → CS2 importer). Buttons are bool properties whose setter
// runs the action (the game's settings UI calls the setter on click). Scripts call the same setters (ScriptApi).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Colossal.IO.AssetDatabase;
using Cs2Importer;
using Game;
using Game.Modding;
using Game.SceneFlow;
using Game.Settings;
using Game.UI.Widgets;

namespace Cs2ImporterMod
{
    [FileLocation("ModsSettings/Cs2Importer/Cs2Importer")]
    [SettingsUIGroupOrder(PackageGroup, MapGroup, ImportGroup, ResultGroup)]
    [SettingsUIShowGroupName(PackageGroup, MapGroup, ImportGroup, ResultGroup)]
    public class Setting : ModSetting
    {
        public const string SettingsName = "Cs2Importer";
        public const string Section = "Main";
        public const string PackageGroup = "Package";
        public const string MapGroup = "Map";
        public const string ImportGroup = "Import";
        public const string ResultGroup = "Result";

        public Setting(IMod mod) : base(mod) { }

        // ---------------------------------------------------------------- package

        /// <summary>Folder of an import package (the one with cs1conv-plan.json and roadbuilder/), from the web tool.</summary>
        [SettingsUISection(Section, PackageGroup)]
        [SettingsUITextInput]
        public string PackageFolder { get; set; } = "";

        /// <summary>Packages found under %LOCALAPPDATA%\cs1-to-cs2; picking one fills in the folder.</summary>
        [SettingsUISection(Section, PackageGroup)]
        [SettingsUIDropdown(typeof(Setting), nameof(GetPackageItems))]
        public string FoundPackage
        {
            get { return ""; }
            set { if (!string.IsNullOrEmpty(value)) { PackageFolder = value; Check = true; } }
        }

        /// <summary>Windows' file dialog for a package's cs1conv-plan.json or its .zip (the settings UI can't browse).</summary>
        [SettingsUISection(Section, PackageGroup)]
        [SettingsUIButton]
        [SettingsUIButtonGroup("pick")]
        public bool Browse { set { Actions.Browse(this); } }

        /// <summary>Opens %LOCALAPPDATA%\cs1-to-cs2\packages in Explorer (where zips are unpacked; unzip packages there too).</summary>
        [SettingsUISection(Section, PackageGroup)]
        [SettingsUIButton]
        [SettingsUIButtonGroup("pick")]
        public bool OpenPackagesFolder { set { Actions.OpenPackagesFolder(this); } }

        [SettingsUISection(Section, PackageGroup)]
        [SettingsUIButton]
        public bool Check { set { Actions.Check(this); } }

        [SettingsUISection(Section, PackageGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        public bool InstallRoadBuilderConfigs { set { Actions.InstallRoadBuilderConfigs(this); } }

        // ---------------------------------------------------------------- map

        /// <summary>In a new city: base map's nets and buildings removed, the package's terrain, world map and water applied.</summary>
        [SettingsUISection(Section, MapGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(ImportDisabled))]
        public bool PrepareMap { set { Actions.PrepareMap(this); } }

        // ---------------------------------------------------------------- import

        /// <summary>Bulldoze the roads already in the plan's area first (planned roads can't cross existing ones).</summary>
        [SettingsUISection(Section, ImportGroup)]
        public bool ClearArea { get; set; } = true;

        /// <summary>
        /// Hidden and unused: the "exact" height mode it selected crashes CS2 on converted terrain (DECISIONS-importer.md
        /// #19). Flat heights are right on both the converted terrain and a stand-in map.
        /// </summary>
        [SettingsUIHidden]
        public bool TerrainIsConverted { get; set; } = false;

        /// <summary>Import a package with terrain into a city that wasn't prepared with it (otherwise Import refuses).</summary>
        [SettingsUISection(Section, ImportGroup)]
        public bool KeepMapTerrain { get; set; } = false;

        /// <summary>After the roads: paint the plan's zone cells on the new zone blocks.</summary>
        [SettingsUISection(Section, ImportGroup)]
        public bool ApplyZoning { get; set; } = true;

        /// <summary>After the roads: traffic lights, all-way stops, turn bans and crosswalks (vanilla CS2).</summary>
        [SettingsUISection(Section, ImportGroup)]
        public bool ApplyTrafficSettings { get; set; } = true;

        /// <summary>After the roads: priority signs through the Traffic mod, when it is installed.</summary>
        [SettingsUISection(Section, ImportGroup)]
        public bool ApplyTrafficMod { get; set; } = true;

        /// <summary>After the priority signs: TM:PE lane connections through the Traffic mod (DECISIONS-importer.md #52).</summary>
        [SettingsUISection(Section, ImportGroup)]
        public bool ApplyLaneConnections { get; set; } = true;

        /// <summary>After the roads and zones: buildings, trees/props, districts and surfaces (buildings/ worker's steps).</summary>
        [SettingsUISection(Section, ImportGroup)]
        public bool ApplyBuildings { get; set; } = true;

        /// <summary>After the buildings: CS1 bus/tram stops and transit lines (transit/ worker's library).</summary>
        [SettingsUISection(Section, ImportGroup)]
        public bool ApplyTransit { get; set; } = true;

        /// <summary>Last: outside connections where CS1 highways and train tracks leave the crop (terrain/ worker's library).</summary>
        [SettingsUISection(Section, ImportGroup)]
        public bool ApplyConnections { get; set; } = true;

        [SettingsUISection(Section, ImportGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(ImportDisabled))]
        public bool Import { set { Actions.Import(this); } }

        /// <summary>Deletes everything the imports into this city created (Undo.cs), after a confirmation.</summary>
        [SettingsUISection(Section, ImportGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(ImportDisabled))]
        public bool RemoveImported { set { Actions.RemoveImported(this); } }

        /// <summary>Test placement only (hidden): shift the whole plan.</summary>
        [SettingsUIHidden]
        public float OffsetX { get; set; }

        [SettingsUIHidden]
        public float OffsetZ { get; set; }

        // ---------------------------------------------------------------- result

        /// <summary>Status / result text; shown through its locale label, which LocaleEN rewrites.</summary>
        [SettingsUISection(Section, ResultGroup)]
        [SettingsUIMultilineText]
        public string Status => string.Empty;

        /// <summary>Stops Prepare map, Import or Remove imported at the next safe point; enabled only while one runs.</summary>
        [SettingsUISection(Section, ResultGroup)]
        [SettingsUIButton]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(CancelDisabled))]
        public bool Cancel { set { Actions.Cancel(this); } }

        /// <summary>Version and whether Road Builder and Traffic are running (a label, rebuilt with the locale).</summary>
        [SettingsUISection(Section, ResultGroup)]
        [SettingsUIMultilineText]
        public string About => string.Empty;

        /// <summary>The same text, readable by scripts (not shown).</summary>
        [SettingsUIHidden]
        public string StatusText { get; set; } = "";

        [SettingsUIHidden]
        public string LastReportPath { get; set; } = "";

        /// <summary>The last five package folders used, '|'-separated (newest first).</summary>
        [SettingsUIHidden]
        public string RecentPackages { get; set; } = "";

        /// <summary>For scripts: the "unpacked … to …" note of the last zip, and the last Remove imported result.</summary>
        [SettingsUIHidden]
        public string LastUnpacked { get; set; }

        [SettingsUIHidden]
        public string LastUndo { get; set; }

        public bool ImportDisabled() { return ImportSystem.Busy; }
        public bool CancelDisabled() { return !ImportSystem.Busy; }

        public override void SetDefaults()
        {
            PackageFolder = "";
            ClearArea = true;
            TerrainIsConverted = false;
            ApplyZoning = ApplyTrafficSettings = ApplyTrafficMod = ApplyBuildings = ApplyTransit = ApplyConnections = true;
            ApplyLaneConnections = true;
            KeepMapTerrain = false;
            RecentPackages = "";
            OffsetX = OffsetZ = 0;
            StatusText = "";
            LastReportPath = "";
        }

        public DropdownItem<string>[] GetPackageItems()
        {
            var items = new List<DropdownItem<string>> { new DropdownItem<string> { value = "", displayName = T.S("pick.choose") } };
            var recent = Packages.Recent(this);
            foreach (var dir in recent)
                items.Add(new DropdownItem<string> { value = dir, displayName = T.S("pick.recent", Packages.Describe(dir)) });
            foreach (var dir in Packages.Found())
                if (!recent.Any(r => string.Equals(r, dir, StringComparison.OrdinalIgnoreCase)))
                    items.Add(new DropdownItem<string> { value = dir, displayName = Packages.Describe(dir) });
            return items.ToArray();
        }
    }
}
