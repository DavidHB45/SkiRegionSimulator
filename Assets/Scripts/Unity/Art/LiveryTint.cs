using System.Collections.Generic;
using AlpineSim.Core.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity.Art
{
    /// <summary>
    /// Paints a generated model in its record's colours (MeshRecipe.ColorHex / AccentHex) and hands
    /// the model the three material slots from docs/ART_CONTRACT.md section 5: body, metal, glass.
    /// <para>
    /// Livery is a tint on a shared texture set, never a texture of its own, so the whole fleet of
    /// one class shares one material set and differs only in <c>_LiveryColor</c>. The cache is keyed
    /// by texture set and colour rather than by machine, which means two classes painted the same
    /// resort red also share: twenty machines on the hill are still one material.
    /// </para>
    /// </summary>
    public sealed class LiveryTint : MonoBehaviour
    {
        public static readonly Color DefaultLivery = new Color(0.78f, 0.24f, 0.16f, 1f);
        public static readonly Color DefaultAccent = new Color(0.18f, 0.22f, 0.27f, 1f);

        private static readonly Dictionary<string, Material[]> _sets = new Dictionary<string, Material[]>();

        public Color Livery { get; private set; } = DefaultLivery;
        public Color Accent { get; private set; } = DefaultAccent;

        /// <summary>Body, metal and glass, in contract slot order.</summary>
        public Material[] Slots { get; private set; }

        /// <summary>Paint this model in the colours its record carries (vehicles.json / attachments.json "Visual").</summary>
        public void ApplyRecipe(string bodySet, string metalSet, MeshRecipe visual)
        {
            Apply(bodySet, metalSet,
                  Parse(visual != null ? visual.ColorHex : null, DefaultLivery),
                  Parse(visual != null ? visual.AccentHex : null, DefaultAccent));
        }

        /// <summary>Resolve the slot materials for one livery and assign them to every renderer below this object.</summary>
        public void Apply(string bodySet, string metalSet, Color livery, Color accent)
        {
            Livery = livery;
            Accent = accent;
            Slots = SlotsFor(bodySet, metalSet, Livery, Accent);
            Assign(gameObject, Slots);
        }

        /// <summary>The shared material set for one texture set and livery pair.</summary>
        public static Material[] SlotsFor(string bodySet, string metalSet, Color livery, Color accent)
        {
            string key = bodySet + "|" + metalSet + "|" + ColorUtility.ToHtmlStringRGB(livery) + "|" + ColorUtility.ToHtmlStringRGB(accent);
            if (_sets.TryGetValue(key, out var cached) && cached != null && cached.Length == 3 && cached[0] != null) return cached;

            var body = GeneratedMaterials.Body(bodySet);
            if (body != null)
            {
                // A new instance of the shared base, so the tint does not leak into every other livery.
                body = new Material(body) { name = "art_body_" + ColorUtility.ToHtmlStringRGB(livery) };
                body.SetColor("_LiveryColor", livery);
                body.SetColor("_AccentColor", accent);
            }
            var slots = new[] { body, GeneratedMaterials.Metal(metalSet), GeneratedMaterials.Glass() };
            _sets[key] = slots;
            return slots;
        }

        /// <summary>
        /// Fill every renderer's material array from the slots, by submesh index. A generated model
        /// has at most three submeshes in contract order; anything beyond that reuses the last slot
        /// rather than leaving a renderer with the import's default material.
        /// </summary>
        public static void Assign(GameObject root, Material[] slots)
        {
            if (root == null || slots == null || slots.Length == 0 || slots[0] == null) return;
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                var existing = r.sharedMaterials;
                int count = existing != null && existing.Length > 0 ? existing.Length : 1;
                var assigned = new Material[count];
                for (int s = 0; s < count; s++) assigned[s] = slots[s < slots.Length ? s : slots.Length - 1];
                r.sharedMaterials = assigned;
            }
        }

        public static Color Parse(string hex, Color fallback)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return fallback;
        }

        /// <summary>Drops the shared sets. Used when the material factory is reloaded.</summary>
        public static void Clear() => _sets.Clear();
    }
}
