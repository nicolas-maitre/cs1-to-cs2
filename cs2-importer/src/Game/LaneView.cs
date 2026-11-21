// op laneview: the Traffic mod's lane connector tool on one plan node, for screenshots of a junction's lane connections
// (DECISIONS-importer.md #60). Best effort, by reflection: the tool system is activated and its selected node set; the
// result lists what was found so a changed mod version shows up in the output. `view` (tool default) turns it off.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GameBridge;
using Unity.Entities;

namespace Cs2Importer
{
    static class LaneView
    {
        internal static object Show(Dictionary<string, object> a)
        {
            var w = new GameWorld();
            var job = Entry.LoadJob(a, System.IO.File.ReadAllText(Entry.PlanFile(a), System.Text.Encoding.UTF8));
            var res = M2Ops.Resolve(w, job);
            long planNode = (long)P.Num(a, "node", -1);
            Entity node;
            if (!res.Node.TryGetValue(planNode, out node)) return new Dictionary<string, object> { { "op", "laneview" }, { "error", "plan node " + planNode + " has no CS2 node" } };
            var type = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => { try { return x.GetTypes(); } catch { return new Type[0]; } })
                .FirstOrDefault(t => t.Name == "LaneConnectorToolSystem");
            if (type == null) return new Dictionary<string, object> { { "op", "laneview" }, { "error", "the Traffic mod's LaneConnectorToolSystem is not loaded" } };
            var tool = w.World.GetExistingSystemManaged(type) as Game.Tools.ToolBaseSystem;
            if (tool == null) return new Dictionary<string, object> { { "op", "laneview" }, { "error", "no LaneConnectorToolSystem instance" } };
            var tools = w.World.GetOrCreateSystemManaged<Game.Tools.ToolSystem>();
            tools.activeTool = tool;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var set = new List<object>();
            foreach (var f in type.GetFields(F).Where(f => f.FieldType == typeof(Entity) && f.Name.IndexOf("selected", StringComparison.OrdinalIgnoreCase) >= 0 && f.Name.IndexOf("node", StringComparison.OrdinalIgnoreCase) >= 0))
            { f.SetValue(tool, node); set.Add(f.Name); }
            // a tool mode that shows the selected node's connections, if the mod has one
            var modes = new List<object>();
            var mp = type.GetProperty("ToolMode", F);
            if (mp != null && mp.PropertyType.IsEnum)
            {
                foreach (var n in Enum.GetNames(mp.PropertyType)) modes.Add(n);
                var pick = Enum.GetNames(mp.PropertyType).FirstOrDefault(n => n.IndexOf("SelectedNode", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0);
                if (pick != null && mp.CanWrite) { try { mp.SetValue(tool, Enum.Parse(mp.PropertyType, pick)); set.Add("ToolMode=" + pick); } catch (Exception e) { set.Add("ToolMode failed: " + e.Message); } }
            }
            return new Dictionary<string, object> { { "op", "laneview" }, { "tool", tools.activeTool?.GetType().FullName }, { "node", node.Index }, { "set", set }, { "tool_modes", modes } };
        }
    }
}
