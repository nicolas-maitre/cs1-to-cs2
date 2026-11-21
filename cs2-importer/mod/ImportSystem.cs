// Steps the import coroutine (Cs2Importer.Entry, op "import") once per frame in ToolUpdate, the same way the bridge's
// exec pump:"tool" does for scripts.
using System;
using System.Collections;
using Colossal.Serialization.Entities;
using Game;

namespace Cs2ImporterMod
{
    /// <summary>
    /// CS2's barrier command buffer systems (Game.SafeCommandBufferSystem) only hand out buffers after their
    /// AllowBarrier&lt;T&gt; system has run since their last update; the import coroutine runs in ToolUpdate, where that
    /// isn't guaranteed. Allowing them here is what those systems do every frame (same fix as the bridge's, #36).
    /// </summary>
    internal static class BarrierAccess
    {
        static Unity.Entities.World s_World;
        static Game.SafeCommandBufferSystem[] s_Barriers;

        public static void AllowAll(Unity.Entities.World world)
        {
            try
            {
                if (s_World != world || s_Barriers == null)
                {
                    var list = new System.Collections.Generic.List<Game.SafeCommandBufferSystem>();
                    foreach (var sys in world.Systems) if (sys is Game.SafeCommandBufferSystem b) list.Add(b);
                    s_Barriers = list.ToArray();
                    s_World = world;
                }
                foreach (var b in s_Barriers) b.AllowUsage();
            }
            catch (Exception e) { Mod.Log.Warn("barrier access failed: " + e.Message); s_Barriers = null; }
        }
    }

    public class ImportSystem : GameSystemBase
    {
        static IEnumerator s_Job;
        static object s_Last;
        static Action<object, Exception> s_Done;

        public static bool Busy { get { return s_Job != null; } }

        /// <summary>Set by the Cancel button; the running job stops at its next safe point (between steps or batches).</summary>
        public static bool CancelRequested { get; private set; }

        public static bool Cancel()
        {
            if (s_Job == null) return false;
            CancelRequested = true;
            return true;
        }

        /// <summary>Main thread only. Fails when an import is already running.</summary>
        public static void Start(IEnumerator job, Action<object, Exception> done)
        {
            if (s_Job != null) throw new InvalidOperationException("an import is already running");
            s_Job = job;
            CancelRequested = false;
            s_Last = null;
            s_Done = done;
        }

        /// <summary>A load or a return to the menu ends any running import (ToolUpdate would stop stepping it).</summary>
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            if (s_Job == null) return;
            var done = s_Done;
            s_Job = null;
            done?.Invoke(null, new InvalidOperationException("the city was unloaded during the import"));
        }

        protected override void OnUpdate()
        {
            if (s_Job == null) return;
            BarrierAccess.AllowAll(World); // steps may use game methods that queue end-of-frame commands (NameSystem, …)
            var job = s_Job;
            var done = s_Done;
            try
            {
                if (job.MoveNext()) { if (job.Current != null) s_Last = job.Current; return; }
                s_Job = null;
                done?.Invoke(s_Last, null);
            }
            catch (Exception e)
            {
                s_Job = null;
                Mod.Log.Error("import failed: " + e);
                done?.Invoke(null, e);
            }
        }
    }
}
