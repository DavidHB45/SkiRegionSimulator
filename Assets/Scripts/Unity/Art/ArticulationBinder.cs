using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AlpineSim.Unity.Art
{
    /// <summary>A model does not publish a transform gameplay binds to. See docs/ART_CONTRACT.md section 3.</summary>
    public sealed class ArticulationContractException : Exception
    {
        public ArticulationContractException(string message) : base(message) { }
    }

    /// <summary>
    /// Resolves the named articulation transforms of one generated model and hands them to whatever
    /// drives them. Binding is by name, exactly as spelled in docs/ART_CONTRACT.md section 3, because
    /// that is the interface between the generator and the game.
    /// <para>
    /// A missing required transform throws. Silently animating nothing is the failure mode this whole
    /// contract exists to prevent: a blade that never lifts looks like a physics bug for a week before
    /// anyone checks the model, whereas an exception at startup names the model, the transform and the
    /// contract in one line. Duplicate names throw for the same reason - the generator is supposed to
    /// keep them unique, and picking one of two silently would hide that it did not.
    /// </para>
    /// </summary>
    public sealed class ArticulationBinder : MonoBehaviour
    {
        private readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private readonly HashSet<string> _duplicates = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The model this binder indexed, for error messages.</summary>
        public string ModelName { get; private set; } = "";

        public int Count => _bones.Count;

        /// <summary>Index every transform under a model root. Called by ModelRegistry on instantiation.</summary>
        public void Index(GameObject root, string modelName)
        {
            ModelName = modelName ?? (root != null ? root.name : "");
            _bones.Clear();
            _duplicates.Clear();
            if (root == null) return;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t.gameObject == root) continue;
                string n = t.gameObject.name;
                if (_bones.ContainsKey(n))
                {
                    if (_duplicates.Add(n) && LooksLikeContractName(n))
                        Debug.LogError("[Art] Model '" + ModelName + "' has more than one transform named '" + n +
                                       "'. docs/ART_CONTRACT.md section 3 requires names to be unique within a model; the generator that wrote this model needs fixing.");
                    continue;
                }
                _bones[n] = t;
            }
        }

        /// <summary>
        /// Check the contract for this model: every name in <paramref name="required"/> must exist,
        /// every name in <paramref name="optional"/> is bound when present and ignored when not.
        /// </summary>
        public void Bind(IList<string> required, IList<string> optional)
        {
            if (required != null)
            {
                for (int i = 0; i < required.Count; i++)
                {
                    string n = required[i];
                    if (string.IsNullOrEmpty(n)) continue;
                    if (_duplicates.Contains(n)) throw Duplicate(n);
                    if (!_bones.ContainsKey(n)) throw Missing(n);
                }
            }
            if (optional != null)
            {
                for (int i = 0; i < optional.Count; i++)
                {
                    string n = optional[i];
                    if (!string.IsNullOrEmpty(n) && _duplicates.Contains(n)) throw Duplicate(n);
                }
            }
        }

        /// <summary>The transform, or an exception naming the model and the contract.</summary>
        public Transform Get(string boneName)
        {
            if (_bones.TryGetValue(boneName, out var t) && t != null) return t;
            throw Missing(boneName);
        }

        public bool TryGet(string boneName, out Transform bone)
        {
            if (!string.IsNullOrEmpty(boneName) && _bones.TryGetValue(boneName, out bone) && bone != null) return true;
            bone = null;
            return false;
        }

        public bool Has(string boneName) => !string.IsNullOrEmpty(boneName) && _bones.ContainsKey(boneName);

        private ArticulationContractException Missing(string boneName)
        {
            return new ArticulationContractException(
                "Model '" + ModelName + "' publishes no transform named '" + boneName + "'. docs/ART_CONTRACT.md section 3 " +
                "lists it as required for this family; the model needs rebuilding with tools/assetgen. It publishes: " + Published());
        }

        private ArticulationContractException Duplicate(string boneName)
        {
            return new ArticulationContractException(
                "Model '" + ModelName + "' publishes more than one transform named '" + boneName + "'. docs/ART_CONTRACT.md " +
                "section 3 requires unique names; binding one of them would hide a generator bug.");
        }

        private string Published()
        {
            var sb = new StringBuilder(256);
            int n = 0;
            foreach (var kv in _bones)
            {
                if (n >= 24) { sb.Append(", ... (").Append(_bones.Count).Append(" transforms)"); break; }
                if (n > 0) sb.Append(", ");
                sb.Append(kv.Key);
                n++;
            }
            return n == 0 ? "nothing" : sb.ToString();
        }

        /// <summary>Whether a name is one the contract reserves, so a duplicate of it is worth an error.</summary>
        private static bool LooksLikeContractName(string n)
        {
            switch (n)
            {
                case "track_L": case "track_R": case "pivot_center": case "cab": case "bucket": case "turret":
                case "exhaust": case "blade_lift": case "blade_angle_L": case "blade_angle_R": case "blade_tilt":
                case "tiller_arm": case "tiller_rotor": case "finisher": case "blower_impeller": case "blower_chute":
                case "spreader_disc": case "winch_drum": case "winch_boom": case "fork_L": case "fork_R":
                case "bullwheel": case "crossarm": case "cabin_hanger": case "grip_arm": case "door_L": case "door_R":
                case "bar": case "carpet_belt": case "fan_rotor": case "gun_yaw": case "gun_pitch": case "oscillator":
                case "hitch":
                    return true;
            }
            return n.StartsWith("wheel_", StringComparison.Ordinal)
                || n.StartsWith("steer_", StringComparison.Ordinal)
                || n.StartsWith("boom_", StringComparison.Ordinal)
                || n.StartsWith("sheave_", StringComparison.Ordinal)
                || n.StartsWith("mount_", StringComparison.Ordinal)
                || n.StartsWith("seat_", StringComparison.Ordinal)
                || n.StartsWith("light_", StringComparison.Ordinal);
        }
    }
}
