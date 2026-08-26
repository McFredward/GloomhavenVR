// GloomhavenVR — VoiceSpatialProbe: a dependency-free JSON reader.
//
// WHY THIS EXISTS AND NOT JsonUtility. The probe's config carries a custom rolloff curve as an
// array of [x, y] PAIRS — a jagged array. UnityEngine.JsonUtility cannot deserialise
// float[][] (it cannot deserialise any nested array), and it cannot deserialise a top-level
// array either. The project has no Newtonsoft package (Packages/manifest.json), so the choice
// was a hand-rolled reader or a config shape bent to fit a serialiser. The config shape is the
// contract with the caller, so the reader is what gives.
//
// Scope: read-only, no writer, no reflection, no attributes. Numbers come back as double,
// strings as string, booleans as bool, null as null, objects as Dictionary<string, object>
// and arrays as List<object>. Parse errors throw with a character offset.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GloomhavenVR.VoiceProbe
{
    internal static class MiniJson
    {
        internal static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            int i = 0;
            object v = ParseValue(text, ref i);
            SkipWhite(text, ref i);
            if (i != text.Length)
                throw new FormatException($"MiniJson: trailing content at offset {i}");
            return v;
        }

        // ---- typed accessors --------------------------------------------------------------
        // Every one of these takes the FALLBACK as an argument rather than inventing a default,
        // because a probe that silently substitutes its own idea of a setting is exactly the kind
        // of instrument that reports its own hypothesis.

        internal static Dictionary<string, object> AsObject(object o) => o as Dictionary<string, object>;
        internal static List<object> AsList(object o) => o as List<object>;

        internal static bool Has(Dictionary<string, object> o, string key) =>
            o != null && o.ContainsKey(key) && o[key] != null;

        internal static double Num(Dictionary<string, object> o, string key, double fallback)
        {
            if (!Has(o, key)) return fallback;
            object v = o[key];
            if (v is double d) return d;
            if (v is bool b) return b ? 1.0 : 0.0;
            if (v is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p))
                return p;
            throw new FormatException($"MiniJson: '{key}' is not a number");
        }

        internal static float Flt(Dictionary<string, object> o, string key, float fallback) =>
            (float)Num(o, key, fallback);

        internal static int Int(Dictionary<string, object> o, string key, int fallback) =>
            (int)Math.Round(Num(o, key, fallback));

        internal static bool Bool(Dictionary<string, object> o, string key, bool fallback)
        {
            if (!Has(o, key)) return fallback;
            object v = o[key];
            if (v is bool b) return b;
            if (v is double d) return d != 0.0;
            throw new FormatException($"MiniJson: '{key}' is not a boolean");
        }

        internal static string Str(Dictionary<string, object> o, string key, string fallback)
        {
            if (!Has(o, key)) return fallback;
            return o[key] as string ?? throw new FormatException($"MiniJson: '{key}' is not a string");
        }

        internal static float[] Floats(Dictionary<string, object> o, string key, float[] fallback)
        {
            if (!Has(o, key)) return fallback;
            var list = AsList(o[key]) ?? throw new FormatException($"MiniJson: '{key}' is not an array");
            var outv = new float[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                if (!(list[i] is double d))
                    throw new FormatException($"MiniJson: '{key}[{i}]' is not a number");
                outv[i] = (float)d;
            }
            return outv;
        }

        /// <summary>Reads an array of [x, y] pairs, e.g. a rolloff curve.</summary>
        internal static float[][] Pairs(Dictionary<string, object> o, string key)
        {
            if (!Has(o, key)) return null;
            var list = AsList(o[key]) ?? throw new FormatException($"MiniJson: '{key}' is not an array");
            var outv = new float[list.Count][];
            for (int i = 0; i < list.Count; i++)
            {
                var pair = AsList(list[i]);
                if (pair == null || pair.Count < 2)
                    throw new FormatException($"MiniJson: '{key}[{i}]' is not an [x, y] pair");
                if (!(pair[0] is double x) || !(pair[1] is double y))
                    throw new FormatException($"MiniJson: '{key}[{i}]' has non-numeric members");
                outv[i] = new[] { (float)x, (float)y };
            }
            return outv;
        }

        // ---- parser -----------------------------------------------------------------------

        private static void SkipWhite(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhite(s, ref i);
            if (i >= s.Length) throw new FormatException("MiniJson: unexpected end of input");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException($"MiniJson: expected '{word}' at offset {i}");
            i += word.Length;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++; // '{'
            SkipWhite(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWhite(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new FormatException($"MiniJson: expected a key string at offset {i}");
                string key = ParseString(s, ref i);
                SkipWhite(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException($"MiniJson: expected ':' at offset {i}");
                i++;
                d[key] = ParseValue(s, ref i);
                SkipWhite(s, ref i);
                if (i >= s.Length) throw new FormatException("MiniJson: unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException($"MiniJson: expected ',' or '}}' at offset {i}");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var l = new List<object>();
            i++; // '['
            SkipWhite(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue(s, ref i));
                SkipWhite(s, ref i);
                if (i >= s.Length) throw new FormatException("MiniJson: unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new FormatException($"MiniJson: expected ',' or ']' at offset {i}");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
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
                        if (i + 4 > s.Length) throw new FormatException("MiniJson: truncated \\u escape");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: throw new FormatException($"MiniJson: bad escape '\\{e}' at offset {i}");
                }
            }
            throw new FormatException("MiniJson: unterminated string");
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string tok = s.Substring(start, i - start);
            if (!double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                throw new FormatException($"MiniJson: bad number '{tok}' at offset {start}");
            return d;
        }
    }
}
