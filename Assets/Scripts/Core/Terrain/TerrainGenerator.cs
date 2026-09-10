using System;
using AlpineSim.Core.Data;
using AlpineSim.Core.Math;

namespace AlpineSim.Core.Terrain
{
    /// <summary>
    /// Builds the bare-ground heightmap from scenario parameters and the world seed.
    /// Layout: a south base area rising to an east-west ridge in the north; low-frequency lateral
    /// bowls and spurs; fBm detail that grows with elevation; an optional gorge that isolates the
    /// eastern bowl (only a long-span lift can cross it); engineered flats for base and lots.
    /// Later milestones call <see cref="SmoothCorridor"/> for pistes and roads.
    /// </summary>
    public static class TerrainGenerator
    {
        public static TerrainData Build(ScenarioData scenario, int seed)
        {
            var p = scenario.Terrain;
            int res = System.Math.Max(64, p.SizeM);
            var t = new TerrainData(res, 1f);
            uint nseed = unchecked((uint)(seed * 747796405 + p.SeedOffset * 2891336453));

            float ridgeHeight = p.RidgeElevationM - p.BaseElevationM;
            for (int iy = 0; iy < res; iy++)
            {
                float y = iy;
                // Ridge profile: rises with an ease curve to RidgeY, then drops on the back side.
                float up = MathUtil.SmoothStep(0f, p.RidgeY, y);
                float profile = p.BaseElevationM + ridgeHeight * MathF.Pow(up, 1.35f);
                if (y > p.RidgeY)
                {
                    float back = MathUtil.SmoothStep(p.RidgeY, p.RidgeY + p.RidgeFalloffM, y);
                    profile -= p.BackSlopeDropM * back;
                }
                for (int ix = 0; ix < res; ix++)
                {
                    float x = ix;
                    float elevFrac = MathUtil.Clamp01((profile - p.BaseElevationM) / System.Math.Max(1f, ridgeHeight));
                    // Lateral bowls/spurs: a slow cosine plus low-frequency noise, stronger on the upper mountain.
                    float lateral = MathF.Cos(x / p.LateralWavelengthM * MathUtil.TwoPi) * p.LateralAmplitudeM * (0.3f + 0.7f * elevFrac);
                    float low = Noise.Fbm(x * 0.0009f, y * 0.0009f, nseed + 11u, 3, 0.5f, 2f) * p.LateralAmplitudeM * 1.2f * (0.2f + 0.8f * elevFrac);
                    float rugged = 1f + p.RuggednessWithElevation * elevFrac;
                    float detail = Noise.Fbm(x * p.NoiseFrequency, y * p.NoiseFrequency, nseed, p.NoiseOctaves, p.NoiseGain, p.NoiseLacunarity) * p.NoiseAmplitudeM * rugged;
                    float ridged = (Noise.Ridged(x * p.NoiseFrequency * 0.6f, y * p.NoiseFrequency * 0.6f, nseed + 3u, 3, 0.5f, 2f) - 0.5f) * p.NoiseAmplitudeM * 0.8f * elevFrac;
                    float h = profile + lateral + low + detail + ridged;
                    t.Heights[iy * res + ix] = h;
                }
            }

            if (p.Gorge != null && p.Gorge.Enabled) CarveGorge(t, p.Gorge);
            foreach (var fa in p.FlatAreas) Flatten(t, fa.X, fa.Y, fa.RadiusM, fa.ElevationM, fa.BlendM, true);
            Flatten(t, scenario.BaseArea.X, scenario.BaseArea.Y, scenario.BaseAreaRadiusM, float.NaN, 80f, true);
            foreach (var c in scenario.GetCorridors())
            {
                if (c.IsDisc) Flatten(t, c.Points[0].X, c.Points[0].Y, c.HalfWidthM, float.NaN, c.BlendM, true);
                else SmoothCorridor(t, c.Points, c.HalfWidthM, c.BlendM, c.MaxGradeDeg, c.Pinned);
            }

            ComputeFlags(t, p);
            t.RecomputeBounds();
            return t;
        }

        private static void CarveGorge(TerrainData t, GorgeParams g)
        {
            int res = t.Res;
            float sigma = g.WidthM * 0.5f;
            int y0 = System.Math.Max(0, (int)(g.Y - g.WidthM * 2f)), y1 = System.Math.Min(res - 1, (int)(g.Y + g.WidthM * 2f));
            int x0 = System.Math.Max(0, (int)g.XStart), x1 = System.Math.Min(res - 1, (int)g.XEnd);
            for (int iy = y0; iy <= y1; iy++)
            {
                float dy = (iy - g.Y) / sigma;
                float profile = MathF.Exp(-dy * dy * 2f);
                for (int ix = x0; ix <= x1; ix++)
                {
                    // fade in over the first 120 m so the gorge mouth is not a wall
                    float fade = MathUtil.SmoothStep(g.XStart, g.XStart + 120f, ix);
                    float cut = g.DepthM * profile * fade;
                    int i = iy * res + ix;
                    t.Heights[i] -= cut;
                    if (cut > g.NoBuildDepthM) t.Flags[i] |= (byte)(TerrainFlags.NoFoundation | TerrainFlags.Gorge);
                }
            }
        }

        /// <summary>Flattens a disc to a target elevation (NaN = mean of the disc), blending at the rim.</summary>
        public static void Flatten(TerrainData t, float cx, float cy, float radius, float elevation, float blend, bool markFlat)
        {
            int res = t.Res;
            int x0 = System.Math.Max(0, (int)(cx - radius - blend)), x1 = System.Math.Min(res - 1, (int)(cx + radius + blend));
            int y0 = System.Math.Max(0, (int)(cy - radius - blend)), y1 = System.Math.Min(res - 1, (int)(cy + radius + blend));
            if (float.IsNaN(elevation))
            {
                double sum = 0; int n = 0;
                for (int iy = y0; iy <= y1; iy++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        float dx = ix - cx, dy = iy - cy;
                        if (dx * dx + dy * dy <= radius * radius) { sum += t.Heights[iy * res + ix]; n++; }
                    }
                elevation = n > 0 ? (float)(sum / n) : t.HeightAt((int)cx, (int)cy);
            }
            for (int iy = y0; iy <= y1; iy++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    float dx = ix - cx, dy = iy - cy;
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d > radius + blend) continue;
                    float w = d <= radius ? 1f : 1f - MathUtil.SmoothStep(radius, radius + blend, d);
                    int i = iy * res + ix;
                    t.Heights[i] = MathUtil.Lerp(t.Heights[i], elevation, w);
                    if (markFlat && d <= radius) t.Flags[i] |= (byte)TerrainFlags.Flat;
                }
        }

        /// <summary>
        /// Benches a corridor along a polyline: within halfWidth of the centreline the cross-slope is
        /// removed (height follows the centreline), blending back to natural terrain over blend metres.
        /// Used for pistes, cat tracks and access roads so machinery and guests get legible surfaces.
        /// </summary>
        public static void SmoothCorridor(TerrainData t, Vec2[] polyline, float halfWidth, float blend, float maxGradeDeg = 0f, bool[] pinned = null)
        {
            if (polyline == null || polyline.Length < 2) return;
            int res = t.Res;
            // Pre-sample centreline heights from the untouched map so smoothing is order independent per corridor.
            var h = new float[polyline.Length];
            for (int i = 0; i < h.Length; i++) h[i] = t.SampleHeight(polyline[i]);
            if (maxGradeDeg > 0f)
            {
                // cut and fill: clamp the rise between consecutive vertices to the grade limit, forward then backward,
                // so the road profile is the nearest feasible one (a real road cuts into ribs and fills gullies).
                // Pinned vertices are tie-ins to other corridors and never move; two passes each way let their
                // constraint propagate through the free vertices between them.
                float tanMax = MathF.Tan(maxGradeDeg * MathUtil.Deg2Rad);
                bool Free(int i) => pinned == null || i >= pinned.Length || !pinned[i];
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int i = 1; i < h.Length; i++) { if (!Free(i)) continue; float rise = Vec2.Distance(polyline[i - 1], polyline[i]) * tanMax; h[i] = MathUtil.Clamp(h[i], h[i - 1] - rise, h[i - 1] + rise); }
                    for (int i = h.Length - 2; i >= 0; i--) { if (!Free(i)) continue; float rise = Vec2.Distance(polyline[i], polyline[i + 1]) * tanMax; h[i] = MathUtil.Clamp(h[i], h[i + 1] - rise, h[i + 1] + rise); }
                }
            }
            // Each cell is stamped once, by the segment it is nearest to, so the result does not depend on segment
            // order and interior vertices carry no seam: stamping every segment over its own bounding box used to
            // flatten the last metres before a vertex toward the vertex height and leave a step after it.
            int last = polyline.Length - 2;
            float pad = halfWidth + blend;
            for (int s = 0; s <= last; s++)
            {
                Vec2 a = polyline[s], b = polyline[s + 1];
                if ((b - a).SqrLength < 1e-6f) continue;
                int x0 = System.Math.Max(0, (int)(MathF.Min(a.X, b.X) - pad)), x1 = System.Math.Min(res - 1, (int)(MathF.Max(a.X, b.X) + pad));
                int y0 = System.Math.Max(0, (int)(MathF.Min(a.Y, b.Y) - pad)), y1 = System.Math.Min(res - 1, (int)(MathF.Max(a.Y, b.Y) + pad));
                for (int iy = y0; iy <= y1; iy++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        var pnt = new Vec2(ix, iy);
                        if (!Nearest(polyline, pnt, out int ns, out float uRaw, out float d) || ns != s || d > pad) continue;
                        float u = MathUtil.Clamp01(uRaw);
                        float target = h[ns] + (h[ns + 1] - h[ns]) * u;
                        float w = d <= halfWidth ? 1f : 1f - MathUtil.SmoothStep(halfWidth, pad, d);
                        // beyond the polyline's ends the corridor fades out over the blend distance instead of stamping
                        // the end height into the ground around the terminal (which built a step at every run bottom)
                        float len = Vec2.Distance(polyline[ns], polyline[ns + 1]);
                        float over = ns == 0 && uRaw < 0f ? -uRaw * len : (ns == last && uRaw > 1f ? (uRaw - 1f) * len : 0f);
                        if (over > 0f) w *= 1f - MathUtil.SmoothStep(0f, blend, over);
                        if (w <= 0f) continue;
                        int i = iy * res + ix;
                        // Keep a little of the natural roll inside the corridor so it is not a billiard table (none on a graded road).
                        float inside = MathUtil.Lerp(t.Heights[i], target, maxGradeDeg > 0f ? 1f : 0.85f);
                        t.Heights[i] = MathUtil.Lerp(t.Heights[i], inside, w);
                    }
            }
        }

        /// <summary>
        /// Nearest segment of a polyline to a point: its index, the unclamped parameter along it and the distance
        /// to the clamped foot. Ties at a shared vertex go to the earlier segment.
        /// </summary>
        private static bool Nearest(Vec2[] polyline, Vec2 pnt, out int seg, out float uRaw, out float dist)
        {
            seg = -1; uRaw = 0f; dist = float.MaxValue;
            for (int s = 0; s < polyline.Length - 1; s++)
            {
                Vec2 a = polyline[s], ab = polyline[s + 1] - a;
                float len2 = ab.SqrLength;
                if (len2 < 1e-6f) continue;
                float ur = Vec2.Dot(pnt - a, ab) / len2;
                float d = Vec2.Distance(pnt, a + ab * MathUtil.Clamp01(ur));
                if (d < dist - 1e-4f) { dist = d; seg = s; uRaw = ur; }
            }
            return seg >= 0;
        }

        private static void ComputeFlags(TerrainData t, TerrainParams p)
        {
            int res = t.Res;
            float tanLimit = MathF.Tan(p.NoBuildSlopeDeg * MathUtil.Deg2Rad);
            for (int iy = 1; iy < res - 1; iy++)
                for (int ix = 1; ix < res - 1; ix++)
                {
                    int i = iy * res + ix;
                    float gx = (t.Heights[i + 1] - t.Heights[i - 1]) * 0.5f;
                    float gy = (t.Heights[i + res] - t.Heights[i - res]) * 0.5f;
                    if (gx * gx + gy * gy > tanLimit * tanLimit) t.Flags[i] |= (byte)TerrainFlags.NoFoundation;
                }
        }
    }
}
