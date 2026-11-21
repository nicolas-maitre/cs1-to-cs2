// Keeps CS1 from autosaving while the bridge drives the game, so a script-loaded city never overwrites the user's
// Saves\AutoSave.crp. Counterpart of tools/cs2-bridge/AutosaveGuard.cs.
//
// How CS1 autosaves (Assembly-CSharp 1.21, by IL inspection): LoadingManager owns one AutoSaveTimer for the whole game
// session. AutoSaveTimer.Start() (called when a level finishes loading and when the autosave options change) starts
// the AutoSaveRoutine coroutine and sets m_Running; it waits the interval, waits while m_Paused > 0, then calls
// SavePanel.AutoSave("AutoSave") — the only caller of that method. Stop() stops the coroutine and clears m_Running.
//
// The guard keeps the coroutine stopped: every frame, if m_Running is set it calls Stop(). Pausing (m_Paused) is not
// enough: the game's load/save panels call UnPause() when they are hidden even if they never paused, which clamps the
// counter back to 0 (seen in the M2 test: an autosave went through 2 min after a guarded load).
// Releasing the guard calls Start() again, which re-reads the user's settings and restarts the interval from zero, so
// lifting the guard never triggers an immediate autosave. Nothing is written to Settings.autoSave / autoSaveInterval.
using System;
using System.Reflection;
using ColossalFramework;

namespace Cs1Bridge
{
    internal static class AutosaveGuard
    {
        /// <summary>Set in the game's environment by gamectl for every game it launches.</summary>
        public const string LaunchedByGamectlEnv = "CS1TOCS2_GAMECTL";

        static readonly FieldInfo s_RunningField = typeof(LoadingManager.AutoSaveTimer).GetField("m_Running", BindingFlags.NonPublic | BindingFlags.Instance);

        static bool s_Requested;
        static string s_Reason;
        static bool s_WeStopped; // we stopped a running timer and owe it a Start()
        static int s_Stops;

        public static bool Requested { get { return s_Requested; } }
        public static string Reason { get { return s_Requested ? s_Reason : null; } }

        /// <summary>True while the guard is on and the game's autosave coroutine is not running.</summary>
        public static bool Active
        {
            get
            {
                if (!s_Requested) return false;
                var t = Timer();
                return t == null || !Running(t);
            }
        }

        public static void Init()
        {
            if (Environment.GetEnvironmentVariable(LaunchedByGamectlEnv) == "1") Request(true, "launched by gamectl");
        }

        public static void Request(bool block, string reason)
        {
            if (block && !s_Requested) Bridge.Log("autosave guard on (" + reason + ")");
            if (!block && s_Requested) Bridge.Log("autosave guard off (" + reason + ")");
            s_Requested = block;
            s_Reason = reason;
        }

        static LoadingManager.AutoSaveTimer Timer()
        {
            return Singleton<LoadingManager>.exists ? Singleton<LoadingManager>.instance.autoSaveTimer : null;
        }

        static bool Running(LoadingManager.AutoSaveTimer t)
        {
            // If the private field ever disappears, assume it runs so the guard keeps calling Stop() (harmless).
            return s_RunningField == null || (bool)s_RunningField.GetValue(t);
        }

        /// <summary>Main thread, every frame: enforce the requested state on the game's AutoSaveTimer.</summary>
        public static void Apply()
        {
            var t = Timer();
            if (t == null) return;
            if (s_Requested)
            {
                if (!Running(t)) return;
                t.Stop();
                s_WeStopped = true;
                if (++s_Stops <= 20) Bridge.Log("autosave held back (AutoSaveTimer stopped, in memory only; settings untouched)");
            }
            else if (s_WeStopped)
            {
                s_WeStopped = false;
                t.Start(); // restarts the interval; only runs if the user's settings enable autosave in this mode
                Bridge.Log("autosave given back (AutoSaveTimer restarted with the user's settings)");
            }
        }

        /// <summary>Lift the guard and give the timer back right away (mod unload / game quit).</summary>
        public static void Release()
        {
            Request(false, "bridge unloading");
            try { Apply(); } catch (Exception e) { Bridge.Log("could not restart AutoSaveTimer: " + e.Message); }
        }
    }
}
