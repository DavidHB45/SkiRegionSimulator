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
        public static void SmoothCorridor(TerrainData t, Vec2[] polyline, float halfWidth, float blend)
        {
            if (polyline == null || polyline.Length < 2) return;
            int res = t.Res;
            // Pre-sample centreline heights from the untouched map so smoothing is order independent per corridor.
            for (int s = 0; s < polyline.Length - 1; s++)
            {
                Vec2 a = polyline[s], b = polyline[s + 1];
                float ha = t.SampleHeight(a), hb = t.SampleHeight(b);
                float pad = halfWidth + blend;
                int x0 = System.Math.Max(0, (int)(MathF.Min(a.X, b.X) - pad)), x1 = System.Math.Min(res - 1, (int)(MathF.Max(a.X, b.X) + pad));
                int y0 = System.Math.Max(0, (int)(MathF.Min(a.Y, b.Y) - pad)), y1 = System.Math.Min(res - 1, (int)(MathF.Max(a.Y, b.Y) + pad));
                Vec2 ab = b - a;
                float len2 = ab.SqrLength;
                if (len2 < 1e-6f) continue;
                for (int iy = y0; iy <= y1; iy++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        var pnt = new Vec2(ix, iy);
                        float u = MathUtil.Clamp01(Vec2.Dot(pnt - a, ab) / len2);
                        Vec2 c = a + ab * u;
                        float d = Vec2.Distance(pnt, c);
                        if (d > pad) continue;
                        float target = ha + (hb - ha) * u;
                        float w = d <= halfWidth ? 1f : 1f - MathUtil.SmoothStep(halfWidth, pad, d);
                        int i = iy * res + ix;
                        // Keep a little of the natural roll inside the corridor so it is not a billiard table.
                        float inside = MathUtil.Lerp(t.Heights[i], target, 0.85f);
                        t.Heights[i] = MathUtil.Lerp(t.Heights[i], inside, w);
                    }
            }
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
