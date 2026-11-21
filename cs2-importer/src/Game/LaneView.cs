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
            return Select(w, tool, type, node);
        }

        /// <summary>
        /// What a click on the junction does in the mod 0.2.x (LaneConnectorToolSystem.Apply, read from its IL): state
        /// SelectingSourceConnector, control points cleared, SelectIntersectionNode(deps, node), which sets _selectedNode, the
        /// UI's selected intersection and schedules the job that builds the connector helpers. The tool must be running
        /// first (a few frames after it becomes the active tool). Run with phase/pump "tool".
        /// </summary>
        static System.Collections.IEnumerator Select(GameWorld w, Game.Tools.ToolBaseSystem tool, Type type, Entity node)
        {
            var tools = w.World.GetOrCreateSystemManaged<Game.Tools.ToolSystem>();
            tools.activeTool = tool;
            for (int i = 0; i < 5; i++) yield return null;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var steps = new List<object>();
            string error = null;
            try
            {
                var stateField = type.GetField("_state", F);
                if (stateField != null) { stateField.SetValue(tool, Enum.ToObject(stateField.FieldType, 1)); steps.Add("_state=SelectingSourceConnector"); }
                var cps = type.GetField("_controlPoints", F)?.GetValue(tool);
                if (cps != null) { cps.GetType().GetMethod("Clear")?.Invoke(cps, null); steps.Add("_controlPoints cleared"); }
                var sel = type.GetMethod("SelectIntersectionNode", F);
                if (sel == null) error = "SelectIntersectionNode not found";
                else
                {
                    var h = (Unity.Jobs.JobHandle)sel.Invoke(tool, new object[] { default(Unity.Jobs.JobHandle), node });
                    h.Complete();
                    steps.Add("SelectIntersectionNode");
                }
            }
            catch (Exception e) { error = (e.InnerException ?? e).GetType().Name + ": " + (e.InnerException ?? e).Message; }
            for (int i = 0; i < 20; i++) yield return null;
            object selected = null, state = null;
            try { selected = ((Entity)type.GetProperty("SelectedNode", F).GetValue(tool)).Index; state = type.GetProperty("ToolState", F)?.GetValue(tool)?.ToString(); } catch { }
            yield return new Dictionary<string, object> { { "op", "laneview" }, { "tool", tools.activeTool?.GetType().FullName }, { "node", node.Index }, { "steps", steps }, { "error", error }, { "selected_node", selected }, { "tool_state", state } };
        }
    }
}
