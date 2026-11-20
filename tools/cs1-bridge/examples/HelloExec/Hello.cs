// Example command code for the CS1 bridge's `exec` method (see tools/PROTOCOL.md, "exec: hot-loaded command code").
using System.Collections;
using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace HelloExec
{
    public static class Hello
    {
        const string Greeting = "hello v1";

        /// <summary>Plain call: returns at once. {"args":{"note":"..."}} is echoed back.</summary>
        public static object Run(Dictionary<string, object> args)
        {
            object note;
            args.TryGetValue("note", out note);
            return Snapshot(note);
        }

        /// <summary>Coroutine variant: the bridge steps it once per frame and replies with the last yielded value.</summary>
        public static IEnumerator RunLater(Dictionary<string, object> args)
        {
            int start = Time.frameCount;
            for (int i = 0; i < 3; i++) yield return null; // wait three frames
            var result = Snapshot(null);
            result["frames_waited"] = Time.frameCount - start;
            yield return result;
        }

        static Dictionary<string, object> Snapshot(object note)
        {
            var result = new Dictionary<string, object> { { "greeting", Greeting }, { "note", note } };
            if (Singleton<SimulationManager>.exists && Singleton<SimulationManager>.instance.m_metaData != null)
                result["city"] = Singleton<SimulationManager>.instance.m_metaData.m_CityName;
            if (Singleton<NetManager>.exists)
            {
                var nm = Singleton<NetManager>.instance;
                result["segments"] = nm.m_segmentCount;
                result["nodes"] = nm.m_nodeCount;
            }
            return result;
        }
    }
}
