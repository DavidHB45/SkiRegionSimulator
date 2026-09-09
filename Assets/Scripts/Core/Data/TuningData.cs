using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Data
{
    /// <summary>One designer-editable number with its provenance. Every anchor in tuning.json is one of these.</summary>
    [Serializable]
    public sealed class TuningEntry
    {
        public string Key;
        public float Value;
        public float Min;
        public float Max;
        public string Unit = "";
        public string Comment = "";
        public float[] Xs; // present for curves
        public float[] Ys;
        public bool IsCurve => Xs != null && Ys != null && Xs.Length > 0;
    }

    public sealed class TuningKeyMissingException : Exception
    {
        public TuningKeyMissingException(string key) : base("tuning.json has no entry '" + key + "'. All balance numbers live in Assets/StreamingAssets/Data/tuning.json; add the key there (with a comment naming its basis).") { }
    }

    /// <summary>
    /// Flattened view of tuning.json. Nested objects become dotted keys ("snow.freshDensityKgM3").
    /// A leaf is any object with a "value" member or with "xs"/"ys" curve members.
    /// Code reads numbers by key and never embeds them. Missing keys throw so silent defaults cannot creep in.
    /// </summary>
    public sealed class TuningData
    {
        private readonly Dictionary<string, TuningEntry> _entries = new Dictionary<string, TuningEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _cache = new Dictionary<string, float>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, TuningEntry> Entries => _entries;
        public int Count => _entries.Count;

        public static TuningData FromJson(JsonNode root)
        {
            var t = new TuningData();
            if (root != null && root.IsObject) t.Flatten("", root);
            return t;
        }

        public static TuningData Empty() => new TuningData();

        private void Flatten(string prefix, JsonNode node)
        {
            foreach (var key in node.Keys)
            {
                if (key == "comment" || key == "$schema") continue;
                var child = node[key];
                string full = prefix.Length == 0 ? key : prefix + "." + key;
                if (child.IsObject && (child.Has("value") || child.Has("xs")))
                {
                    var e = new TuningEntry
                    {
                        Key = full,
                        Value = child.GetFloat("value"),
                        Min = child.GetFloat("min", float.NaN),
                        Max = child.GetFloat("max", float.NaN),
                        Unit = child.GetString("unit", ""),
                        Comment = child.GetString("comment", ""),
                    };
                    if (child.Has("xs"))
                    {
                        e.Xs = JsonMapper.FromJson<float[]>(child["xs"]);
                        e.Ys = JsonMapper.FromJson<float[]>(child["ys"]);
                    }
                    _entries[full] = e;
                }
                else if (child.IsObject)
                {
                    Flatten(full, child);
                }
                else if (child.IsNumber || child.IsBool)
                {
                    _entries[full] = new TuningEntry { Key = full, Value = child.AsFloat, Comment = "(bare value)" };
                }
            }
        }

        public bool Has(string key) => _entries.ContainsKey(key);

        public TuningEntry Entry(string key)
        {
            if (_entries.TryGetValue(key, out var e)) return e;
            throw new TuningKeyMissingException(key);
        }

        /// <summary>Float value of a scalar entry.</summary>
        public float F(string key)
        {
            if (_cache.TryGetValue(key, out float v)) return v;
            v = Entry(key).Value;
            _cache[key] = v;
            return v;
        }

        public int I(string key) => (int)System.Math.Round(F(key));
        public bool B(string key) => F(key) != 0f;

        /// <summary>Float value with a fallback when the key is absent (use sparingly; prefer F).</summary>
        public float FOr(string key, float fallback) => _entries.ContainsKey(key) ? F(key) : fallback;

        /// <summary>Piecewise-linear curve sample.</summary>
        public float Curve(string key, float x)
        {
            var e = Entry(key);
            if (!e.IsCurve) return e.Value;
            return MathUtil.SampleCurve(e.Xs, e.Ys, x);
        }

        /// <summary>Runtime override (tests, difficulty presets, mods). Not persisted.</summary>
        public void Override(string key, float value)
        {
            if (!_entries.TryGetValue(key, out var e)) { e = new TuningEntry { Key = key, Comment = "(override)" }; _entries[key] = e; }
            e.Value = value;
            _cache[key] = value;
        }

        /// <summary>Copy with independent overrides so one test cannot leak tuning into another.</summary>
        public TuningData Clone()
        {
            var t = new TuningData();
            foreach (var kv in _entries)
            {
                var e = kv.Value;
                t._entries[kv.Key] = new TuningEntry { Key = e.Key, Value = e.Value, Min = e.Min, Max = e.Max, Unit = e.Unit, Comment = e.Comment, Xs = e.Xs, Ys = e.Ys };
            }
            return t;
        }
    }
}
