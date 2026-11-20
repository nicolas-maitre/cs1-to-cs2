// Cities: Skylines II bridge mod: hosts the shared GameBridge server (tools/bridge-common) and runs its commands on the
// game's main thread. Protocol: tools/PROTOCOL.md.
using System;
using Colossal.Logging;
using Game;
using Game.Modding;
using GameBridge;

namespace Cs2Bridge
{
    public sealed class Mod : IMod
    {
        public const string Version = "0.1.0";
        public const int Port = 17760;

        public static readonly ILog Log = LogManager.GetLogger("Cs2Bridge").SetShowsErrorsInUI(false);

        internal static BridgeServer Server;
        internal static BridgeCore Core;

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("OnLoad " + Version);
            try
            {
                Server = new BridgeServer("cs2", Port, Version);
                Server.Log = m => Log.Info(m);
                Core = new BridgeCore(Server);
                Core.Log = m => Log.Info(m);
                AutosaveGuard.Init();
                Server.Start();
            }
            catch (Exception e)
            {
                // Port taken (second instance?) or similar: the game keeps running without the bridge.
                Log.Error("bridge failed to start: " + e);
                Server = null;
                Core = null;
                return;
            }
            // MainLoop runs in the main menu and during loads as well as in a city.
            updateSystem.UpdateAt<BridgeDispatchSystem>(SystemUpdatePhase.MainLoop);
            // exec phase:"tool" entry points are run from here (before the net/area generation systems).
            updateSystem.UpdateAt<BridgeToolPhaseSystem>(SystemUpdatePhase.ToolUpdate);
        }

        public void OnDispose()
        {
            Log.Info("OnDispose");
            AutosaveGuard.Release(Unity.Entities.World.DefaultGameObjectInjectionWorld);
            try { if (Server != null) Server.Stop(); } catch (Exception e) { Log.Warn("stop failed: " + e.Message); }
            Server = null;
            Core = null;
        }
    }
}
