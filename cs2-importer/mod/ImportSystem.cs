// Steps the import coroutine (Cs2Importer.Entry, op "import") once per frame in ToolUpdate, the same way the bridge's
// exec pump:"tool" does for scripts.
using System;
using System.Collections;
using Colossal.Serialization.Entities;
using Game;

namespace Cs2ImporterMod
{
    public class ImportSystem : GameSystemBase
    {
        static IEnumerator s_Job;
        static object s_Last;
        static Action<object, Exception> s_Done;

        public static bool Busy { get { return s_Job != null; } }

        /// <summary>Main thread only. Fails when an import is already running.</summary>
        public static void Start(IEnumerator job, Action<object, Exception> done)
        {
            if (s_Job != null) throw new InvalidOperationException("an import is already running");
            s_Job = job;
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
