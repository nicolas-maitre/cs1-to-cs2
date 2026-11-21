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
    [SettingsUIGroupOrder(PackageGroup, ImportGroup, ResultGroup)]
    [SettingsUIShowGroupName(PackageGroup, ImportGroup, ResultGroup)]
    public class Setting : ModSetting
    {
        public const string SettingsName = "Cs2Importer";
        public const string Section = "Main";
        public const string PackageGroup = "Package";
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

        [SettingsUISection(Section, PackageGroup)]
        [SettingsUIButton]
        public bool Check { set { Actions.Check(this); } }

        [SettingsUISection(Section, PackageGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        public bool InstallRoadBuilderConfigs { set { Actions.InstallRoadBuilderConfigs(this); } }

        // ---------------------------------------------------------------- import

        /// <summary>Bulldoze the roads already in the plan's area first (planned roads can't cross existing ones).</summary>
        [SettingsUISection(Section, ImportGroup)]
        public bool ClearArea { get; set; } = true;

        /// <summary>The city's terrain is the converted CS1 terrain: place every node at its exact CS1 height.</summary>
        [SettingsUISection(Section, ImportGroup)]
        public bool TerrainIsConverted { get; set; } = false;

        [SettingsUISection(Section, ImportGroup)]
        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(ImportDisabled))]
        public bool Import { set { Actions.Import(this); } }

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

        /// <summary>The same text, readable by scripts (not shown).</summary>
        [SettingsUIHidden]
        public string StatusText { get; set; } = "";

        [SettingsUIHidden]
        public string LastReportPath { get; set; } = "";

        public bool ImportDisabled() { return ImportSystem.Busy; }

        public override void SetDefaults()
        {
            PackageFolder = "";
            ClearArea = true;
            TerrainIsConverted = false;
            OffsetX = OffsetZ = 0;
            StatusText = "";
            LastReportPath = "";
        }

        public DropdownItem<string>[] GetPackageItems()
        {
            var items = new List<DropdownItem<string>> { new DropdownItem<string> { value = "", displayName = "(choose a package found on this PC)" } };
            foreach (var dir in Actions.FindPackages())
                items.Add(new DropdownItem<string> { value = dir, displayName = Actions.Describe(dir) });
            return items.ToArray();
        }
    }
}
