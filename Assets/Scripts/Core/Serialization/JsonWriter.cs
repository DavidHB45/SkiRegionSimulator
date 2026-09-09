using System.Collections.Generic;
using System.Text;

namespace AlpineSim.Core.Serialization
{
    /// <summary>Deterministic JSON writer. Optional sorted keys for hashing.</summary>
    public static class JsonWriter
    {
        public static string Write(JsonNode node, bool pretty = true, bool sortKeys = false)
        {
            var sb = new StringBuilder(1024);
            WriteNode(sb, node, pretty, sortKeys, 0);
            if (pretty) sb.Append('\n');
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            sb.Append('\n');
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }

        private static void WriteNode(StringBuilder sb, JsonNode n, bool pretty, bool sortKeys, int depth)
        {
            if (n == null) { sb.Append("null"); return; }
            switch (n.Kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(n.AsBool ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(n.RawNumber ?? n.AsDouble.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); break;
                case JsonKind.String: WriteString(sb, n.AsString); break;
                case JsonKind.Array:
                    {
                        var items = n.Items;
                        if (items.Count == 0) { sb.Append("[]"); break; }
                        bool inline = pretty && items.Count <= 8 && AllScalars(items);
                        sb.Append('[');
                        for (int i = 0; i < items.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            if (pretty && !inline) Indent(sb, depth + 1);
                            else if (inline && i > 0) sb.Append(' ');
                            WriteNode(sb, items[i], pretty, sortKeys, depth + 1);
                        }
                        if (pretty && !inline) Indent(sb, depth);
                        sb.Append(']');
                        break;
                    }
                case JsonKind.Object:
                    {
                        var keys = n.Keys;
                        if (keys.Count == 0) { sb.Append("{}"); break; }
                        IList<string> ordered = keys as IList<string>;
                        if (sortKeys)
                        {
                            var copy = new List<string>(keys);
                            copy.Sort(System.StringComparer.Ordinal);
                            ordered = copy;
                        }
                        sb.Append('{');
                        for (int i = 0; i < ordered.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            if (pretty) Indent(sb, depth + 1);
                            WriteString(sb, ordered[i]);
                            sb.Append(pretty ? ": " : ":");
                            WriteNode(sb, n[ordered[i]], pretty, sortKeys, depth + 1);
                        }
                        if (pretty) Indent(sb, depth);
                        sb.Append('}');
                        break;
                    }
            }
        }

        private static bool AllScalars(IReadOnlyList<JsonNode> items)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var k = items[i].Kind;
                if (k == JsonKind.Array || k == JsonKind.Object) return false;
            }
            return true;
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            if (s != null)
            {
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
            }
            sb.Append('"');
        }
    }
}
