using System.Collections.Generic;
using UnityEngine;

namespace AlpineSim.Unity.Art
{
    /// <summary>
    /// The material factory behind the generated models: one shared, untinted material per texture
    /// set, built from the maps the asset pipeline wrote under Resources/AlpineSim/Textures.
    /// <para>
    /// Nothing here fails when a map is missing. A machine has to render on a checkout that has
    /// never run <c>make assets</c>, so every texture is optional and the shader is told which maps
    /// it actually got; with none of them a model is flat painted bodywork, which is exactly what
    /// the primitive tier looks like.
    /// </para>
    /// </summary>
    public static class GeneratedMaterials
    {
        public const string TextureRoot = "AlpineSim/Textures/";
        public const string ShaderName = "AlpineSim/MachinePBR";

        /// <summary>Texture set ids from tools/assetgen/textures/pbr.py and trimsheets.py.</summary>
        public const string SetMachine = "machine";
        public const string SetLift = "lift";
        public const string SetProp = "prop";
        public const string SetGlass = "glass";
        public const string SetRubber = "rubber";
        public const string SetTrim = "trim_industrial";

        private static readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private static Shader _shader;
        private static bool _shaderWarned;

        /// <summary>
        /// The hand-written Built-in RP shader, or the best stand-in available. A player build only
        /// contains a shader that something references or that GraphicsSettings always includes, so
        /// the fallback chain matters: an unlit-looking machine beats a magenta one.
        /// </summary>
        public static Shader MachineShader
        {
            get
            {
                if (_shader != null) return _shader;
                _shader = Shader.Find(ShaderName);
                if (_shader == null) _shader = Shader.Find("Standard");
                if (_shader == null) _shader = Shader.Find("Legacy Shaders/Diffuse");
                if (_shader == null && !_shaderWarned)
                {
                    _shaderWarned = true;
                    Debug.LogWarning("[Art] Neither " + ShaderName + " nor Standard could be found; generated models will use whatever material the import produced.");
                }
                return _shader;
            }
        }

        /// <summary>
        /// The shared material for one texture set. <paramref name="smoothness"/> and
        /// <paramref name="metallic"/> are the values the shader uses where no ORM map was found.
        /// </summary>
        public static Material Base(string setId, float smoothness, float metallic)
        {
            string key = setId + "|" + smoothness.ToString("0.00") + "|" + metallic.ToString("0.00");
            if (_materials.TryGetValue(key, out var cached) && cached != null) return cached;

            var shader = MachineShader;
            if (shader == null) return null;
            var m = new Material(shader) { name = "art_" + setId };
            m.enableInstancing = true;

            var albedo = Load(setId, "albedo");
            var normal = Load(setId, "normal");
            var orm = Load(setId, "orm");
            // Not every set has a wear map of its own - the trim sheet has none - and a wear mask is a
            // noise field rather than a surface, so borrowing the machine set's costs nothing.
            var wear = Load(setId, "wear") ?? Load(SetMachine, "wear");

            if (albedo != null) m.SetTexture("_MainTex", albedo);
            if (normal != null) m.SetTexture("_BumpMap", normal);
            if (orm != null) m.SetTexture("_ORM", orm);
            if (wear != null) m.SetTexture("_WearMask", wear);
            m.SetFloat("_UseOrm", orm != null ? 1f : 0f);
            m.SetFloat("_UseWear", wear != null ? 1f : 0f);
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetColor("_LiveryColor", Color.white);
            m.SetColor("_AccentColor", Color.white);

            _materials[key] = m;
            return m;
        }

        /// <summary>Bodywork: painted, mid-gloss, and the slot LiveryTint recolours.</summary>
        public static Material Body(string setId) => Base(setId, 0.45f, 0.0f);

        /// <summary>Tracks, frames, rams and unpainted steel.</summary>
        public static Material Metal(string setId) => Base(setId, 0.30f, 0.65f);

        /// <summary>Glazing. Opaque and very smooth: a dark reflective pane reads as glass at 200 m
        /// and costs neither a transparent queue nor sorting against the snow.</summary>
        public static Material Glass() => Base(SetGlass, 0.92f, 0.0f);

        private static Texture2D Load(string setId, string suffix)
        {
            return Resources.Load<Texture2D>(TextureRoot + setId + "_" + suffix);
        }

        /// <summary>Drops every cached material. Only the asset pipeline's own reload path needs this.</summary>
        public static void Clear()
        {
            _materials.Clear();
            _shader = null;
            _shaderWarned = false;
        }
    }
}
