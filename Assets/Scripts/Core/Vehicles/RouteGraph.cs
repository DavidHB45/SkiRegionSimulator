using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Terrain;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Vehicles
{
    /// <summary>
    /// Routing graph over cat tracks, roads, piste centrelines and the base area. Vertices are the
    /// polyline points; edges follow polylines, plus short links where polylines pass within a few
    /// metres of each other. Dijkstra with endpoint snapping. Rebuilt whenever the piste network
    /// changes (new run, new lift ramp).
    /// </summary>
    public sealed class RouteGraph
    {
        private readonly List<Vec2> _verts = new List<Vec2>();
        private readonly List<List<int>> _adj = new List<List<int>>();
        private readonly List<List<float>> _cost = new List<List<float>>();
        private int _builtForVersion = -1;

        public int VertexCount => _verts.Count;

        private Terrain.TerrainData _terrain;
        private float _maxGradeDeg = 24f, _steepFactor = 20f;

        /// <summary>
        /// Builds the graph from roads, cat tracks and pistes plus the base, links vertices within
        /// <paramref name="linkRadiusM"/>, and adds straight cross-country links up to
        /// <paramref name="longLinkRadiusM"/> where the ground between them stays under the grade limit and
        /// takes a foundation. Edge cost grows with grade; edges over the limit cost <paramref name="steepFactor"/>x
        /// so they are a last resort, never the first choice.
        /// </summary>
        public void Build(PisteNetwork net, Vec2 basePos, float linkRadiusM, Terrain.TerrainData terrain = null, float maxGradeDeg = 24f, float longLinkRadiusM = 0f, float steepFactor = 20f)
        {
            _terrain = terrain; _maxGradeDeg = maxGradeDeg; _steepFactor = steepFactor;
            _verts.Clear(); _adj.Clear(); _cost.Clear();
            void AddPolyline(List<Vec2> pts, float costFactor)
            {
                int first = _verts.Count;
                for (int i = 0; i < pts.Count; i++) { _verts.Add(pts[i]); _adj.Add(new List<int>()); _cost.Add(new List<float>()); }
                for (int i = 0; i < pts.Count - 1; i++) Link(first + i, first + i + 1, costFactor);
            }
            foreach (var z in net.Zones)
            {
                if (z.Kind == ZoneKind.Road || z.Kind == ZoneKind.CatTrack) AddPolyline(z.Points, 1f);
                else if (z.Points.Count > 0) { _verts.Add(z.Points[0]); _adj.Add(new List<int>()); _cost.Add(new List<float>()); }
            }
            foreach (var p in net.Pistes) AddPolyline(p.Points, 1.3f);
            _verts.Add(basePos); _adj.Add(new List<int>()); _cost.Add(new List<float>());
            // proximity links, then longer cross-country links where the ground allows
            float r2 = linkRadiusM * linkRadiusM;
            float l2 = longLinkRadiusM * longLinkRadiusM;
            for (int i = 0; i < _verts.Count; i++)
                for (int j = i + 1; j < _verts.Count; j++)
                {
                    float d2 = Vec2.SqrDistance(_verts[i], _verts[j]);
                    if (d2 <= r2) Link(i, j, 1f);
                    else if (d2 <= l2 && terrain != null && Passable(_verts[i], _verts[j])) Link(i, j, 1.2f);
                }
        }

        /// <summary>Steepest grade along a straight segment, sampled every 10 m (0 without terrain).</summary>
        private float MaxGradeAlong(Vec2 a, Vec2 b)
        {
            if (_terrain == null) return 0f;
            float len = Vec2.Distance(a, b);
            if (len < 1f) return 0f;
            Vec2 dir = (b - a) / len;
            float worst = 0f;
            for (float s = 0f; s <= len; s += 10f) worst = MathF.Max(worst, MathF.Abs(_terrain.GradeAlongDeg(a.X + dir.X * s, a.Y + dir.Y * s, dir, 8f)));
            return worst;
        }

        private bool Passable(Vec2 a, Vec2 b)
        {
            if (MaxGradeAlong(a, b) > _maxGradeDeg) return false;
            float len = Vec2.Distance(a, b);
            Vec2 dir = (b - a) / MathF.Max(1f, len);
            for (float s = 0f; s <= len; s += 10f) if (_terrain.HasFlag(a.X + dir.X * s, a.Y + dir.Y * s, TerrainFlags.NoFoundation)) return false;
            return true;
        }

        private void Link(int a, int b, float factor)
        {
            float grade = MaxGradeAlong(_verts[a], _verts[b]);
            float gradeFactor = grade > _maxGradeDeg ? _steepFactor : 1f + 0.5f * grade / MathF.Max(1f, _maxGradeDeg);
            float d = Vec2.Distance(_verts[a], _verts[b]) * factor * gradeFactor;
            if (!_adj[a].Contains(b)) { _adj[a].Add(b); _cost[a].Add(d); }
            if (!_adj[b].Contains(a)) { _adj[b].Add(a); _cost[b].Add(d); }
        }

        private int Nearest(Vec2 p, float maxDist)
        {
            int best = -1; float bd = maxDist * maxDist;
            for (int i = 0; i < _verts.Count; i++)
            {
                float d = Vec2.SqrDistance(_verts[i], p);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>Route from a to b along the graph (includes a and b). Falls back to a straight line.</summary>
        public List<Vec2> Find(Vec2 from, Vec2 to, float snapRadius, float straightMax)
        {
            var route = new List<Vec2>();
            if (Vec2.Distance(from, to) <= straightMax || _verts.Count == 0) { route.Add(from); route.Add(to); return route; }
            int s = Nearest(from, snapRadius), t = Nearest(to, snapRadius);
            if (s < 0 || t < 0) { route.Add(from); route.Add(to); return route; }
            int n = _verts.Count;
            var dist = new float[n]; var prev = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[s] = 0f;
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1; float best = float.MaxValue;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0 || u == t) break;
                done[u] = true;
                var adj = _adj[u]; var cost = _cost[u];
                for (int k = 0; k < adj.Count; k++)
                {
                    int v = adj[k];
                    float nd = dist[u] + cost[k];
                    if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
                }
            }
            if (dist[t] == float.MaxValue) { route.Add(from); route.Add(to); return route; }
            var stack = new List<int>();
            for (int v = t; v >= 0; v = prev[v]) stack.Add(v);
            route.Add(from);
            for (int i = stack.Count - 1; i >= 0; i--) route.Add(_verts[stack[i]]);
            route.Add(to);
            // drop tiny leading/trailing hops
            if (route.Count > 2 && Vec2.Distance(route[0], route[1]) < 2f) route.RemoveAt(1);
            if (route.Count > 2 && Vec2.Distance(route[route.Count - 1], route[route.Count - 2]) < 2f) route.RemoveAt(route.Count - 2);
            return route;
        }
    }
}
