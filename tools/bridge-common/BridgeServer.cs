// Loopback HTTP server for the game bridges (see tools/PROTOCOL.md). Plain TcpListener, because HttpListener is
// missing or unreliable in CS1's Mono 2 runtime. Requests are parsed on their own thread; RPC commands are queued and
// executed on the game's main thread by BridgeCore.Tick(), the HTTP thread waits for the result.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace GameBridge
{
    public sealed class Command
    {
        public string Method;
        public Dictionary<string, object> Params;
        public readonly ManualResetEvent Done = new ManualResetEvent(false);
        public object Result;
        public string Error;
        public DateTime Queued = DateTime.UtcNow;

        // 0 queued, 1 started, 2 dropped (timed out before the main thread picked it up: never runs)
        int m_State;
        /// <summary>Set when the HTTP caller gave up while the command was already running. Pumps should stop early.</summary>
        public volatile bool Abandoned;
        int m_Finished;

        public bool TryStart() { return Interlocked.CompareExchange(ref m_State, 1, 0) == 0; }
        public bool Drop()
        {
            if (Interlocked.CompareExchange(ref m_State, 2, 0) == 0) return true;
            Abandoned = true;
            return false;
        }

        public void Complete(object result)
        {
            if (Interlocked.Exchange(ref m_Finished, 1) != 0) return;
            Result = result;
            Done.Set();
        }

        public void Fail(string error)
        {
            if (Interlocked.Exchange(ref m_Finished, 1) != 0) return;
            Error = error ?? "failed";
            Done.Set();
        }

        public bool Finished { get { return m_Finished != 0; } }
    }

    public sealed class BridgeServer
    {
        public const int Protocol = 1;
        const int MaxHeaderBytes = 64 * 1024;
        const int MaxBodyBytes = 16 * 1024 * 1024;

        public readonly string Game;
        public readonly int Port;
        public readonly string Version;
        public string Token { get; private set; }
        public string InfoFile { get; private set; }
        public Action<string> Log = delegate { };

        readonly List<Command> m_Inbox = new List<Command>(); // not Queue<T>: it lives in System.dll, which CS2's reference set forwards awkwardly
        readonly object m_Gate = new object();
        TcpListener m_Listener;
        Thread m_Thread;
        volatile bool m_Running;

        // Status snapshot refreshed by the main thread (BridgeCore), served by GET /health without touching game state.
        volatile string m_StatusJson = "null";
        long m_StatusTicks;

        public BridgeServer(string game, int port, string version)
        {
            Game = game;
            Port = port;
            Version = version;
        }

        public bool Running { get { return m_Running; } }

        public void SetStatus(string json)
        {
            m_StatusJson = json;
            Interlocked.Exchange(ref m_StatusTicks, DateTime.UtcNow.Ticks);
        }

        public void Start()
        {
            if (m_Running) return;
            Token = NewToken();
            m_Listener = new TcpListener(IPAddress.Loopback, Port);
            m_Listener.Start();
            m_Running = true;
            m_Thread = new Thread(AcceptLoop) { IsBackground = true, Name = Game + "-bridge-http" };
            m_Thread.Start();
            WriteInfoFile();
            Log("listening on http://127.0.0.1:" + Port + "/ (info file " + InfoFile + ")");
        }

        public void Stop()
        {
            m_Running = false;
            try { if (m_Listener != null) m_Listener.Stop(); } catch { }
            lock (m_Gate)
            {
                foreach (var c in m_Inbox) c.Fail("bridge is shutting down");
                m_Inbox.Clear();
            }
            try
            {
                // Only delete the info file if it is still ours (a second game instance may have replaced it).
                if (InfoFile != null && File.Exists(InfoFile) && File.ReadAllText(InfoFile).Contains(Token)) File.Delete(InfoFile);
            }
            catch { }
            Log("stopped");
        }

        public bool TryDequeue(out Command cmd)
        {
            lock (m_Gate)
            {
                if (m_Inbox.Count == 0) { cmd = null; return false; }
                cmd = m_Inbox[0];
                m_Inbox.RemoveAt(0);
                return true;
            }
        }

        public static string InfoDirectory()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(Path.Combine(local, "cs1-to-cs2"), "bridge");
        }

        void WriteInfoFile()
        {
            var dir = InfoDirectory();
            Directory.CreateDirectory(dir);
            InfoFile = Path.Combine(dir, Game + ".json");
            var info = new Dictionary<string, object>
            {
                { "game", Game }, { "port", Port }, { "token", Token }, { "pid", Process.GetCurrentProcess().Id },
                { "bridge_version", Version }, { "protocol", Protocol }, { "started", DateTime.UtcNow },
            };
            var tmp = InfoFile + ".tmp";
            File.WriteAllText(tmp, MiniJson.Write(info));
            if (File.Exists(InfoFile)) File.Delete(InfoFile);
            File.Move(tmp, InfoFile);
        }

        static string NewToken()
        {
            var bytes = new byte[24];
            new RNGCryptoServiceProvider().GetBytes(bytes);
            var sb = new StringBuilder();
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        void AcceptLoop()
        {
            while (m_Running)
            {
                TcpClient client;
                try { client = m_Listener.AcceptTcpClient(); }
                catch { if (!m_Running) return; continue; }
                // One thread per request: an RPC may block for minutes (e.g. waiting for a load), it must not park
                // ThreadPool workers the game uses for its own async work.
                try { new Thread(delegate () { Handle(client); }) { IsBackground = true, Name = Game + "-bridge-req" }.Start(); }
                catch (Exception e) { Log("request thread failed: " + e.Message); try { client.Close(); } catch { } }
            }
        }

        sealed class Request
        {
            public string Method, Path;
            public Dictionary<string, string> Headers = new Dictionary<string, string>();
            public string Body = "";
        }

        static Request ReadRequest(NetworkStream stream)
        {
            var head = new MemoryStream();
            int last4 = 0; // the last four bytes read, packed; "\r\n\r\n" == 0x0D0A0D0A
            while (last4 != 0x0D0A0D0A)
            {
                int b = stream.ReadByte();
                if (b < 0) throw new IOException("connection closed");
                head.WriteByte((byte)b);
                if (head.Length > MaxHeaderBytes) throw new InvalidDataException("headers too large");
                last4 = (last4 << 8) | b;
            }
            var text = Encoding.ASCII.GetString(head.ToArray());
            var lines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length < 2) throw new InvalidDataException("bad request line");
            var rq = new Request { Method = first[0].ToUpperInvariant(), Path = first[1] };
            int q = rq.Path.IndexOf('?');
            if (q >= 0) rq.Path = rq.Path.Substring(0, q);
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c <= 0) continue;
                rq.Headers[lines[i].Substring(0, c).Trim().ToLowerInvariant()] = lines[i].Substring(c + 1).Trim();
            }
            string len;
            if (rq.Headers.TryGetValue("content-length", out len))
            {
                int n = int.Parse(len, CultureInfo.InvariantCulture);
                if (n < 0 || n > MaxBodyBytes) throw new InvalidDataException("body too large");
                var buf = new byte[n];
                int read = 0;
                while (read < n)
                {
                    int r = stream.Read(buf, read, n - read);
                    if (r <= 0) throw new IOException("connection closed while reading body");
                    read += r;
                }
                rq.Body = Encoding.UTF8.GetString(buf);
            }
            return rq;
        }

        static void WriteResponse(NetworkStream stream, int status, Dictionary<string, object> body)
        {
            var json = Encoding.UTF8.GetBytes(MiniJson.Write(body));
            string reason = status == 200 ? "OK" : status == 400 ? "Bad Request" : status == 401 ? "Unauthorized" : status == 403 ? "Forbidden"
                : status == 404 ? "Not Found" : status == 413 ? "Payload Too Large" : status == 415 ? "Unsupported Media Type"
                : status == 504 ? "Gateway Timeout" : "Error";
            var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "
                + json.Length + "\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n");
            stream.Write(head, 0, head.Length);
            stream.Write(json, 0, json.Length);
            stream.Flush();
        }

        void Handle(TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 15000;
                    var stream = client.GetStream();
                    var resp = new Dictionary<string, object>();
                    int status = 200;
                    try
                    {
                        var rq = ReadRequest(stream);
                        // A web page can POST to 127.0.0.1 from the user's browser. Browsers always send Origin / Sec-Fetch-*
                        // on such requests; real clients (gamectl) never do.
                        if (rq.Headers.ContainsKey("origin") || rq.Headers.ContainsKey("sec-fetch-site") || rq.Headers.ContainsKey("sec-fetch-mode"))
                        {
                            status = 403; resp["ok"] = false; resp["error"] = "browser requests are refused";
                        }
                        else if (rq.Method == "GET" && rq.Path == "/health")
                        {
                            resp["ok"] = true; resp["game"] = Game; resp["bridge_version"] = Version; resp["protocol"] = Protocol;
                            resp["pid"] = Process.GetCurrentProcess().Id;
                            long ticks = Interlocked.Read(ref m_StatusTicks);
                            resp["status_age_ms"] = ticks == 0 ? -1 : (long)(DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc)).TotalMilliseconds;
                            resp["status"] = new RawJson(m_StatusJson);
                        }
                        else if (rq.Method == "POST" && rq.Path == "/rpc")
                        {
                            string auth, ct;
                            rq.Headers.TryGetValue("authorization", out auth);
                            rq.Headers.TryGetValue("content-type", out ct);
                            if (auth == null || !ConstantTimeEquals(auth, "Bearer " + Token))
                            {
                                status = 401; resp["ok"] = false; resp["error"] = "missing or wrong token (read it from " + InfoFile + ")";
                            }
                            else if (ct == null || !ct.TrimStart().StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                            {
                                status = 415; resp["ok"] = false; resp["error"] = "Content-Type must be application/json";
                            }
                            else status = Rpc(rq.Body, resp);
                        }
                        else { status = 404; resp["ok"] = false; resp["error"] = "unknown endpoint " + rq.Method + " " + rq.Path; }
                    }
                    catch (Exception e)
                    {
                        status = 400; resp["ok"] = false; resp["error"] = e.GetType().Name + ": " + e.Message;
                    }
                    WriteResponse(stream, status, resp);
                }
            }
            catch (Exception e) { Log("request failed: " + e.Message); }
        }

        int Rpc(string body, Dictionary<string, object> resp)
        {
            var req = MiniJson.Parse(body) as Dictionary<string, object>;
            if (req == null) throw new InvalidDataException("body must be a JSON object {\"method\":..., \"params\":{...}}");
            var cmd = new Command { Method = P.Str(req, "method", ""), Params = P.Obj(req, "params") ?? new Dictionary<string, object>() };
            if (cmd.Method.Length == 0) throw new InvalidDataException("missing method");
            double timeout = P.Num(req, "timeout_s", 60);
            if (double.IsNaN(timeout) || timeout <= 0) timeout = 60;
            if (timeout > 3600) timeout = 3600;
            lock (m_Gate) m_Inbox.Add(cmd);
            if (!cmd.Done.WaitOne(TimeSpan.FromSeconds(timeout), false))
            {
                resp["ok"] = false;
                if (cmd.Drop())
                {
                    resp["executed"] = false;
                    resp["error"] = "timed out after " + timeout + " s waiting for the game's main thread; the command was NOT run";
                }
                else
                {
                    resp["executed"] = "unknown";
                    resp["error"] = "timed out after " + timeout + " s while the command was running; it was told to stop (check state before retrying)";
                }
                Log(cmd.Method + ": " + resp["error"]);
                return 504;
            }
            if (cmd.Error != null) { resp["ok"] = false; resp["error"] = cmd.Error; return 400; }
            resp["ok"] = true;
            resp["result"] = cmd.Result;
            return 200;
        }

        static bool ConstantTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
