using System.Collections.Generic;
using AlpineSim.Core.Terrain;
using UnityEngine;
using UnityEngine.Rendering;

namespace AlpineSim.Unity.Terrain
{
    /// <summary>
    /// Builds chunk meshes from the Core heightmap. Vertex colour encodes ground type
    /// (r = rock, g = alpine grass/scree, b = engineered flat) so the ground shader needs no textures.
    /// UV0 = map position / map size, used by SnowSurface to sample the snow map.
    /// </summary>
    public static class TerrainMeshBuilder
    {
        public static Mesh BuildChunk(TerrainData terrain, int chunkX, int chunkY, int chunkSizeM, int stepM)
        {
            int x0 = chunkX * chunkSizeM;
            int y0 = chunkY * chunkSizeM;
            int n = chunkSizeM / stepM + 1;
            var verts = new Vector3[n * n];
            var normals = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            var colors = new Color32[n * n];
            float size = terrain.SizeM;
            float invSize = 1f / size;

            for (int j = 0; j < n; j++)
            {
                for (int i = 0; i < n; i++)
                {
                    float mx = Mathf.Min(x0 + i * stepM, size);
                    float my = Mathf.Min(y0 + j * stepM, size);
                    float h = terrain.SampleHeight(mx, my);
                    int idx = j * n + i;
                    verts[idx] = new Vector3(mx, h, my);
                    var nrm = terrain.Normal(mx, my);
                    normals[idx] = new Vector3(nrm.X, nrm.Z, nrm.Y);
                    uvs[idx] = new Vector2(mx * invSize, my * invSize);
                    colors[idx] = GroundColour(terrain, mx, my, nrm.Z);
                }
            }

            var tris = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int j = 0; j < n - 1; j++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                    // Unity is left-handed with Y up: winding (a, c, b) faces up.
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            }

            var mesh = new Mesh { name = "terrain_" + chunkX + "_" + chunkY + "_s" + stepM };
            mesh.indexFormat = verts.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.colors32 = colors;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            // Snow displacement is applied in the vertex shader; pad bounds so chunks are not culled early.
            var b2 = mesh.bounds;
            b2.Expand(new Vector3(0f, 6f, 0f));
            mesh.bounds = b2;
            mesh.UploadMeshData(false);
            return mesh;
        }

        private static Color32 GroundColour(TerrainData terrain, float x, float y, float normalZ)
        {
            var flags = terrain.FlagsAt(x, y);
            if ((flags & TerrainFlags.Flat) != 0) return new Color32(0, 0, 255, 255);
            float slope = Mathf.Acos(Mathf.Clamp01(normalZ)) * Mathf.Rad2Deg;
            float rock = Mathf.InverseLerp(28f, 45f, slope);
            float elev = Mathf.InverseLerp(terrain.MinHeight, terrain.MaxHeight, terrain.SampleHeight(x, y));
            rock = Mathf.Max(rock, Mathf.InverseLerp(0.75f, 0.95f, elev) * 0.6f);
            byte r = (byte)(255 * rock);
            byte g = (byte)(255 * (1f - rock));
            return new Color32(r, g, 0, 255);
        }
    }
}
