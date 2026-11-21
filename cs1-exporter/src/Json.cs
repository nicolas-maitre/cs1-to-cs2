using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Cs1Exporter
{
    /// <summary>
    /// Tiny streaming JSON writer (CS1 ships no JSON library and targets .NET 3.5).
    /// Tracks comma placement per nesting level; numbers use invariant culture.
    /// </summary>
    public sealed class JsonWriter : IDisposable
    {
        private readonly TextWriter _w;
        private readonly Stack<bool> _first = new Stack<bool>();
        private bool _afterName;

        public JsonWriter(TextWriter w) { _w = w; }

        public void Dispose() { _w.Flush(); }

        private void Sep()
        {
            if (_afterName) { _afterName = false; return; }
            if (_first.Count == 0) return;
            if (_first.Peek()) { _first.Pop(); _first.Push(false); }
            else _w.Write(',');
        }

        public JsonWriter BeginObject() { Sep(); _w.Write('{'); _first.Push(true); return this; }
        public JsonWriter EndObject() { _first.Pop(); _w.Write('}'); return this; }
        public JsonWriter BeginArray() { Sep(); _w.Write('['); _first.Push(true); return this; }
        public JsonWriter EndArray() { _first.Pop(); _w.Write(']'); return this; }

        public JsonWriter Name(string name)
        {
            Sep();
            WriteString(name);
            _w.Write(':');
            _afterName = true;
            return this;
        }

        /// <summary>Appends an already-serialised JSON value.</summary>
        public JsonWriter RawValue(string json) { Sep(); _w.Write(json); return this; }

        public JsonWriter Value(string s) { Sep(); if (s == null) _w.Write("null"); else WriteString(s); return this; }
        public JsonWriter Value(bool b) { Sep(); _w.Write(b ? "true" : "false"); return this; }
        public JsonWriter Value(int i) { Sep(); _w.Write(i.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(uint i) { Sep(); _w.Write(i.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(long i) { Sep(); _w.Write(i.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(float f)
        {
            Sep();
            if (float.IsNaN(f) || float.IsInfinity(f)) _w.Write("null");
            else _w.Write(f.ToString("R", CultureInfo.InvariantCulture));
            return this;
        }
        public JsonWriter Value(double d)
        {
            Sep();
            if (double.IsNaN(d) || double.IsInfinity(d)) _w.Write("null");
            else _w.Write(d.ToString("R", CultureInfo.InvariantCulture));
            return this;
        }
        public JsonWriter Value(Vector3 v) { BeginArray(); Value(v.x); Value(v.y); Value(v.z); return EndArray(); }
        public JsonWriter Value(Color32 c) { BeginArray(); Value((int)c.r); Value((int)c.g); Value((int)c.b); Value((int)c.a); return EndArray(); }

        public JsonWriter Prop(string n, string v) { Name(n); return Value(v); }
        public JsonWriter Prop(string n, bool v) { Name(n); return Value(v); }
        public JsonWriter Prop(string n, int v) { Name(n); return Value(v); }
        public JsonWriter Prop(string n, uint v) { Name(n); return Value(v); }
        public JsonWriter Prop(string n, float v) { Name(n); return Value(v); }
        public JsonWriter Prop(string n, Vector3 v) { Name(n); return Value(v); }
        public JsonWriter Prop(string n, long v) { Name(n); return Value(v); }
        public JsonWriter Prop(string n, double v) { Name(n); return Value(v); }
        public JsonWriter Null() { Sep(); _w.Write("null"); return this; }

        /// <summary>Writes an arbitrary reflected value as best it can.</summary>
        public JsonWriter Any(object o)
        {
            if (o == null) return Value((string)null);
            if (o is string s) return Value(s);
            if (o is bool b) return Value(b);
            if (o is int i) return Value(i);
            if (o is uint u) return Value(u);
            if (o is byte by) return Value((int)by);
            if (o is ushort us) return Value((int)us);
            if (o is short sh) return Value((int)sh);
            if (o is long l) return Value(l);
            if (o is ulong ul) { Sep(); _w.Write(ul.ToString(CultureInfo.InvariantCulture)); return this; }
            if (o is float f) return Value(f);
            if (o is double d) return Value(d);
            if (o is Vector3 v3) return Value(v3);
            if (o is Vector2 v2) { BeginArray(); Value(v2.x); Value(v2.y); return EndArray(); }
            if (o is Color32 c32) return Value(c32);
            if (o is Color c) return Value((Color32)c);
            if (o is Enum) return Value(o.ToString());
            return Value(o.ToString());
        }

        private void WriteString(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            _w.Write(sb.ToString());
        }
    }
}
