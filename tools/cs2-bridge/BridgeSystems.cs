// The two ECS systems of the bridge. Not partial, no SystemAPI: no source generators needed.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Game;
using GameBridge;

namespace Cs2Bridge
{
    /// <summary>Runs in SystemUpdatePhase.MainLoop (menu, loading and in-game): drives BridgeCore once per frame.</summary>
    public class BridgeDispatchSystem : GameSystemBase
    {
        Cs2Methods m_Methods;

        protected override void OnCreate()
        {
            base.OnCreate();
            var core = Mod.Core;
            if (core == null) { Enabled = false; return; }
            m_Methods = new Cs2Methods(this, core);
            m_Methods.Register();
        }

        protected override void OnUpdate()
        {
            var core = Mod.Core;
            if (core == null) return;
            try { AutosaveGuard.Apply(World); }
            catch (Exception e) { Mod.Log.Error("autosave guard failed: " + e); }
            try { core.Tick(); }
            catch (Exception e) { Mod.Log.Error("tick failed: " + e); }
        }
    }

    /// <summary>
    /// Runs in SystemUpdatePhase.ToolUpdate (only while a city or the editor is loaded): invokes exec entry points
    /// queued with phase:"tool", so they can create definitions the same frame's net generation systems pick up.
    /// </summary>
    public class BridgeToolPhaseSystem : GameSystemBase
    {
        sealed class ToolJob
        {
            public Command Cmd;
            public MethodInfo Method;
            public object[] Args;
        }

        /// <summary>An exec coroutine (IEnumerator result) stepped once per frame in ToolUpdate (exec param pump:"tool").</summary>
        sealed class ToolRoutine
        {
            public Command Cmd;
            public IEnumerator Routine;
            public object Last;
            public long LastStepFrame; // BridgeCore.Frame of the last step, for the main-loop watchdog
            public bool Finished;
        }

        static readonly List<ToolJob> s_Queue = new List<ToolJob>();
        static readonly List<ToolRoutine> s_Routines = new List<ToolRoutine>();

        /// <summary>Main thread only (called from BridgeCore.Exec via ExecRunner).</summary>
        internal static void Enqueue(Command cmd, MethodInfo method, object[] args)
        {
            s_Queue.Add(new ToolJob { Cmd = cmd, Method = method, Args = args });
        }

        internal static bool IsQueued(Command cmd)
        {
            foreach (var j in s_Queue) if (j.Cmd == cmd) return true;
            return false;
        }

        internal static void Remove(Command cmd)
        {
            s_Queue.RemoveAll(j => j.Cmd == cmd);
        }

        /// <summary>
        /// Keeps stepping an exec coroutine in ToolUpdate instead of on the main-loop pump, so that every step can create
        /// definitions that the same frame's net generation picks up. A main-loop watchdog fails it when ToolUpdate stops
        /// running (city unloaded) or the caller gives up.
        /// </summary>
        static void StartToolRoutine(BridgeCore core, Command cmd, IEnumerator routine)
        {
            var r = new ToolRoutine { Cmd = cmd, Routine = routine, LastStepFrame = core.Frame };
            s_Routines.Add(r);
            core.AddPump(delegate
            {
                if (r.Finished) return true;
                if (cmd.Abandoned || core.Frame - r.LastStepFrame > 600)
                {
                    s_Routines.Remove(r);
                    r.Finished = true;
                    cmd.Fail(cmd.Abandoned ? "abandoned by the caller" : "the ToolUpdate phase stopped running (city unloaded?)");
                    return true;
                }
                return false;
            });
        }

        void StepRoutines(BridgeCore core)
        {
            if (s_Routines.Count == 0) return;
            foreach (var r in s_Routines.ToArray())
            {
                if (r.Finished) continue;
                r.LastStepFrame = core.Frame;
                try
                {
                    if (r.Routine.MoveNext()) { if (r.Routine.Current != null) r.Last = r.Routine.Current; continue; }
                    r.Finished = true;
                    core.CompleteExec(r.Cmd, r.Last);
                }
                catch (Exception e)
                {
                    var inner = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                    Mod.Log.Info("exec tool coroutine failed: " + inner);
                    r.Finished = true;
                    r.Cmd.Fail(inner.GetType().Name + ": " + inner.Message);
                }
            }
            s_Routines.RemoveAll(r => r.Finished);
        }

        protected override void OnUpdate()
        {
            var core = Mod.Core;
            if (core == null) return;
            StepRoutines(core);
            if (s_Queue.Count == 0) return;
            var jobs = s_Queue.ToArray();
            s_Queue.Clear();
            foreach (var j in jobs)
            {
                if (j.Cmd.Abandoned) { j.Cmd.Fail("abandoned by the caller"); continue; }
                try
                {
                    object result;
                    try { result = j.Method.Invoke(null, j.Args); }
                    catch (TargetInvocationException e) { throw e.InnerException ?? e; }
                    var routine = result as IEnumerator;
                    if (routine != null && P.Str(j.Cmd.Params, "pump", "main") == "tool")
                    {
                        // stepped once per frame in ToolUpdate, from the next frame on
                        StartToolRoutine(core, j.Cmd, routine);
                        continue;
                    }
                    core.CompleteExec(j.Cmd, result); // an IEnumerator continues on the main-loop pump
                }
                catch (Exception e)
                {
                    Mod.Log.Info("exec (tool phase) failed: " + e);
                    j.Cmd.Fail(e.GetType().Name + ": " + e.Message);
                }
            }
        }
    }
}
