// The user-facing CS2 importer mod (M2): an Options page that checks an import package and builds its roads into the
// loaded city. It runs the same code as the exec library (src/Core + src/Game, compiled into this assembly).
using System;
using System.Collections;
using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace Cs2ImporterMod
{
    public sealed class Mod : IMod
    {
        public const string Version = "0.4.0";
        public static readonly ILog Log = LogManager.GetLogger("Cs2ImporterMod").SetShowsErrorsInUI(false);

        /// <summary>The loaded mod instance; exec scripts reach the settings through it (see ScriptApi).</summary>
        public static Mod Instance { get; private set; }

        public Setting Settings { get; private set; }
        LocaleEN m_Locale;
        LocaleFR m_LocaleFr;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Instance = this;
            Log.Info("OnLoad " + Version);
            Settings = new Setting(this);
            m_Locale = new LocaleEN(Settings);
            GameManager.instance.localizationManager.AddSource("en-US", m_Locale);
            m_LocaleFr = new LocaleFR(Settings);
            GameManager.instance.localizationManager.AddSource("fr-FR", m_LocaleFr);
            AssetDatabase.global.LoadSettings(Setting.SettingsName, Settings, new Setting(this));
            Settings.RegisterInOptionsUI();
            // buildings/ worker's steps (buildings, objects, districts, surfaces), behind one Options toggle
            Cs2Buildings.Steps.RegisterAll((name, step) => ImportSteps.Register(name, args =>
            {
                if (!Settings.ApplyBuildings) return SkippedStep("label.ApplyBuildings");
                // #170: station platforms aligned on the CS1 track, opt-in with rail like transit.rail
                if (name == "buildings") args["buildings.alignStations"] = Settings.ConnectRail;
                return step(args);
            }));
            // transit after the buildings (stations and depots first)
            ImportSteps.Register("transit", args =>
            {
                if (!Settings.ApplyTransit) return SkippedStep("label.ApplyTransit");
                args["transit.rail"] = Settings.ConnectRail; // #161: yards at the crop edge + station and yard links
                return TransitStep.Run(args);
            });
            // the import coroutine creates road definitions, which must happen in ToolUpdate (M0 Spike 1b)
            updateSystem.UpdateAt<ImportSystem>(SystemUpdatePhase.ToolUpdate);
        }

        public void OnDispose()
        {
            Log.Info("OnDispose");
            if (Settings != null) Settings.UnregisterInOptionsUI();
            if (m_Locale != null) GameManager.instance.localizationManager.RemoveSource("en-US", m_Locale);
            if (m_LocaleFr != null) GameManager.instance.localizationManager.RemoveSource("fr-FR", m_LocaleFr);
            Instance = null;
        }

        /// <param name="labelKey">The Strings key of the Options label that turned the step off.</param>
        static IEnumerator SkippedStep(string labelKey)
        {
            yield return new Dictionary<string, object> { { "skipped", T.S("line.off", T.S(labelKey)) } };
        }

        /// <summary>Rewrites the status line on the Options page (it is a locale entry, so the page re-reads it).</summary>
        internal void SetStatus(string text)
        {
            if (Settings != null) Settings.StatusText = text;
            if (m_Locale == null) return;
            ModLocale.Status = text;
            try { GameManager.instance.localizationManager.ReloadActiveLocale(); }
            catch (Exception e) { Log.Warn("locale reload failed: " + e.Message); }
        }
    }
}
