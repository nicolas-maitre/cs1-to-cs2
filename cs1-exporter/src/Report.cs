using System;
using System.Collections.Generic;

namespace Cs1Exporter
{
    /// <summary>report.json: non-fatal problems and notes, one item per line.</summary>
    public sealed class Report
    {
        public sealed class Item
        {
            public string Level;   // info | warning | error
            public string Section; // network | netinfos | imt | terrain | manifest | mods | zip | ...
            public string Message;
        }

        public readonly List<Item> Items = new List<Item>();

        public void Info(string section, string message) { Add("info", section, message); }
        public void Warn(string section, string message) { Add("warning", section, message); }
        public void Error(string section, string message) { Add("error", section, message); }

        public void Add(string level, string section, string message)
        {
            Items.Add(new Item { Level = level, Section = section, Message = message });
            UnityEngine.Debug.Log("[Cs1Exporter] " + level + " " + section + ": " + message);
        }

        public int Count(string level)
        {
            int n = 0;
            foreach (var i in Items) if (i.Level == level) n++;
            return n;
        }

        public void Write(JsonWriter w)
        {
            w.BeginObject();
            w.Name("items").BeginArray();
            foreach (var i in Items)
            {
                w.BeginObject();
                w.Prop("level", i.Level);
                w.Prop("section", i.Section);
                w.Prop("message", i.Message);
                w.EndObject();
            }
            w.EndArray();
            w.EndObject();
        }

        public static string Describe(Exception e)
        {
            var inner = e;
            while (inner is System.Reflection.TargetInvocationException && inner.InnerException != null) inner = inner.InnerException;
            return inner.GetType().Name + ": " + inner.Message;
        }
    }
}
