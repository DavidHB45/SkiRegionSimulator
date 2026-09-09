using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace AlpineSim.Core.Serialization
{
    /// <summary>
    /// Marks a public field that must not be serialized (runtime caches, derived data).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class JsonIgnoreAttribute : Attribute { }

    /// <summary>
    /// Types that need a custom JSON representation (e.g. packed binary blobs) implement this.
    /// </summary>
    public interface IJsonConvertible
    {
        JsonNode ToJson();
        void FromJson(JsonNode node);
    }

    /// <summary>
    /// Reflection-based mapper between plain C# object graphs (public instance fields) and JsonNode.
    /// Supported: primitives, string, enums (by name), arrays, List&lt;T&gt;, Dictionary&lt;string,T&gt;,
    /// nested classes/structs with a parameterless constructor, nullable value types, JsonNode
    /// pass-through, and IJsonConvertible. Fields are written in ordinal name order so output is
    /// independent of reflection order and therefore deterministic across runtimes.
    /// Missing members keep their default value (forward-compatible loading).
    /// </summary>
    public static class JsonMapper
    {
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();

        public static T FromJson<T>(JsonNode node) => (T)FromJson(node, typeof(T));
        public static T FromJson<T>(string text) => FromJson<T>(JsonParser.Parse(text));
        public static JsonNode ToJson(object value) => ToJson(value, value?.GetType() ?? typeof(object));
        public static string ToJsonString(object value, bool pretty = true, bool sortKeys = false) => JsonWriter.Write(ToJson(value), pretty, sortKeys);

        /// <summary>Populates an existing object's fields from a node (used for partial overrides such as tuning patches).</summary>
        public static void Populate(object target, JsonNode node)
        {
            if (target == null || node == null || !node.IsObject) return;
            foreach (var f in Fields(target.GetType()))
            {
                if (!node.TryGet(f.Name, out var child)) continue;
                f.SetValue(target, FromJson(child, f.FieldType, f.GetValue(target)));
            }
        }

        public static FieldInfo[] Fields(Type t)
        {
            lock (FieldCache)
            {
                if (FieldCache.TryGetValue(t, out var cached)) return cached;
                var list = new List<FieldInfo>();
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.IsInitOnly || f.IsLiteral) continue;
                    if (f.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
                    if (f.GetCustomAttribute<NonSerializedAttribute>() != null) continue;
                    list.Add(f);
                }
                list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                var arr = list.ToArray();
                FieldCache[t] = arr;
                return arr;
            }
        }

        // ---------------------------------------------------------------- write
        public static JsonNode ToJson(object value, Type declared)
        {
            if (value == null) return JsonNode.Null();
            Type t = value.GetType();
            if (value is JsonNode jn) return jn;
            if (value is IJsonConvertible conv) return conv.ToJson();
            if (t.IsEnum) return JsonNode.FromString(value.ToString());
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Boolean: return JsonNode.FromBool((bool)value);
                case TypeCode.String: return JsonNode.FromString((string)value);
                case TypeCode.Single: return JsonNode.FromFloat((float)value);
                case TypeCode.Double: return JsonNode.FromDouble((double)value);
                case TypeCode.Int32: return JsonNode.FromLong((int)value);
                case TypeCode.Int64: return JsonNode.FromLong((long)value);
                case TypeCode.Int16: return JsonNode.FromLong((short)value);
                case TypeCode.Byte: return JsonNode.FromLong((byte)value);
                case TypeCode.SByte: return JsonNode.FromLong((sbyte)value);
                case TypeCode.UInt16: return JsonNode.FromLong((ushort)value);
                case TypeCode.UInt32: return JsonNode.FromLong((uint)value);
                case TypeCode.UInt64: return JsonNode.FromULong((ulong)value);
                case TypeCode.Char: return JsonNode.FromString(value.ToString());
                case TypeCode.Decimal: return JsonNode.FromDouble((double)(decimal)value);
            }
            if (t.IsArray)
            {
                var arr = JsonNode.NewArray();
                var et = t.GetElementType();
                foreach (var item in (Array)value) arr.Add(ToJson(item, et));
                return arr;
            }
            if (value is IDictionary dict)
            {
                var obj = JsonNode.NewObject();
                var keys = new List<string>();
                var map = new Dictionary<string, object>();
                foreach (DictionaryEntry e in dict)
                {
                    string k = Convert.ToString(e.Key, System.Globalization.CultureInfo.InvariantCulture);
                    keys.Add(k);
                    map[k] = e.Value;
                }
                keys.Sort(StringComparer.Ordinal);
                Type vt = t.IsGenericType ? t.GetGenericArguments()[1] : typeof(object);
                foreach (var k in keys) obj.Set(k, ToJson(map[k], vt));
                return obj;
            }
            if (value is IList list)
            {
                var arr = JsonNode.NewArray();
                Type et = t.IsGenericType ? t.GetGenericArguments()[0] : typeof(object);
                foreach (var item in list) arr.Add(ToJson(item, et));
                return arr;
            }
            var o = JsonNode.NewObject();
            foreach (var f in Fields(t))
            {
                o.Set(f.Name, ToJson(f.GetValue(value), f.FieldType));
            }
            return o;
        }

        // ---------------------------------------------------------------- read
        public static object FromJson(JsonNode node, Type t) => FromJson(node, t, null);

        public static object FromJson(JsonNode node, Type t, object existing)
        {
            if (t == typeof(JsonNode)) return node == null ? JsonNode.Null() : node.Clone();
            if (node == null || node.IsNull)
            {
                return t.IsValueType && Nullable.GetUnderlyingType(t) == null ? Activator.CreateInstance(t) : null;
            }
            Type nullable = Nullable.GetUnderlyingType(t);
            if (nullable != null) t = nullable;
            if (t.IsEnum)
            {
                if (node.IsString)
                {
                    try { return Enum.Parse(t, node.AsString, true); }
                    catch (ArgumentException) { throw new JsonMappingException("Unknown enum value '" + node.AsString + "' for " + t.Name); }
                }
                return Enum.ToObject(t, node.AsInt);
            }
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Boolean: return node.AsBool;
                case TypeCode.String: return node.IsString ? node.AsString : (node.IsNumber ? node.AsString : null);
                case TypeCode.Single: return node.AsFloat;
                case TypeCode.Double: return node.AsDouble;
                case TypeCode.Int32: return (int)node.AsLong;
                case TypeCode.Int64: return node.AsLong;
                case TypeCode.Int16: return (short)node.AsLong;
                case TypeCode.Byte: return (byte)node.AsLong;
                case TypeCode.SByte: return (sbyte)node.AsLong;
                case TypeCode.UInt16: return (ushort)node.AsLong;
                case TypeCode.UInt32: return (uint)node.AsLong;
                case TypeCode.UInt64: return node.AsULong;
                case TypeCode.Char: return node.AsString != null && node.AsString.Length > 0 ? node.AsString[0] : '\0';
                case TypeCode.Decimal: return (decimal)node.AsDouble;
            }
            if (typeof(IJsonConvertible).IsAssignableFrom(t))
            {
                var inst = (IJsonConvertible)(existing ?? Activator.CreateInstance(t));
                inst.FromJson(node);
                return inst;
            }
            if (t.IsArray)
            {
                var et = t.GetElementType();
                if (!node.IsArray) return Array.CreateInstance(et, 0);
                var arr = Array.CreateInstance(et, node.Count);
                for (int i = 0; i < node.Count; i++) arr.SetValue(FromJson(node[i], et), i);
                return arr;
            }
            if (t.IsGenericType)
            {
                var gd = t.GetGenericTypeDefinition();
                if (gd == typeof(List<>))
                {
                    var et = t.GetGenericArguments()[0];
                    var list = (IList)Activator.CreateInstance(t);
                    if (node.IsArray) for (int i = 0; i < node.Count; i++) list.Add(FromJson(node[i], et));
                    return list;
                }
                if (gd == typeof(Dictionary<,>))
                {
                    var kt = t.GetGenericArguments()[0];
                    var vt = t.GetGenericArguments()[1];
                    var dict = (IDictionary)Activator.CreateInstance(t);
                    if (node.IsObject)
                    {
                        foreach (var k in node.Keys)
                        {
                            object key = kt == typeof(string) ? k : (kt.IsEnum ? Enum.Parse(kt, k, true) : Convert.ChangeType(k, kt, System.Globalization.CultureInfo.InvariantCulture));
                            dict[key] = FromJson(node[k], vt);
                        }
                    }
                    return dict;
                }
            }
            if (!node.IsObject) throw new JsonMappingException("Expected object for type " + t.Name + " but got " + node.Kind);
            object target = existing ?? Activator.CreateInstance(t);
            foreach (var f in Fields(t))
            {
                if (!node.TryGet(f.Name, out var child)) continue;
                object cur = f.GetValue(target);
                f.SetValue(target, FromJson(child, f.FieldType, f.FieldType.IsValueType ? null : cur));
            }
            return target;
        }
    }

    public sealed class JsonMappingException : Exception
    {
        public JsonMappingException(string message) : base(message) { }
    }
}
