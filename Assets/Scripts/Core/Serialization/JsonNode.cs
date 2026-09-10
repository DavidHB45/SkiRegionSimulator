using System;
using System.Collections.Generic;
using System.Globalization;

namespace AlpineSim.Core.Serialization
{
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>
    /// Minimal JSON DOM. Core owns its own JSON implementation so the simulation has no
    /// dependency on the engine JSON utility or third-party packages and behaves identically
    /// under Unity and under `dotnet test`.
    /// Object members keep insertion order (deterministic output).
    /// </summary>
    public sealed class JsonNode
    {
        public JsonKind Kind { get; private set; }

        private bool _bool;
        private double _number;
        private string _raw; // original numeric token text (exact integers / round-trip floats)
        private string _string;
        private List<JsonNode> _array;
        private List<string> _keys;
        private Dictionary<string, JsonNode> _members;

        public static JsonNode Null() => new JsonNode { Kind = JsonKind.Null };
        public static JsonNode FromBool(bool b) => new JsonNode { Kind = JsonKind.Bool, _bool = b };
        public static JsonNode FromString(string s) => s == null ? Null() : new JsonNode { Kind = JsonKind.String, _string = s };
        public static JsonNode FromDouble(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return Null();
            return new JsonNode { Kind = JsonKind.Number, _number = d, _raw = d.ToString("R", CultureInfo.InvariantCulture) };
        }
        public static JsonNode FromFloat(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) return Null();
            return new JsonNode { Kind = JsonKind.Number, _number = f, _raw = f.ToString("R", CultureInfo.InvariantCulture) };
        }
        public static JsonNode FromLong(long v) => new JsonNode { Kind = JsonKind.Number, _number = v, _raw = v.ToString(CultureInfo.InvariantCulture) };
        public static JsonNode FromULong(ulong v) => new JsonNode { Kind = JsonKind.Number, _number = v, _raw = v.ToString(CultureInfo.InvariantCulture) };
        public static JsonNode FromInt(int v) => FromLong(v);
        internal static JsonNode FromNumberToken(string raw, double parsed) => new JsonNode { Kind = JsonKind.Number, _number = parsed, _raw = raw };
        public static JsonNode NewArray() => new JsonNode { Kind = JsonKind.Array, _array = new List<JsonNode>() };
        public static JsonNode NewObject() => new JsonNode { Kind = JsonKind.Object, _keys = new List<string>(), _members = new Dictionary<string, JsonNode>(StringComparer.Ordinal) };

        public bool IsNull => Kind == JsonKind.Null;
        public bool IsObject => Kind == JsonKind.Object;
        public bool IsArray => Kind == JsonKind.Array;
        public bool IsNumber => Kind == JsonKind.Number;
        public bool IsString => Kind == JsonKind.String;
        public bool IsBool => Kind == JsonKind.Bool;

        public bool AsBool => Kind == JsonKind.Bool ? _bool : (Kind == JsonKind.Number ? _number != 0 : false);
        public double AsDouble => Kind == JsonKind.Number ? _number : (Kind == JsonKind.Bool ? (_bool ? 1 : 0) : 0);
        public float AsFloat
        {
            get
            {
                if (Kind != JsonKind.Number) return AsBool ? 1f : 0f;
                if (_raw != null && float.TryParse(_raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return f;
                return (float)_number;
            }
        }
        public int AsInt => (int)AsLong;
        public long AsLong
        {
            get
            {
                if (Kind != JsonKind.Number) return AsBool ? 1 : 0;
                if (_raw != null && long.TryParse(_raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)) return l;
                return (long)System.Math.Round(_number);
            }
        }
        public ulong AsULong
        {
            get
            {
                if (Kind != JsonKind.Number) return 0;
                if (_raw != null && ulong.TryParse(_raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong u)) return u;
                return (ulong)System.Math.Max(0, System.Math.Round(_number));
            }
        }
        public string AsString => Kind == JsonKind.String ? _string : (Kind == JsonKind.Number ? _raw : (Kind == JsonKind.Bool ? (_bool ? "true" : "false") : null));
        public string RawNumber => _raw;

        // ---- arrays ----
        public int Count => Kind == JsonKind.Array ? _array.Count : (Kind == JsonKind.Object ? _keys.Count : 0);
        public JsonNode this[int index] => Kind == JsonKind.Array && index >= 0 && index < _array.Count ? _array[index] : Null();
        public IReadOnlyList<JsonNode> Items => Kind == JsonKind.Array ? _array : (IReadOnlyList<JsonNode>)Array.Empty<JsonNode>();
        public JsonNode Add(JsonNode item)
        {
            if (Kind != JsonKind.Array) throw new InvalidOperationException("Not an array");
            _array.Add(item ?? Null());
            return this;
        }

        // ---- objects ----
        public IReadOnlyList<string> Keys => Kind == JsonKind.Object ? _keys : (IReadOnlyList<string>)Array.Empty<string>();
        public JsonNode this[string key]
        {
            get => Kind == JsonKind.Object && _members.TryGetValue(key, out var n) ? n : Null();
            set => Set(key, value);
        }
        public bool Has(string key) => Kind == JsonKind.Object && _members.ContainsKey(key);
        public bool TryGet(string key, out JsonNode node)
        {
            if (Kind == JsonKind.Object && _members.TryGetValue(key, out node)) return true;
            node = null;
            return false;
        }
        public JsonNode Set(string key, JsonNode value)
        {
            if (Kind != JsonKind.Object) throw new InvalidOperationException("Not an object");
            value = value ?? Null();
            if (!_members.ContainsKey(key)) _keys.Add(key);
            _members[key] = value;
            return this;
        }
        public JsonNode Set(string key, string v) => Set(key, FromString(v));
        public JsonNode Set(string key, float v) => Set(key, FromFloat(v));
        public JsonNode Set(string key, double v) => Set(key, FromDouble(v));
        public JsonNode Set(string key, int v) => Set(key, FromInt(v));
        public JsonNode Set(string key, long v) => Set(key, FromLong(v));
        public JsonNode Set(string key, bool v) => Set(key, FromBool(v));
        public bool Remove(string key)
        {
            if (Kind != JsonKind.Object || !_members.Remove(key)) return false;
            _keys.Remove(key);
            return true;
        }

        // ---- convenience getters with defaults ----
        public float GetFloat(string key, float def = 0f) => TryGet(key, out var n) && !n.IsNull ? n.AsFloat : def;
        public int GetInt(string key, int def = 0) => TryGet(key, out var n) && !n.IsNull ? n.AsInt : def;
        public bool GetBool(string key, bool def = false) => TryGet(key, out var n) && !n.IsNull ? n.AsBool : def;
        public string GetString(string key, string def = null) => TryGet(key, out var n) && n.IsString ? n.AsString : def;

        public override string ToString() => JsonWriter.Write(this, false);

        /// <summary>Deep structural clone.</summary>
        public JsonNode Clone()
        {
            switch (Kind)
            {
                case JsonKind.Array:
                    var a = NewArray();
                    foreach (var i in _array) a.Add(i.Clone());
                    return a;
                case JsonKind.Object:
                    var o = NewObject();
                    foreach (var k in _keys) o.Set(k, _members[k].Clone());
                    return o;
                case JsonKind.Number:
                    return new JsonNode { Kind = JsonKind.Number, _number = _number, _raw = _raw };
                case JsonKind.String:
                    return FromString(_string);
                case JsonKind.Bool:
                    return FromBool(_bool);
                default:
                    return Null();
            }
        }
    }
}
