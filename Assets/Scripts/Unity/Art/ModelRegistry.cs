using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Lifts;
using AlpineSim.Core.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity.Art
{
    /// <summary>Where a visual came from. Tier order is authored, then generated, then primitive.</summary>
    public enum ModelTier { Authored, Generated, Primitive }

    /// <summary>The pieces a lift is assembled from; see docs/ART_CONTRACT.md section 9.</summary>
    public enum LiftPart { Tower, TowerLow, TowerHigh, TerminalDrive, TerminalReturn, Carrier, Barn }

    /// <summary>
    /// Resolves every machine, attachment and lift component to a visual, through the three tiers of
    /// docs/ART_CONTRACT.md section 9: a hand-authored model named by the record's ModelOverride, the
    /// model the asset pipeline generated, or nothing - in which case the caller keeps its primitive
    /// geometry.
    /// <para>
    /// Tier 3 is not a degraded mode, it is the floor: the game is fully playable with
    /// Assets/Art/Generated deleted, which is also how the art pipeline is A/B tested. Nothing in here
    /// throws when a model is missing; Resources.Load simply returns null and the caller builds boxes.
    /// </para>
    /// <para>
    /// Everything is cached per record: twenty groomers of one class share one loaded model and one
    /// material set, and only their GameObjects differ.
    /// </para>
    /// </summary>
    public static class ModelRegistry
    {
        // Which tiling surface set each slot samples. Generated models are box-projected at a fixed
        // metres-per-tile (lib/meshkit._unwrap), not atlassed, so a slot wants a tiling material set
        // and not the trim sheet: painted steel for bodywork, dark rubber for tracks and frames,
        // galvanised steel for lift structures, and one glass set for every pane in the resort.
        public const string MachineRoot = "AlpineSim/Models/Machines/";
        public const string AttachmentRoot = "AlpineSim/Models/Attachments/";
        public const string LiftRoot = "AlpineSim/Models/Lifts/";
        public const string PropRoot = "AlpineSim/Models/Props/";

        private static readonly Dictionary<string, Source> _sources = new Dictionary<string, Source>();
        private static readonly int[] _tierCounts = new int[3];
        private static RenderData _render = new RenderData();
        private static bool _pendingSummary;
        private static int _lastResolveFrame = -1;
        private static bool _reporterSpawned;

        /// <summary>Point the registry at render.json. Idempotent; every view calls it before resolving.</summary>
        public static void Configure(RenderData render)
        {
            if (render != null) _render = render;
        }

        /// <summary>How many visuals resolved to each tier so far.</summary>
        public static int ResolvedCount(ModelTier tier) => _tierCounts[(int)tier];

        // ------------------------------------------------------------------ machines and implements

        /// <summary>The machine's model under <paramref name="parent"/>, or null to keep the primitive chassis.</summary>
        public static GameObject Machine(VehicleDef def, Transform parent)
        {
            if (def == null) return null;
            var source = Resolve("machine:" + def.Id, def.ModelOverride, MachineRoot + def.Id, def.Id,
                                 GeneratedMaterials.SetMachine, GeneratedMaterials.SetRubber);
            return Spawn(source, parent, "model_" + def.Id, def.Visual);
        }

        /// <summary>The implement's model under <paramref name="parent"/>, or null to keep the primitive one.</summary>
        public static GameObject Attachment(AttachmentDef def, Transform parent)
        {
            if (def == null) return null;
            var source = Resolve("attachment:" + def.Id, def.ModelOverride, AttachmentRoot + def.Id, def.Id,
                                 GeneratedMaterials.SetMachine, GeneratedMaterials.SetRubber);
            return Spawn(source, parent, "model_" + def.Id, def.Visual);
        }

        /// <summary>A scenery prop by id (hydrants, gates, fencing, pump houses).</summary>
        public static GameObject Prop(string propId, Transform parent)
        {
            if (string.IsNullOrEmpty(propId)) return null;
            var source = Resolve("prop:" + propId, null, PropRoot + propId, propId,
                                 GeneratedMaterials.SetProp, GeneratedMaterials.SetLift);
            return Spawn(source, parent, "model_" + propId, null);
        }

        // ------------------------------------------------------------------ lifts

        /// <summary>
        /// One piece of a lift. A carrier is painted in the family's colour, everything else is
        /// galvanised steel, so the livery is passed in rather than read from a MeshRecipe: lifts.json
        /// has no mesh recipe, the lift view owns that choice.
        /// </summary>
        public static GameObject LiftComponent(LiftTypeDef type, LiftPart part, Transform parent, Color livery, Color accent)
        {
            var source = ResolveLift(type, part, livery, accent);
            return Spawn(source, parent, "model_" + PartName(part), livery, accent);
        }

        /// <summary>
        /// The merged static proxy of a lift part, for drawing hundreds of copies with
        /// Graphics.DrawMeshInstanced. A gondola line carries more carriers than is worth a GameObject
        /// each, and LOD1 is exactly the merged, non-articulated mesh that job wants.
        /// </summary>
        public static bool TryGetProxy(LiftTypeDef type, LiftPart part, Color livery, Color accent, out Mesh mesh, out Material[] materials)
        {
            mesh = null;
            materials = null;
            var source = ResolveLift(type, part, livery, accent);
            if (source == null || source.Prefab == null) return false;
            if (!source.ProxyProbed)
            {
                source.ProxyProbed = true;
                source.ProxyMesh = FindProxyMesh(source.Prefab);
                source.ProxySlots = source.Slots;
            }
            if (source.ProxyMesh == null) return false;
            mesh = source.ProxyMesh;
            materials = source.ProxySlots;
            return materials != null && materials.Length > 0 && materials[0] != null;
        }

        /// <summary>
        /// Where a named transform sits inside a lift part, in the model's own frame. A carrier's origin
        /// is the bottom of the cabin like every other model's, but it hangs from its grip, so the view
        /// needs to know how far below the rope to put it.
        /// </summary>
        public static Vector3 LiftPartAnchor(LiftTypeDef type, LiftPart part, string[] boneNames, Color livery, Color accent)
        {
            var source = ResolveLift(type, part, livery, accent);
            if (source == null || source.Prefab == null || boneNames == null || boneNames.Length == 0) return Vector3.zero;
            string key = string.Join(",", boneNames);
            if (source.AnchorKey == key) return source.Anchor;
            var anchor = Vector3.zero;
            var all = source.Prefab.GetComponentsInChildren<Transform>(true);
            for (int n = 0; n < boneNames.Length; n++)
            {
                bool found = false;
                for (int i = 0; i < all.Length && !found; i++)
                {
                    if (all[i] == null || all[i].gameObject.name != boneNames[n]) continue;
                    anchor = source.Prefab.transform.InverseTransformPoint(all[i].position);
                    found = true;
                }
                if (found) break;
            }
            source.AnchorKey = key;
            source.Anchor = anchor;
            return anchor;
        }

        /// <summary>Whether a lift part resolved to a model at all, without instantiating it.</summary>
        public static bool HasLiftComponent(LiftTypeDef type, LiftPart part, Color livery, Color accent)
        {
            var source = ResolveLift(type, part, livery, accent);
            return source != null && source.Prefab != null;
        }

        private static Source ResolveLift(LiftTypeDef type, LiftPart part, Color livery, Color accent)
        {
            if (type == null) return null;
            string partName = PartName(part);
            string key = "lift:" + type.Id + ":" + partName;
            if (_sources.TryGetValue(key, out var cached)) return cached;

            // An override is a folder, and a folder may cover only some of the parts; anything it does
            // not carry falls through to the generated folder, then to the primitive tier.
            string authored = string.IsNullOrEmpty(type.ModelOverride) ? null : type.ModelOverride.TrimEnd('/') + "/" + partName;
            string generated = LiftRoot + type.Id + "/" + partName;
            bool heightClass = part == LiftPart.TowerLow || part == LiftPart.TowerHigh;
            var source = Resolve(key, authored, generated, type.Id + "_" + partName,
                                 part == LiftPart.Carrier ? GeneratedMaterials.SetMachine : GeneratedMaterials.SetLift,
                                 GeneratedMaterials.SetLift, livery, accent, heightClass);

            // The nominal tower is the one to fall back on when a height class was not generated.
            if (source.Prefab == null && heightClass)
            {
                var nominal = ResolveLift(type, LiftPart.Tower, livery, accent);
                _sources[key] = nominal;
                return nominal;
            }
            return source;
        }

        private static string PartName(LiftPart part)
        {
            switch (part)
            {
                case LiftPart.Tower: return "tower";
                case LiftPart.TowerLow: return "tower_low";
                case LiftPart.TowerHigh: return "tower_high";
                case LiftPart.TerminalDrive: return "terminal_drive";
                case LiftPart.TerminalReturn: return "terminal_return";
                case LiftPart.Barn: return "barn";
                default: return "carrier";
            }
        }

        // ------------------------------------------------------------------ resolution

        private sealed class Source
        {
            public GameObject Prefab;
            public ModelTier Tier;
            public string ModelName;
            public string BodySet;
            public string MetalSet;
            public Color Livery;
            public Color Accent;
            public Material[] Slots;
            public Mesh ProxyMesh;
            public Material[] ProxySlots;
            public bool ProxyProbed;
            public string AnchorKey;
            public Vector3 Anchor;
        }

        private static Source Resolve(string cacheKey, string authoredPath, string generatedPath, string modelName,
                                      string bodySet, string metalSet)
        {
            return Resolve(cacheKey, authoredPath, generatedPath, modelName, bodySet, metalSet,
                           LiveryTint.DefaultLivery, LiveryTint.DefaultAccent, false);
        }

        private static Source Resolve(string cacheKey, string authoredPath, string generatedPath, string modelName,
                                      string bodySet, string metalSet, Color livery, Color accent, bool quiet)
        {
            if (_sources.TryGetValue(cacheKey, out var cached)) return cached;

            var source = new Source
            {
                Tier = ModelTier.Primitive,
                ModelName = modelName,
                BodySet = bodySet,
                MetalSet = metalSet,
                Livery = livery,
                Accent = accent,
            };
            string usedPath = null;

            if (!string.IsNullOrEmpty(authoredPath))
            {
                source.Prefab = Resources.Load<GameObject>(authoredPath);
                if (source.Prefab != null) { source.Tier = ModelTier.Authored; usedPath = authoredPath; }
            }
            if (source.Prefab == null && _render.UseGeneratedModels && !string.IsNullOrEmpty(generatedPath))
            {
                source.Prefab = Resources.Load<GameObject>(generatedPath);
                if (source.Prefab != null) { source.Tier = ModelTier.Generated; usedPath = generatedPath; }
            }
            if (source.Prefab != null) source.Slots = LiveryTint.SlotsFor(bodySet, metalSet, livery, accent);

            _sources[cacheKey] = source;
            if (!quiet || source.Prefab != null) Record(source, cacheKey, usedPath);
            return source;
        }

        private static GameObject Spawn(Source source, Transform parent, string name, MeshRecipe visual)
        {
            var livery = LiveryTint.Parse(visual != null ? visual.ColorHex : null, LiveryTint.DefaultLivery);
            var accent = LiveryTint.Parse(visual != null ? visual.AccentHex : null, LiveryTint.DefaultAccent);
            return Spawn(source, parent, name, livery, accent);
        }

        private static GameObject Spawn(Source source, Transform parent, string name, Color livery, Color accent)
        {
            if (source == null || source.Prefab == null) return null;
            var go = UnityEngine.Object.Instantiate(source.Prefab, parent);
            go.name = name;
            var t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            BuildColliders(go, source.ModelName);
            BuildLodGroup(go);
            var tint = go.AddComponent<LiveryTint>();
            if (tint != null) tint.Apply(source.BodySet, source.MetalSet, livery, accent);
            var binder = go.AddComponent<ArticulationBinder>();
            if (binder != null) binder.Index(go, source.ModelName);
            return go;
        }

        /// <summary>
        /// Turn the collider proxies the generator exported into real colliders. The convex hull comes
        /// out as <c>&lt;id&gt;_col</c>; anything else ending in _col (cab_col, att_col) is a box, and
        /// its bounds are all the box needs. Either way the proxy geometry stops being drawn.
        /// </summary>
        private static void BuildColliders(GameObject root, string modelName)
        {
            string hullName = modelName + "_col";
            var filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                if (mf == null) continue;
                var go = mf.gameObject;
                if (go == null || !go.name.EndsWith("_col", System.StringComparison.Ordinal)) continue;
                var mesh = mf.sharedMesh;
                if (mesh != null)
                {
                    if (go.name == hullName)
                    {
                        var mc = go.AddComponent<MeshCollider>();
                        if (mc != null) { mc.sharedMesh = mesh; mc.convex = true; }
                    }
                    else
                    {
                        var bc = go.AddComponent<BoxCollider>();
                        if (bc != null) { bc.center = mesh.bounds.center; bc.size = mesh.bounds.size; }
                    }
                }
                // Switched off rather than destroyed: Destroy only takes effect at the end of the
                // frame, and the LOD pass right below would still find the proxy renderer.
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.enabled = false;
            }
        }

        /// <summary>
        /// Build the LOD group the import may not have built. Unity's model importer only makes one
        /// when the FBX names its levels the way it expects, so the registry does it from the _LOD0 /
        /// _LOD1 / _LOD2 children and the screen heights in render.json.
        /// </summary>
        private static void BuildLodGroup(GameObject root)
        {
            if (root.GetComponentInChildren<LODGroup>() != null) return;
            var levels = new List<Renderer>[3];
            var t = root.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child == null) continue;
                int level = LodLevelOf(child.gameObject.name);
                if (level < 0) continue;
                if (levels[level] == null) levels[level] = new List<Renderer>();
                var rends = child.GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < rends.Length; r++)
                {
                    if (rends[r] == null || rends[r].gameObject.name.EndsWith("_col", System.StringComparison.Ordinal)) continue;
                    levels[level].Add(rends[r]);
                }
            }
            if (levels[0] == null || levels[0].Count == 0) return;   // a single-mesh model needs no group

            float[] heights = { _render.ModelLod0ScreenHeight, _render.ModelLod1ScreenHeight, _render.ModelLod2ScreenHeight };
            var lods = new List<LOD>(3);
            for (int level = 0; level < levels.Length; level++)
            {
                if (levels[level] == null || levels[level].Count == 0) continue;
                lods.Add(new LOD(heights[level], levels[level].ToArray()));
            }
            var group = root.AddComponent<LODGroup>();
            if (group == null) return;
            group.SetLODs(lods.ToArray());
            group.RecalculateBounds();
        }

        private static int LodLevelOf(string name)
        {
            if (name.EndsWith("_LOD0", System.StringComparison.Ordinal)) return 0;
            if (name.EndsWith("_LOD1", System.StringComparison.Ordinal)) return 1;
            if (name.EndsWith("_LOD2", System.StringComparison.Ordinal)) return 2;
            return -1;
        }

        /// <summary>The merged LOD1 proxy if the model has one, otherwise the model's only mesh.</summary>
        private static Mesh FindProxyMesh(GameObject prefab)
        {
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            Mesh only = null;
            int meshes = 0;
            for (int i = 0; i < filters.Length; i++)
            {
                var mf = filters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                string n = mf.gameObject.name;
                if (n.EndsWith("_col", System.StringComparison.Ordinal)) continue;
                if (n.EndsWith("_LOD1", System.StringComparison.Ordinal)) return mf.sharedMesh;
                meshes++;
                only = mf.sharedMesh;
            }
            return meshes == 1 ? only : null;
        }

        // ------------------------------------------------------------------ logging

        private static void Record(Source source, string cacheKey, string usedPath)
        {
            _tierCounts[(int)source.Tier]++;
            _pendingSummary = true;
            _lastResolveFrame = Time.frameCount;
            if (_render.LogModelResolutionPerAsset)
                Debug.Log("[Art] " + cacheKey + " -> " + source.Tier.ToString().ToLowerInvariant() +
                          (usedPath != null ? " (" + usedPath + ")" : " (procedural geometry)"));
            if (_reporterSpawned) return;
            _reporterSpawned = true;
            var go = new GameObject("ModelRegistryLog") { hideFlags = HideFlags.HideAndDontSave };
            go.AddComponent<ModelRegistryReporter>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        /// <summary>
        /// One summary per tier, once the world has finished asking. Resolution happens in a burst when
        /// a world is built, so the log waits a frame and prints three lines instead of one per machine;
        /// render.json turns on the per-asset lines when a specific model is in question.
        /// </summary>
        internal static void FlushLog()
        {
            if (!_pendingSummary || Time.frameCount == _lastResolveFrame) return;
            _pendingSummary = false;
            int total = _tierCounts[0] + _tierCounts[1] + _tierCounts[2];
            Debug.Log("[Art] tier 1 authored:  " + _tierCounts[(int)ModelTier.Authored] + " of " + total + " visuals (ModelOverride)");
            Debug.Log("[Art] tier 2 generated: " + _tierCounts[(int)ModelTier.Generated] + " of " + total + " visuals (Assets/Art/Generated)");
            Debug.Log("[Art] tier 3 primitive: " + _tierCounts[(int)ModelTier.Primitive] + " of " + total + " visuals (procedural geometry)");
        }

        /// <summary>Forget every loaded model. Used when the art tree is rebuilt underneath a running editor.</summary>
        public static void Clear()
        {
            _sources.Clear();
            for (int i = 0; i < _tierCounts.Length; i++) _tierCounts[i] = 0;
            LiveryTint.Clear();
            GeneratedMaterials.Clear();
        }
    }

    /// <summary>Prints the registry's resolution summary once the burst of loading is over.</summary>
    internal sealed class ModelRegistryReporter : MonoBehaviour
    {
        private void LateUpdate() => ModelRegistry.FlushLog();
    }
}
