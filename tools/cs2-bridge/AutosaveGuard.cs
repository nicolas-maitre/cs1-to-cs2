// Keeps CS2 from autosaving while the bridge drives the game. The user's settings keep 10 autosaves at a 1-minute
// interval, so every minute a test spends in a loaded city would delete their oldest autosave.
//
// Only the game's AutoSaveSystem is disabled (ComponentSystemBase.Enabled, in memory). Its OnUpdate is the only place
// that autosaves and prunes old autosaves; GeneralSettings.autoSave is never touched, so nothing is written to the
// user's settings and a game restart always comes back with autosave as configured. The bridge also re-enables the
// system itself when the guard is lifted (autosave.set) and when the mod unloads.
using System;
using Game;
using Unity.Entities;

namespace Cs2Bridge
{
    internal static class AutosaveGuard
    {
        /// <summary>Set in the game's environment by gamectl for every game it launches.</summary>
        public const string LaunchedByGamectlEnv = "CS1TOCS2_GAMECTL";

        static bool s_Requested;
        static bool s_WeDisabled;
        static string s_Reason;

        public static bool Requested { get { return s_Requested; } }
        public static string Reason { get { return s_Requested ? s_Reason : null; } }

        public static void Init()
        {
            if (Environment.GetEnvironmentVariable(LaunchedByGamectlEnv) == "1") Request(true, "launched by gamectl");
        }

        public static void Request(bool block, string reason)
        {
            if (block && !s_Requested) Mod.Log.Info("autosave guard on (" + reason + ")");
            if (!block && s_Requested) Mod.Log.Info("autosave guard off (" + reason + ")");
            s_Requested = block;
            s_Reason = reason;
        }

        /// <summary>Main thread, every frame: enforce the requested state on the game's AutoSaveSystem.</summary>
        public static void Apply(World world)
        {
            var sys = world == null ? null : world.GetExistingSystemManaged<AutoSaveSystem>();
            if (sys == null) return;
            if (s_Requested)
            {
                if (sys.Enabled)
                {
                    sys.Enabled = false;
                    s_WeDisabled = true;
                    Mod.Log.Info("AutoSaveSystem disabled (in memory only; settings untouched)");
                }
            }
            else if (s_WeDisabled)
            {
                sys.Enabled = true;
                s_WeDisabled = false;
                Mod.Log.Info("AutoSaveSystem re-enabled");
            }
        }

        /// <summary>Lift the guard and give the system back right away (mod unload).</summary>
        public static void Release(World world)
        {
            Request(false, "bridge unloading");
            try { Apply(world); } catch (Exception e) { Mod.Log.Warn("could not re-enable AutoSaveSystem: " + e.Message); }
        }

        /// <summary>True while autosave is actually off because of the guard.</summary>
        public static bool Active(World world)
        {
            var sys = world == null ? null : world.GetExistingSystemManaged<AutoSaveSystem>();
            return sys != null && s_WeDisabled && !sys.Enabled;
        }
    }
}
