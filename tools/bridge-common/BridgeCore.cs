// Main-thread side of the bridges: method registry, per-frame pumps and the built-in methods every game has
// (ping, methods, wait, exec, screenshot). Each game calls Tick() once per frame from its main thread
// (CS1: a MonoBehaviour.Update, CS2: a GameSystemBase that runs in the menu too).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace GameBridge
{
    /// <summary>Returns the result, or <see cref="BridgeCore.Pending"/> when a pump will complete the command later.</summary>
    public delegate object Handler(Command cmd);

    public sealed class BridgeCore
    {
        /// <summary>Handler return value meaning "I'll call cmd.Complete/Fail myself in a later frame".</summary>
        public static readonly object Pending = new object();

        sealed class Entry
        {
            public Handler Handler;
            public string Help;
            public bool AllowWhileLoading;
        }

        public readonly BridgeServer Server;
        readonly Dictionary<string, Entry> m_Methods = new Dictionary<string, Entry>();
        readonly List<Func<bool>> m_Pumps = new List<Func<bool>>();
        readonly List<Func<bool>> m_NewPumps = new List<Func<bool>>();

        /// <summary>Game status for ping and GET /health (game, mode, loading, city_loaded, ...). Main thread only.</summary>
        public Func<Dictionary<string, object>> Status = delegate { return new Dictionary<string, object>(); };
        /// <summary>True while a save is loading: only methods registered with allowWhileLoading run then.</summary>
        public Func<bool> IsLoading = delegate { return false; };
        /// <summary>Game-specific screenshot call, e.g. ScreenCapture.CaptureScreenshot(path, superSize).</summary>
        public Action<string, int> CaptureScreenshot;
        /// <summary>Runs a hot-loaded exec entry point. Games may override (CS2 can defer to the ToolUpdate phase).</summary>
        public Action<Command, MethodInfo, object[]> ExecRunner;
        public Action<string> Log = delegate { };

        public long Frame { get; private set; }
        public int CommandsPerFrame = 8;
        public int StatusEveryFrames = 15;

        public BridgeCore(BridgeServer server)
        {
            Server = server;
            ExecRunner = RunExecNow;
            Register("ping", "Status of the game (same object as GET /health status).", c => Status(), true);
            Register("methods", "List the available methods.", c => ListMethods(), true);
            Register("wait", "Complete after {frames} frames and/or {seconds} seconds of real time.", Wait, true);
            Register("exec", "Hot-load {dll} (fresh copy every call) and run {type}.{method}(args); see tools/PROTOCOL.md.", Exec);
            Register("screenshot", "Write a PNG of the next rendered frame to {path} (absolute, .png); {supersize} 1-4.", Screenshot, true);
        }

        public void Register(string method, string help, Handler handler, bool allowWhileLoading = false)
        {
            m_Methods[method] = new Entry { Handler = handler, Help = help, AllowWhileLoading = allowWhileLoading };
        }

        /// <summary>Adds a per-frame step; it is called every Tick until it returns true or throws.</summary>
        public void AddPump(Func<bool> step) { m_NewPumps.Add(step); }

        public void Tick()
        {
            Frame++;
            if (m_NewPumps.Count > 0) { m_Pumps.AddRange(m_NewPumps); m_NewPumps.Clear(); }
            for (int i = m_Pumps.Count - 1; i >= 0; i--)
            {
                bool done;
                try { done = m_Pumps[i](); }
                catch (Exception e) { Log("pump failed: " + e); done = true; }
                if (done) m_Pumps.RemoveAt(i);
            }

            if (Frame % StatusEveryFrames == 1)
            {
                try { Server.SetStatus(MiniJson.Write(Status())); }
                catch (Exception e) { Server.SetStatus(MiniJson.Write(new Dictionary<string, object> { { "error", e.Message } })); }
            }

            for (int n = 0; n < CommandsPerFrame; n++)
            {
                Command cmd;
                if (!Server.TryDequeue(out cmd)) break;
                if (!cmd.TryStart()) continue; // caller already timed out: never run it
                Dispatch(cmd);
            }
        }

        void Dispatch(Command cmd)
        {
            Entry e;
            if (!m_Methods.TryGetValue(cmd.Method, out e)) { cmd.Fail("unknown method '" + cmd.Method + "' (see 'methods')"); return; }
            bool loading;
            try { loading = IsLoading(); } catch { loading = false; }
            if (loading && !e.AllowWhileLoading) { cmd.Fail("the game is loading; retry when state.loading is false"); return; }
            try
            {
                var result = e.Handler(cmd);
                if (!ReferenceEquals(result, Pending)) cmd.Complete(result);
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                Log(cmd.Method + " failed: " + inner);
                cmd.Fail(inner.GetType().Name + ": " + inner.Message);
            }
        }

        object ListMethods()
        {
            var list = new List<object>();
            var names = new List<string>(m_Methods.Keys);
            names.Sort(StringComparer.Ordinal);
            foreach (var n in names)
                list.Add(new Dictionary<string, object> { { "name", n }, { "help", m_Methods[n].Help }, { "while_loading", m_Methods[n].AllowWhileLoading } });
            return list;
        }

        object Wait(Command cmd)
        {
            long frames = (long)P.Num(cmd.Params, "frames", 0);
            double seconds = P.Num(cmd.Params, "seconds", 0);
            long until = Frame + Math.Max(0, frames);
            DateTime untilTime = DateTime.UtcNow.AddSeconds(Math.Max(0, seconds));
            long start = Frame;
            DateTime startTime = DateTime.UtcNow;
            AddPump(delegate
            {
                if (cmd.Abandoned) return true;
                if (Frame < until || DateTime.UtcNow < untilTime) return false;
                cmd.Complete(new Dictionary<string, object> { { "frames", Frame - start }, { "seconds", (DateTime.UtcNow - startTime).TotalSeconds } });
                return true;
            });
            return Pending;
        }

        // ------------------------------------------------------------------ exec (hot-loaded code)

        object Exec(Command cmd)
        {
            var dll = P.Required(cmd.Params, "dll");
            if (!Path.IsPathRooted(dll)) throw new ArgumentException("dll must be an absolute path");
            var typeName = P.Str(cmd.Params, "type", null);
            var methodName = P.Str(cmd.Params, "method", "Run");
            object args;
            cmd.Params.TryGetValue("args", out args);

            // Load from bytes: the file stays unlocked so it can be rebuilt while the game runs, and every call gets
            // the current build (old versions stay loaded until the game exits, which is fine for dev use).
            var bytes = File.ReadAllBytes(dll);
            var pdb = Path.ChangeExtension(dll, ".pdb");
            Assembly asm = null;
            if (File.Exists(pdb))
            {
                // Old Mono (CS1) only understands .mdb symbols and may reject a portable PDB: fall back to no symbols.
                try { asm = Assembly.Load(bytes, File.ReadAllBytes(pdb)); } catch { asm = null; }
            }
            if (asm == null) asm = Assembly.Load(bytes);
            MethodInfo method = null;
            foreach (var t in asm.GetTypes())
            {
                if (typeName != null && t.FullName != typeName && t.Name != typeName) continue;
                var m = t.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (m == null) continue;
                if (method != null) throw new ArgumentException("several types have a public static " + methodName + "; pass 'type'");
                method = m;
            }
            if (method == null) throw new ArgumentException("no public static " + (typeName ?? "<any type>") + "." + methodName + " in " + asm.FullName);

            var ps = method.GetParameters();
            object[] callArgs;
            if (ps.Length == 0) callArgs = new object[0];
            else if (ps.Length == 1 && ps[0].ParameterType == typeof(string)) callArgs = new object[] { args == null ? null : MiniJson.Write(args) };
            else if (ps.Length == 1 && ps[0].ParameterType == typeof(Dictionary<string, object>)) callArgs = new object[] { args as Dictionary<string, object> ?? new Dictionary<string, object>() };
            else throw new ArgumentException(method.Name + " must take (), (string argsJson) or (Dictionary<string, object> args)");

            Log("exec " + asm.GetName().Name + " " + method.DeclaringType.FullName + "." + method.Name + " (" + bytes.Length + " bytes)");
            ExecRunner(cmd, method, callArgs);
            return Pending;
        }

        /// <summary>Invoke now; an IEnumerator result is stepped once per frame (a coroutine), its last yielded value is the result.</summary>
        public void RunExecNow(Command cmd, MethodInfo method, object[] callArgs)
        {
            object result;
            try { result = method.Invoke(null, callArgs); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
            CompleteExec(cmd, result);
        }

        public void CompleteExec(Command cmd, object result)
        {
            var routine = result as IEnumerator;
            if (routine == null) { cmd.Complete(ExecResult(result)); return; }
            object last = null;
            AddPump(delegate
            {
                if (cmd.Abandoned) { cmd.Fail("abandoned by the caller"); return true; }
                try
                {
                    if (routine.MoveNext()) { if (routine.Current != null) last = routine.Current; return false; }
                    cmd.Complete(ExecResult(last));
                }
                catch (Exception e)
                {
                    Log("exec coroutine failed: " + e);
                    cmd.Fail(e.GetType().Name + ": " + e.Message);
                }
                return true;
            });
        }

        // A string that looks like JSON is passed through as JSON so command code can return structured data easily.
        static object ExecResult(object r)
        {
            var s = r as string;
            if (s != null)
            {
                var t = s.Trim();
                if (t.Length > 0 && (t[0] == '{' || t[0] == '['))
                {
                    try { MiniJson.Parse(t); return new RawJson(t); } catch { }
                }
            }
            return r;
        }

        // ------------------------------------------------------------------ screenshot

        object Screenshot(Command cmd)
        {
            if (CaptureScreenshot == null) throw new NotSupportedException("screenshots are not supported by this bridge");
            var path = P.Required(cmd.Params, "path");
            if (!Path.IsPathRooted(path) || !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("path must be an absolute .png path");
            int supersize = Math.Max(1, Math.Min(4, (int)P.Num(cmd.Params, "supersize", 1)));
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(path)) File.Delete(path);
            CaptureScreenshot(path, supersize);
            long lastSize = -1;
            int frames = 0, stable = 0;
            AddPump(delegate
            {
                if (cmd.Abandoned) return true;
                if (++frames > 900) { cmd.Fail("screenshot not written after 900 frames (is the window minimised?)"); return true; }
                if (!File.Exists(path)) return false;
                long size = new FileInfo(path).Length;
                if (size == 0 || size != lastSize) { lastSize = size; stable = 0; return false; }
                if (++stable < 2) return false;
                cmd.Complete(new Dictionary<string, object> { { "path", path }, { "bytes", size }, { "frames", frames } });
                return true;
            });
            return Pending;
        }
    }
}
