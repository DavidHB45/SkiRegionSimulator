using System.Collections.Generic;
using AlpineSim.Core.Data;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineSim.Unity.Terrain
{
    /// <summary>
    /// Chunked terrain renderer with two LODs, swapped per frame by camera distance.
    /// M0 uses the ground-only material; M1 swaps in the snow surface material through SetMaterial.
    /// </summary>
    public sealed class TerrainView : MonoBehaviour
    {
        private sealed class Chunk
        {
            public int X, Y;
            public GameObject Go;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Mesh Lod0, Lod1;
            public Vector3 Center;
            public int CurrentLod = -1;
        }

        private readonly List<Chunk> _chunks = new List<Chunk>();
        private Bootstrap _boot;
        private RenderData _render;
        private Material _material;
        public Material Material => _material;
        public int ChunkCount => _chunks.Count;
        public int TerrainLayer { get; private set; }

        public void Build(Bootstrap boot)
        {
            _boot = boot;
            _render = boot.Data.Render;
            var terrain = boot.Sim.Terrain;
            TerrainLayer = LayerMask.NameToLayer("Terrain");
            if (TerrainLayer < 0) TerrainLayer = 0;

            var shader = Shader.Find("AlpineSim/Ground");
            _material = new Material(shader != null ? shader : Shader.Find("Legacy Shaders/Diffuse")) { name = "Ground" };
            _material.SetFloat("_MapSize", terrain.SizeM);

            int chunkSize = Mathf.Max(32, _render.TerrainChunkSizeM);
            int chunksPerAxis = Mathf.CeilToInt(terrain.SizeM / chunkSize);
            for (int cy = 0; cy < chunksPerAxis; cy++)
            {
                for (int cx = 0; cx < chunksPerAxis; cx++)
                {
                    var c = new Chunk { X = cx, Y = cy };
                    c.Lod0 = TerrainMeshBuilder.BuildChunk(terrain, cx, cy, chunkSize, Mathf.Max(1, _render.TerrainLod0StepM));
                    c.Lod1 = TerrainMeshBuilder.BuildChunk(terrain, cx, cy, chunkSize, Mathf.Max(2, _render.TerrainLod1StepM));
                    c.Go = new GameObject("chunk_" + cx + "_" + cy);
                    c.Go.layer = TerrainLayer;
                    c.Go.transform.SetParent(transform, false);
                    c.Filter = c.Go.AddComponent<MeshFilter>();
                    c.Renderer = c.Go.AddComponent<MeshRenderer>();
                    c.Renderer.sharedMaterial = _material;
                    c.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                    c.Renderer.receiveShadows = true;
                    c.Center = c.Lod0.bounds.center;
                    SetLod(c, 1);
                    _chunks.Add(c);
                }
            }
        }

        /// <summary>Replace the ground material (M1 snow surface). Keeps _MapSize set.</summary>
        public void SetMaterial(Material m)
        {
            _material = m;
            _material.SetFloat("_MapSize", _boot.Sim.Terrain.SizeM);
            ApplyMaterials();
        }

        private Material _overlay;

        /// <summary>Draw a second, transparent material over the terrain (debug overlay); null removes it.</summary>
        public void SetOverlay(Material overlay)
        {
            _overlay = overlay;
            ApplyMaterials();
        }

        private void ApplyMaterials()
        {
            var mats = _overlay != null ? new[] { _material, _overlay } : new[] { _material };
            foreach (var c in _chunks) c.Renderer.sharedMaterials = mats;
        }

        private void SetLod(Chunk c, int lod)
        {
            if (c.CurrentLod == lod) return;
            c.CurrentLod = lod;
            c.Filter.sharedMesh = lod == 0 ? c.Lod0 : c.Lod1;
        }

        private void LateUpdate()
        {
            var cam = _boot != null && _boot.Cameras != null ? _boot.Cameras.Camera : null;
            if (cam == null) return;
            Vector3 p = cam.transform.position;
            float d0 = _render.TerrainLod0DistanceM;
            float d0sq = d0 * d0;
            for (int i = 0; i < _chunks.Count; i++)
            {
                var c = _chunks[i];
                float dx = c.Center.x - p.x, dz = c.Center.z - p.z;
                SetLod(c, dx * dx + dz * dz < d0sq ? 0 : 1);
            }
        }

        /// <summary>Ray-march the heightmap for mouse picking; returns map-plane hit or false.</summary>
        public bool Raycast(Ray ray, out Vector3 hit, float maxDistance = 8000f)
        {
            var terrain = _boot.Sim.Terrain;
            float step = 4f;
            Vector3 prev = ray.origin;
            float prevDh = prev.y - terrain.SampleHeight(prev.x, prev.z);
            for (float t = step; t < maxDistance; t += step)
            {
                Vector3 pnt = ray.origin + ray.direction * t;
                if (pnt.x < 0 || pnt.z < 0 || pnt.x > terrain.SizeM || pnt.z > terrain.SizeM)
                {
                    if (t > 200f && (pnt.x < -50 || pnt.z < -50 || pnt.x > terrain.SizeM + 50 || pnt.z > terrain.SizeM + 50)) break;
                    prev = pnt; prevDh = pnt.y - terrain.SampleHeight(Mathf.Clamp(pnt.x, 0, terrain.SizeM), Mathf.Clamp(pnt.z, 0, terrain.SizeM));
                    continue;
                }
                float dh = pnt.y - terrain.SampleHeight(pnt.x, pnt.z);
                if (dh <= 0f)
                {
                    // refine by bisection
                    Vector3 a = prev, b = pnt;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 m = (a + b) * 0.5f;
                        float mdh = m.y - terrain.SampleHeight(Mathf.Clamp(m.x, 0, terrain.SizeM), Mathf.Clamp(m.z, 0, terrain.SizeM));
                        if (mdh <= 0f) b = m; else a = m;
                    }
                    hit = b;
                    hit.y = terrain.SampleHeight(Mathf.Clamp(hit.x, 0, terrain.SizeM), Mathf.Clamp(hit.z, 0, terrain.SizeM));
                    return true;
                }
                prev = pnt;
                prevDh = dh;
                if (dh > 400f) step = 16f; else if (dh > 100f) step = 8f; else step = 2f;
            }
            hit = Vector3.zero;
            return false;
        }
    }
}
