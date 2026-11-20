// Example exec entry points for the CS2 bridge. Each exec call loads a fresh copy of this DLL, so rebuilding with a
// different Build value and calling exec again (without restarting the game) shows the new value: hot reload.
using System.Collections;
using System.Collections.Generic;
using Game.City;
using Unity.Entities;

namespace HelloExec
{
    public static class Hello
    {
        public const string Build = "A";

        /// <summary>City name and entity counts. Works with phase "main" and "tool".</summary>
        public static object Run(Dictionary<string, object> args)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var em = world.EntityManager;
            var result = new Dictionary<string, object>
            {
                { "build", Build },
                { "args", args },
                { "unity_frame", UnityEngine.Time.frameCount },
                { "called_from", Caller() },
                { "city_name", world.GetOrCreateSystemManaged<CityConfigurationSystem>().cityName },
                { "entities", Count(em, em.UniversalQuery) },
                { "net_edges", Count(em, ComponentType.ReadOnly<Game.Net.Edge>()) },
                { "net_nodes", Count(em, ComponentType.ReadOnly<Game.Net.Node>()) },
                { "buildings", Count(em, ComponentType.ReadOnly<Game.Buildings.Building>()) },
            };
            return result;
        }

        /// <summary>The bridge system that invoked us: BridgeDispatchSystem (phase main) or BridgeToolPhaseSystem (phase tool).</summary>
        static string Caller()
        {
            foreach (var f in new System.Diagnostics.StackTrace().GetFrames())
            {
                var t = f.GetMethod() == null ? null : f.GetMethod().DeclaringType;
                if (t != null && t.Name.StartsWith("Bridge") && t.Name.EndsWith("System")) return t.Name;
            }
            return null;
        }

        static int Count(EntityManager em, ComponentType type)
        {
            using (var q = em.CreateEntityQuery(type)) return q.CalculateEntityCount();
        }

        static int Count(EntityManager em, EntityQuery q) { return q.CalculateEntityCount(); }
    }

    public static class HelloCoroutine
    {
        /// <summary>Coroutine: waits {frames}=3 frames, then returns how many Unity frames passed.</summary>
        public static object Run(Dictionary<string, object> args)
        {
            int frames = 3;
            object v;
            if (args != null && args.TryGetValue("frames", out v) && v is double) frames = (int)(double)v;
            return Wait(frames);
        }

        static IEnumerator Wait(int frames)
        {
            int start = UnityEngine.Time.frameCount;
            for (int i = 0; i < frames; i++) yield return null;
            yield return new Dictionary<string, object>
            {
                { "build", Hello.Build },
                { "waited_frames", frames },
                { "unity_frames_elapsed", UnityEngine.Time.frameCount - start },
            };
        }
    }
}
