using System.Collections.Generic;
using UnityEngine;

namespace AlpineSim.Unity.Guests
{
    /// <summary>
    /// Draws many copies of one mesh with Graphics.DrawMeshInstanced in batches of at most 1023
    /// (the Unity limit). Used for guest markers and lift carriers; no GameObjects per instance.
    /// </summary>
    public sealed class InstancedBatch
    {
        private readonly List<Matrix4x4[]> _batches = new List<Matrix4x4[]>();
        private readonly int _batchSize;
        private int _count;
        public Mesh Mesh { get; }
        public Material Material { get; }
        public int Layer { get; set; }

        private static Shader _shader;
        public static Material MakeMaterial(Color color, string name)
        {
            if (_shader == null) _shader = Shader.Find("AlpineSim/Instanced");
            var m = new Material(_shader != null ? _shader : Shader.Find("Legacy Shaders/Diffuse")) { name = name };
            m.SetColor("_Color", color);
            m.enableInstancing = true;
            return m;
        }

        public InstancedBatch(Mesh mesh, Material material, int batchSize = 1023)
        {
            Mesh = mesh;
            Material = material;
            _batchSize = Mathf.Clamp(batchSize, 1, 1023);
        }

        public void Clear() { _count = 0; }

        public void Add(Vector3 pos, Quaternion rot, Vector3 scale)
        {
            int b = _count / _batchSize;
            while (_batches.Count <= b) _batches.Add(new Matrix4x4[_batchSize]);
            _batches[b][_count % _batchSize] = Matrix4x4.TRS(pos, rot, scale);
            _count++;
        }

        public void Add(Vector3 pos, float yawDeg, float size) => Add(pos, Quaternion.Euler(0f, yawDeg, 0f), new Vector3(size, size, size));

        /// <summary>Submit every batch for this frame (call once per frame from LateUpdate).</summary>
        public void Draw()
        {
            if (Mesh == null || Material == null) return;
            int remaining = _count;
            for (int b = 0; remaining > 0 && b < _batches.Count; b++)
            {
                int n = Mathf.Min(_batchSize, remaining);
                Graphics.DrawMeshInstanced(Mesh, 0, Material, _batches[b], n, null, UnityEngine.Rendering.ShadowCastingMode.On, true, Layer);
                remaining -= n;
            }
        }

        public int Count => _count;
    }
}
