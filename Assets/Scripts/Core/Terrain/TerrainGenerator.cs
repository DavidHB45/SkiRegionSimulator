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
            foreach (var fa in p.FlatAreas) Flatten(t, fa.X, fa.Y, fa.RadiusM, fa.ElevationM, fa.BlendM, true, p.CorridorBankDeg);
            // the base pad follows the valley floor at a third of its gradient; its banks may reach 240 m up or down the hill
            Flatten(t, scenario.BaseArea.X, scenario.BaseArea.Y, scenario.BaseAreaRadiusM, float.NaN, 240f, true, p.CorridorBankDeg, p.BaseAreaTiltFrac);
            foreach (var c in scenario.GetCorridors())
            {
                // a lot gets a long smooth apron (its radius over the tangent of LotBankDeg) rather than a clamped cone:
                // the cone kept the natural ground's own steps inside its envelope, and cars have to drive off a lot
                if (c.IsDisc) Flatten(t, c.Points[0].X, c.Points[0].Y, c.HalfWidthM, float.NaN, MathF.Max(c.BlendM, c.HalfWidthM / MathF.Tan(p.LotBankDeg * MathUtil.Deg2Rad)), true);
                else SmoothCorridor(t, c.Points, c.HalfWidthM, c.BlendM, c.MaxGradeDeg, c.Pinned, p.CorridorBankDeg, c.MaxEarthworkM);
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

        /// <summary>
        /// Flattens a disc to a target elevation (NaN = mean of the disc). With bankDeg = 0 the rim blends back to
        /// natural terrain over blend metres (a smoothstep). With bankDeg &gt; 0 the platform meets the hill the way an
        /// engineered pad does: outside the radius the natural surface is clamped to a cone of slope bankDeg around
        /// the pad level, so a cut face or a fill bank never exceeds that angle and the pad creases into the hillside
        /// where the ground is already at its level; blend is then how far the banks may reach. tiltFrac tilts the
        /// pad to that fraction of the ground's mean gradient across the disc (a base area follows the valley floor a
        /// little rather than being a billiard table cut into the mountain).
        /// </summary>
        public static void Flatten(TerrainData t, float cx, float cy, float radius, float elevation, float blend, bool markFlat, float bankDeg = 0f, float tiltFrac = 0f)
        {
            int res = t.Res;
            int x0 = System.Math.Max(0, (int)(cx - radius - blend)), x1 = System.Math.Min(res - 1, (int)(cx + radius + blend));
            int y0 = System.Math.Max(0, (int)(cy - radius - blend)), y1 = System.Math.Min(res - 1, (int)(cy + radius + blend));
            float gx = 0f, gy = 0f;
            if (float.IsNaN(elevation) || tiltFrac > 0f)
            {
                double sum = 0; int n = 0; double sxh = 0, syh = 0, sxx = 0, syy = 0;
                for (int iy = y0; iy <= y1; iy++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        float dx = ix - cx, dy = iy - cy;
                        if (dx * dx + dy * dy <= radius * radius) { float hh = t.Heights[iy * res + ix]; sum += hh; n++; sxh += dx * hh; syh += dy * hh; sxx += dx * dx; syy += dy * dy; }
                    }
                float mean = n > 0 ? (float)(sum / n) : t.HeightAt((int)cx, (int)cy);
                if (float.IsNaN(elevation)) elevation = mean;
                if (tiltFrac > 0f && n > 0)
                {
                    // least-squares plane through the disc (the disc is symmetric, so the axes separate)
                    gx = (float)(sxh / System.Math.Max(1.0, sxx)) * tiltFrac;
                    gy = (float)(syh / System.Math.Max(1.0, syy)) * tiltFrac;
                }
            }
            float tanBank = bankDeg > 0f ? MathF.Tan(MathUtil.Clamp(bankDeg, 10f, 60f) * MathUtil.Deg2Rad) : 0f;
            for (int iy = y0; iy <= y1; iy++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    float dx = ix - cx, dy = iy - cy;
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d > radius + blend) continue;
                    int i = iy * res + ix;
                    float pad = elevation + gx * dx + gy * dy;
                    if (bankDeg > 0f)
                    {
                        if (d <= radius) t.Heights[i] = pad;
                        else { float reach = (d - radius) * tanBank; t.Heights[i] = MathUtil.Clamp(t.Heights[i], pad - reach, pad + reach); }
                    }
                    else
                    {
                        float w = d <= radius ? 1f : 1f - MathUtil.SmoothStep(radius, radius + blend, d);
                        t.Heights[i] = MathUtil.Lerp(t.Heights[i], pad, w);
                    }
                    if (markFlat && d <= radius) t.Flags[i] |= (byte)TerrainFlags.Flat;
                }
        }

        /// <summary>
        /// Benches a corridor along a polyline: within halfWidth of the centreline the cross-slope is
        /// removed (height follows the centreline), blending back to natural terrain over blend metres.
        /// Used for pistes, cat tracks and access roads so machinery and guests get legible surfaces.
        /// The polyline is densified to <see cref="DensifyM"/> spacing so the profile follows the ground
        /// between authored vertices and a grade limit cuts and fills only where the ground demands it;
        /// banks never exceed bankDeg, however deep the cut or fill, so a machine can always drive off
        /// the corridor edge.
        /// </summary>
        public static void SmoothCorridor(TerrainData t, Vec2[] polyline, float halfWidth, float blend, float maxGradeDeg = 0f, bool[] pinned = null, float bankDeg = 26f, float maxEarthworkM = 0f)
        {
            if (polyline == null || polyline.Length < 2) return;
            int res = t.Res;
            Densify(polyline, pinned, DensifyM, out var pts, out var pin);
            int n = pts.Length;
            // Pre-sample centreline heights from the untouched map so smoothing is order independent per corridor.
            var h = new float[n];
            var natural = new float[n];
            for (int i = 0; i < n; i++) h[i] = natural[i] = t.SampleHeight(pts[i]);
            if (maxGradeDeg > 0f)
            {
                // Cut and fill as local step removal: wherever the rise between two consecutive vertices exceeds the
                // grade limit, the high end is cut and the low end filled by half the excess each (all of it on one end
                // when the other is pinned), never beyond the earthwork budget from the natural ground. Repeating the
                // sweep smooths a step across several vertices but never propagates along the whole corridor: a
                // chain-propagating clamp had lifted a cat track eight metres off the base pad because its pinned top
                // could not be reached at the limit grade. A long stretch steeper than the limit therefore stays
                // steep (the route is the designer's), while the steps at run vertices and terminals disappear.
                float tanMax = MathF.Tan(maxGradeDeg * MathUtil.Deg2Rad);
                bool Free(int i) => pin == null || !pin[i];
                float Bound(int i, float value) => maxEarthworkM > 0f ? MathUtil.Clamp(value, natural[i] - maxEarthworkM, natural[i] + maxEarthworkM) : value;
                for (int round = 0; round < 8; round++)
                {
                    bool changed = false;
                    for (int i = 1; i < n; i++)
                    {
                        float lim = Vec2.Distance(pts[i - 1], pts[i]) * tanMax;
                        float diff = h[i] - h[i - 1];
                        if (MathF.Abs(diff) <= lim + 1e-3f) continue;
                        float excess = diff - MathF.Sign(diff) * lim;
                        bool fa = Free(i - 1), fb = Free(i);
                        if (fa && fb) { h[i] = Bound(i, h[i] - excess * 0.5f); h[i - 1] = Bound(i - 1, h[i - 1] + excess * 0.5f); }
                        else if (fb) h[i] = Bound(i, h[i] - excess);
                        else if (fa) h[i - 1] = Bound(i - 1, h[i - 1] + excess);
                        else continue;
                        changed = true;
                    }
                    if (!changed) break;
                }
            }
            // how far the banks can reach: the deepest cut or fill along the centreline and across the bench, at the bank angle
            float tanBank = MathF.Tan(MathUtil.Clamp(bankDeg, 10f, 60f) * MathUtil.Deg2Rad);
            float padExt = halfWidth + blend;
            for (int round = 0; round < 3; round++)
            {
                float maxDelta = 0f;
                for (int i = 0; i < n; i++)
                {
                    maxDelta = MathF.Max(maxDelta, MathF.Abs(h[i] - natural[i]));
                    Vec2 d = (i + 1 < n ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).Normalized;
                    var nrm = new Vec2(-d.Y, d.X);
                    maxDelta = MathF.Max(maxDelta, MathF.Abs(h[i] - t.SampleHeight(pts[i] + nrm * padExt)));
                    maxDelta = MathF.Max(maxDelta, MathF.Abs(h[i] - t.SampleHeight(pts[i] - nrm * padExt)));
                }
                float next = halfWidth + MathF.Min(120f, MathF.Max(blend, maxDelta / tanBank + 2f));
                if (next <= padExt + 0.5f) { padExt = MathF.Max(padExt, next); break; }
                padExt = next;
            }
            // segment bounding boxes (padded) for cheap rejection in the per-cell scan
            int segs = n - 1;
            var bx0 = new float[segs]; var bx1 = new float[segs]; var by0 = new float[segs]; var by1 = new float[segs]; var len = new float[segs];
            float ux0 = float.MaxValue, ux1 = float.MinValue, uy0 = float.MaxValue, uy1 = float.MinValue;
            for (int s = 0; s < segs; s++)
            {
                Vec2 a = pts[s], b = pts[s + 1];
                len[s] = Vec2.Distance(a, b);
                bx0[s] = MathF.Min(a.X, b.X) - padExt; bx1[s] = MathF.Max(a.X, b.X) + padExt;
                by0[s] = MathF.Min(a.Y, b.Y) - padExt; by1[s] = MathF.Max(a.Y, b.Y) + padExt;
                ux0 = MathF.Min(ux0, bx0[s]); ux1 = MathF.Max(ux1, bx1[s]); uy0 = MathF.Min(uy0, by0[s]); uy1 = MathF.Max(uy1, by1[s]);
            }
            int x0 = System.Math.Max(0, (int)ux0), x1 = System.Math.Min(res - 1, (int)MathF.Ceiling(ux1));
            int y0 = System.Math.Max(0, (int)uy0), y1 = System.Math.Min(res - 1, (int)MathF.Ceiling(uy1));
            const float taper = 8f;
            for (int iy = y0; iy <= y1; iy++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    var pnt = new Vec2(ix, iy);
                    // nearest segment (clamped foot) for the stamp weight, and a blend of every segment that has a
                    // perpendicular foot for the target height: at the inside of a bend two segments overlap and their
                    // cross-sections disagree, so a distance-weighted mix replaces the cliff a hard choice would leave.
                    float dmin = float.MaxValue, uMin = 0f; int sMin = -1;
                    float sumW = 0f, sumT = 0f;
                    for (int s = 0; s < segs; s++)
                    {
                        if (pnt.X < bx0[s] || pnt.X > bx1[s] || pnt.Y < by0[s] || pnt.Y > by1[s] || len[s] < 1e-3f) continue;
                        Vec2 a = pts[s], ab = pts[s + 1] - a;
                        float uRaw = Vec2.Dot(pnt - a, ab) / (len[s] * len[s]);
                        float u = MathUtil.Clamp01(uRaw);
                        float d = Vec2.Distance(pnt, a + ab * u);
                        if (d < dmin - 1e-4f) { dmin = d; sMin = s; uMin = uRaw; }
                        if (uRaw > 0f && uRaw < 1f && d < padExt)
                        {
                            float endDist = MathF.Min(uRaw, 1f - uRaw) * len[s];
                            float w = (1f - d / padExt); w *= w;
                            w *= MathUtil.Clamp01(endDist / taper);
                            sumW += w; sumT += w * (h[s] + (h[s + 1] - h[s]) * uRaw);
                        }
                    }
                    if (sMin < 0 || dmin > padExt) continue;
                    float target = sumW > 1e-4f ? sumT / sumW : h[sMin] + (h[sMin + 1] - h[sMin]) * MathUtil.Clamp01(uMin);
                    int i = iy * res + ix;
                    float ground = t.Heights[i];
                    // the bank: never steeper than bankDeg, so a deep fill spreads its toe wider than the authored blend
                    float blendEff = MathF.Min(MathF.Max(blend, MathF.Abs(target - ground) / tanBank), padExt - halfWidth);
                    float wStamp = dmin <= halfWidth ? 1f : 1f - MathUtil.SmoothStep(halfWidth, halfWidth + blendEff, dmin);
                    // beyond the polyline's ends the corridor fades out instead of stamping the end height into the
                    // ground around the terminal (which built a step at every run bottom)
                    float over = sMin == 0 && uMin < 0f ? -uMin * len[sMin] : (sMin == segs - 1 && uMin > 1f ? (uMin - 1f) * len[sMin] : 0f);
                    if (over > 0f) wStamp *= 1f - MathUtil.SmoothStep(0f, blendEff, over);
                    if (wStamp <= 0f) continue;
                    // Keep a little of the natural roll inside the corridor so it is not a billiard table (none on a graded road).
                    float inside = MathUtil.Lerp(ground, target, maxGradeDeg > 0f ? 1f : 0.85f);
                    t.Heights[i] = MathUtil.Lerp(ground, inside, wStamp);
                }
        }

        /// <summary>Vertex spacing corridors are resampled to before grading, so the profile can follow the ground.</summary>
        public const float DensifyM = 20f;

        /// <summary>Inserts vertices so no segment is longer than spacing; authored vertices keep their pinned flag.</summary>
        private static void Densify(Vec2[] polyline, bool[] pinned, float spacing, out Vec2[] pts, out bool[] pin)
        {
            var outPts = new System.Collections.Generic.List<Vec2>(polyline.Length * 4);
            var outPin = new System.Collections.Generic.List<bool>(polyline.Length * 4);
            for (int i = 0; i < polyline.Length - 1; i++)
            {
                Vec2 a = polyline[i], b = polyline[i + 1];
                outPts.Add(a); outPin.Add(pinned != null && i < pinned.Length && pinned[i]);
                float len = Vec2.Distance(a, b);
                int parts = System.Math.Max(1, (int)MathF.Ceiling(len / spacing));
                for (int k = 1; k < parts; k++) { outPts.Add(Vec2.Lerp(a, b, k / (float)parts)); outPin.Add(false); }
            }
            int lastIdx = polyline.Length - 1;
            outPts.Add(polyline[lastIdx]); outPin.Add(pinned != null && lastIdx < pinned.Length && pinned[lastIdx]);
            pts = outPts.ToArray();
            pin = pinned != null ? outPin.ToArray() : null;
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
