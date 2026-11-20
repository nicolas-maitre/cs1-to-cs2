// Cs1Bridge entry point: starts the loopback bridge (tools/PROTOCOL.md) when CS1 enables the mod and pumps it from a
// MonoBehaviour that survives scene loads, so it runs in the main menu, during loads and in game.
using System;
using GameBridge;
using ICities;
using UnityEngine;

namespace Cs1Bridge
{
    public sealed class Mod : IUserMod
    {
        public string Name => "CS1→CS2 Bridge";
        public string Description => "Dev tool: loopback HTTP bridge for scripted control (127.0.0.1:" + Bridge.Port + ", see tools/PROTOCOL.md).";

        // CS1 creates several instances of IUserMod classes, so all state lives in the static Bridge class.
        public void OnEnabled() { Bridge.Start(); }
        public void OnDisabled() { Bridge.Stop(); }
    }

    public static class Bridge
    {
        public const int Port = 17761;
        public const string Version = "0.1.0";

        static BridgeServer s_Server;
        static BridgeCore s_Core;
        static GameObject s_Object;

        public static BridgeCore Core { get { return s_Core; } }

        public static void Log(string message) { Debug.Log("[Cs1Bridge] " + message); }

        public static void Start()
        {
            if (s_Server != null) return; // already running (OnEnabled can be called more than once)
            try
            {
                var server = new BridgeServer("cs1", Port, Version) { Log = Log };
                var core = new BridgeCore(server) { Log = Log };
                core.CaptureScreenshot = (path, supersize) => Application.CaptureScreenshot(path, supersize);
                Cs1Methods.Register(core);
                server.Start();
                s_Server = server;
                s_Core = core;

                s_Object = new GameObject("Cs1Bridge");
                UnityEngine.Object.DontDestroyOnLoad(s_Object);
                s_Object.AddComponent<BridgeRunner>();
                Log("started, bridge " + Version + ", protocol " + BridgeServer.Protocol);
            }
            catch (Exception e)
            {
                Log("failed to start: " + e);
                Stop();
            }
        }

        public static void Stop()
        {
            if (s_Object != null) { UnityEngine.Object.Destroy(s_Object); s_Object = null; }
            if (s_Server != null) { try { s_Server.Stop(); } catch (Exception e) { Log("stop failed: " + e.Message); } }
            s_Server = null;
            s_Core = null;
        }

        internal static void Tick()
        {
            var core = s_Core;
            if (core == null) return;
            try { core.Tick(); }
            catch (Exception e) { Log("tick failed: " + e); }
        }
    }

    public sealed class BridgeRunner : MonoBehaviour
    {
        void Update() { Bridge.Tick(); }
    }
}
