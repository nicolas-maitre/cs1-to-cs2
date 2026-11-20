// The two ECS systems of the bridge. Not partial, no SystemAPI: no source generators needed.
using System;
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

        static readonly List<ToolJob> s_Queue = new List<ToolJob>();

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

        protected override void OnUpdate()
        {
            var core = Mod.Core;
            if (core == null || s_Queue.Count == 0) return;
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
