using System.Collections.Generic;
using UnityEngine;

namespace AlpineSim.Unity.Vehicles
{
    /// <summary>
    /// Tiny mesh builder for boxes, wedges and cylinders with vertex colours. Every machine, lift
    /// tower, carrier and prop is assembled from these at runtime; there are no mesh assets.
    /// Local frame: +Z forward, +Y up, +X right (Unity convention).
    /// </summary>
    public sealed class ProceduralMesh
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Color32> _c = new List<Color32>();
        private readonly List<int> _t = new List<int>();

        public int VertexCount => _v.Count;

        public void Clear() { _v.Clear(); _n.Clear(); _c.Clear(); _t.Clear(); }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col)
        {
            var n = Vector3.Cross(b - a, c - a).normalized;
            int i = _v.Count;
            _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
            for (int k = 0; k < 4; k++) { _n.Add(n); _c.Add(col); }
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
            _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
        }

        /// <summary>Axis-aligned box centred at c with full size s.</summary>
        public void Box(Vector3 c, Vector3 s, Color32 col)
        {
            Vector3 h = s * 0.5f;
            Vector3 p0 = c + new Vector3(-h.x, -h.y, -h.z), p1 = c + new Vector3(h.x, -h.y, -h.z), p2 = c + new Vector3(h.x, -h.y, h.z), p3 = c + new Vector3(-h.x, -h.y, h.z);
            Vector3 p4 = c + new Vector3(-h.x, h.y, -h.z), p5 = c + new Vector3(h.x, h.y, -h.z), p6 = c + new Vector3(h.x, h.y, h.z), p7 = c + new Vector3(-h.x, h.y, h.z);
            Quad(p4, p5, p6, p7, col); // top
            Quad(p1, p0, p3, p2, col); // bottom
            Quad(p0, p1, p5, p4, col); // back (-z)
            Quad(p2, p3, p7, p6, col); // front (+z)
            Quad(p3, p0, p4, p7, col); // left
            Quad(p1, p2, p6, p5, col); // right
        }

        /// <summary>Box rotated by a quaternion about its centre.</summary>
        public void Box(Vector3 c, Vector3 s, Quaternion rot, Color32 col)
        {
            int start = _v.Count;
            Box(Vector3.zero, s, col);
            for (int i = start; i < _v.Count; i++) { _v[i] = c + rot * _v[i]; _n[i] = rot * _n[i]; }
        }

        /// <summary>Wedge (triangular prism) with the slope facing +Z: base at bottom, height rising toward -Z.</summary>
        public void Wedge(Vector3 c, Vector3 s, Color32 col)
        {
            Vector3 h = s * 0.5f;
            Vector3 p0 = c + new Vector3(-h.x, -h.y, -h.z), p1 = c + new Vector3(h.x, -h.y, -h.z), p2 = c + new Vector3(h.x, -h.y, h.z), p3 = c + new Vector3(-h.x, -h.y, h.z);
            Vector3 p4 = c + new Vector3(-h.x, h.y, -h.z), p5 = c + new Vector3(h.x, h.y, -h.z);
            Quad(p1, p0, p3, p2, col); // bottom
            Quad(p0, p1, p5, p4, col); // back
            Quad(p3, p0, p4, p4, col); // left tri (degenerate quad)
            Quad(p1, p2, p5, p5, col); // right tri
            Quad(p2, p3, p4, p5, col); // slope
        }

        /// <summary>Cylinder along the given axis (0 = X, 1 = Y, 2 = Z).</summary>
        public void Cylinder(Vector3 c, float radius, float length, int axis, int segments, Color32 col)
        {
            int start = _v.Count;
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f, a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                Vector3 r0 = Ring(a0, radius, axis), r1 = Ring(a1, radius, axis);
                Vector3 ax = axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
                Vector3 e0 = c + ax * (-length * 0.5f), e1 = c + ax * (length * 0.5f);
                Quad(e0 + r0, e0 + r1, e1 + r1, e1 + r0, col);
                // caps
                Quad(e1, e1 + r0, e1 + r1, e1 + r1, col);
                Quad(e0, e0 + r1, e0 + r0, e0 + r0, col);
            }
            for (int i = start; i < _v.Count; i++) { }
        }

        private static Vector3 Ring(float a, float r, int axis)
        {
            float x = Mathf.Cos(a) * r, y = Mathf.Sin(a) * r;
            switch (axis)
            {
                case 0: return new Vector3(0f, x, y);
                case 1: return new Vector3(x, 0f, y);
                default: return new Vector3(x, y, 0f);
            }
        }

        /// <summary>Thin line segment as a box (for cables, rails, poles).</summary>
        public void Beam(Vector3 a, Vector3 b, float thickness, Color32 col)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 1e-4f) return;
            var rot = Quaternion.LookRotation(d / len, Mathf.Abs(Vector3.Dot(d / len, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up);
            Box((a + b) * 0.5f, new Vector3(thickness, thickness, len), rot, col);
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = _v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetColors(_c);
            mesh.SetTriangles(_t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Color32 Hex(string hex, Color32 fallback)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return fallback;
        }

        public static Color32 Shade(Color32 c, float f)
        {
            return new Color32((byte)Mathf.Clamp(c.r * f, 0, 255), (byte)Mathf.Clamp(c.g * f, 0, 255), (byte)Mathf.Clamp(c.b * f, 0, 255), 255);
        }

        private static Material _vertexMaterial;
        public static Material VertexMaterial
        {
            get
            {
                if (_vertexMaterial == null)
                {
                    var sh = Shader.Find("AlpineSim/VertexColor");
                    _vertexMaterial = new Material(sh != null ? sh : Shader.Find("Legacy Shaders/Diffuse")) { name = "VertexColor" };
                }
                return _vertexMaterial;
            }
        }
    }
}
