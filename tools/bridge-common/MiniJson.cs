// Shared by the CS1 bridge (.NET 3.5 / Mono 2) and the CS2 bridge (.NET 4.8 / Mono): no ValueTuple, no Task,
// no string.IsNullOrWhiteSpace, no 3-argument Path.Combine.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameBridge
{
    /// <summary>Already-serialized JSON, written verbatim by <see cref="MiniJson.Write"/>.</summary>
    public sealed class RawJson
    {
        public readonly string Json;
        public RawJson(string json) { Json = json; }
    }

    /// <summary>
    /// Minimal JSON reader/writer. Parse gives Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, double, bool or null.
    /// Write accepts those plus any IDictionary, IEnumerable, numeric type, enum (as its name), RawJson and Unity-free structs via ToString.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) return null;
            var p = new Parser(text);
            p.SkipWs();
            if (p.AtEnd) return null;
            var v = p.Value();
            p.SkipWs();
            if (!p.AtEnd) throw p.Error("trailing characters");
            return v;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v, int depth)
        {
            if (depth > 64) throw new InvalidOperationException("JSON nesting too deep (cycle?)");
            if (v == null) { sb.Append("null"); return; }
            if (v is RawJson) { sb.Append(((RawJson)v).Json); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is char) { WriteString(sb, v.ToString()); return; }
            if (v is Enum) { WriteString(sb, v.ToString()); return; }
            if (v is double || v is float)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) sb.Append("null");
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (v is int || v is long || v is short || v is byte || v is sbyte || v is uint || v is ulong || v is ushort || v is decimal)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            if (v is DateTime) { WriteString(sb, ((DateTime)v).ToString("o", CultureInfo.InvariantCulture)); return; }
            var dict = v as IDictionary;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry e in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(e.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    WriteValue(sb, e.Value, depth + 1);
                }
                sb.Append('}');
                return;
            }
            var list = v as IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool first = true;
                foreach (var item in list)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item, depth + 1);
                }
                sb.Append(']');
                return;
            }
            WriteString(sb, v.ToString());
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Parser
        {
            readonly string s;
            int i;
            public Parser(string text) { s = text; }
            public bool AtEnd { get { return i >= s.Length; } }
            public FormatException Error(string msg) { return new FormatException("JSON: " + msg + " at position " + i); }

            public void SkipWs()
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
            }

            public object Value()
            {
                SkipWs();
                if (AtEnd) throw Error("unexpected end");
                char c = s[i];
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '"') return Str();
                if (c == 't') { Lit("true"); return true; }
                if (c == 'f') { Lit("false"); return false; }
                if (c == 'n') { Lit("null"); return null; }
                return Num();
            }

            void Lit(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error("unexpected token");
                i += word.Length;
            }

            Dictionary<string, object> Obj()
            {
                var d = new Dictionary<string, object>();
                i++;
                SkipWs();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (AtEnd || s[i] != '"') throw Error("expected property name");
                    string k = Str();
                    SkipWs();
                    if (AtEnd || s[i] != ':') throw Error("expected ':'");
                    i++;
                    d[k] = Value();
                    SkipWs();
                    if (AtEnd) throw Error("unterminated object");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Error("expected ',' or '}'");
                }
            }

            List<object> Arr()
            {
                var l = new List<object>();
                i++;
                SkipWs();
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value());
                    SkipWs();
                    if (AtEnd) throw Error("unterminated array");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw Error("expected ',' or ']'");
                }
            }

            string Str()
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (AtEnd) throw Error("unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("bad escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw Error("bad escape");
                    }
                }
            }

            object Num()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (start == i) throw Error("unexpected character '" + s[start] + "'");
                double d;
                if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw Error("bad number");
                return d;
            }
        }
    }

    /// <summary>Typed access to a parsed params object. Missing or null keys give the default.</summary>
    public static class P
    {
        public static bool Has(Dictionary<string, object> p, string key) { return p != null && p.ContainsKey(key) && p[key] != null; }

        public static string Str(Dictionary<string, object> p, string key, string def)
        {
            object v;
            if (p == null || !p.TryGetValue(key, out v) || v == null) return def;
            return v as string ?? Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static double Num(Dictionary<string, object> p, string key, double def)
        {
            object v;
            if (p == null || !p.TryGetValue(key, out v) || v == null) return def;
            if (v is double) return (double)v;
            if (v is bool) return (bool)v ? 1 : 0;
            double d;
            return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : def;
        }

        public static bool Bool(Dictionary<string, object> p, string key, bool def)
        {
            object v;
            if (p == null || !p.TryGetValue(key, out v) || v == null) return def;
            if (v is bool) return (bool)v;
            if (v is double) return (double)v != 0;
            var s = Convert.ToString(v, CultureInfo.InvariantCulture).ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes";
        }

        public static string Required(Dictionary<string, object> p, string key)
        {
            var s = Str(p, key, null);
            if (s == null || s.Trim().Length == 0) throw new ArgumentException("missing required param '" + key + "'");
            return s;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> p, string key)
        {
            object v;
            return p != null && p.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> List(Dictionary<string, object> p, string key)
        {
            object v;
            return p != null && p.TryGetValue(key, out v) ? v as List<object> : null;
        }
    }
}
