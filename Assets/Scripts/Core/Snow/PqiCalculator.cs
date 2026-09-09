using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Snow
{
    /// <summary>Published every sim hour for each segment and for the resort average.</summary>
    public struct PqiPublishedEvent
    {
        public int SegmentId;
        public string PisteId;
        public float SegmentPqi;
        public float PistePqi;
        public float ResortAverage;
    }

    /// <summary>
    /// Piste Quality Index: weighted blend of coverage, density band score, smoothness and
    /// hours-since-groom, 0..100. The single number guests and the economy consume.
    /// </summary>
    public static class PqiCalculator
    {
        public struct Breakdown
        {
            public float Coverage, DensityScore, Smoothness, Freshness, Pqi;
            public float MeanDepthMm, MeanDensity, MeanRoughness, HoursSinceGroom;
        }

        public static Breakdown Compute(SnowGrid g, List<int> cells, long tick, TuningData t)
        {
            var b = new Breakdown();
            if (cells == null || cells.Count == 0) return b;
            float minCover = t.F("pqi.minCoverageMm");
            float lo = t.F("pqi.densityBandLo"), hi = t.F("pqi.densityBandHi");
            float softLo = t.F("pqi.densitySoftLo"), softHi = t.F("pqi.densitySoftHi");
            int covered = 0;
            double depthSum = 0, densSum = 0, roughSum = 0, scoreSum = 0, groomSum = 0;
            int groomed = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                int id = cells[i];
                float depth = g.TotalDepthMm(id);
                if (depth >= minCover) covered++;
                float dens = g.ColumnDensity(id);
                depthSum += depth;
                densSum += dens;
                roughSum += g.Roughness[id];
                scoreSum += depth >= minCover * 0.5f ? MathUtil.BandScore(dens, lo, hi, softLo, softHi) : 0f;
                if (g.LastGroomTick[id] >= 0) { groomSum += (tick - g.LastGroomTick[id]) / (double)SimTime.TicksPerHour; groomed++; }
            }
            int n = cells.Count;
            b.Coverage = covered / (float)n;
            b.MeanDepthMm = (float)(depthSum / n);
            b.MeanDensity = (float)(densSum / n);
            b.MeanRoughness = (float)(roughSum / n);
            b.DensityScore = (float)(scoreSum / n);
            b.Smoothness = 1f - b.MeanRoughness;
            if (groomed == 0) { b.HoursSinceGroom = 1e6f; b.Freshness = 0f; }
            else
            {
                b.HoursSinceGroom = (float)(groomSum / groomed);
                float half = t.F("pqi.groomHalfLifeHours");
                b.Freshness = MathF.Pow(0.5f, b.HoursSinceGroom / half) * (groomed / (float)n);
            }
            float pqi = 100f * (t.F("pqi.wCoverage") * b.Coverage + t.F("pqi.wDensity") * b.DensityScore
                                + t.F("pqi.wRoughness") * b.Smoothness + t.F("pqi.wFreshness") * b.Freshness);
            // thin cover caps quality: nobody rates a bare run
            pqi *= MathUtil.Lerp(t.F("pqi.bareCapFactor"), 1f, b.Coverage);
            b.Pqi = MathUtil.Clamp(pqi, 0f, 100f);
            return b;
        }

        public static float ComputeSegment(SnowGrid g, PisteSegment seg, long tick, TuningData t) => Compute(g, seg.Cells, tick, t).Pqi;

        /// <summary>Recomputes every segment and piste, sets the resort average and publishes events.</summary>
        public static void PublishAll(SimContext ctx)
        {
            var net = ctx.World.Pistes;
            var g = ctx.World.Snow;
            long tick = ctx.Time.Tick;
            var t = ctx.Tuning;
            foreach (var seg in net.Segments)
            {
                seg.Pqi = ComputeSegment(g, seg, tick, t);
                seg.LastPqiTick = tick;
            }
            double resortSum = 0, resortLen = 0;
            foreach (var p in net.Pistes)
            {
                double sum = 0, len = 0;
                foreach (var sid in p.SegmentIds)
                {
                    var seg = net.Segment(sid);
                    if (seg == null) continue;
                    sum += seg.Pqi * seg.LengthM;
                    len += seg.LengthM;
                }
                p.Pqi = len > 0 ? (float)(sum / len) : 0f;
                if (p.Open) { resortSum += p.Pqi * len; resortLen += len; }
            }
            net.ResortPqi = resortLen > 0 ? (float)(resortSum / resortLen) : 0f;
            foreach (var seg in net.Segments)
            {
                var p = net.Piste(seg.PisteId);
                ctx.Events.Publish(new PqiPublishedEvent { SegmentId = seg.Id, PisteId = seg.PisteId, SegmentPqi = seg.Pqi, PistePqi = p?.Pqi ?? 0f, ResortAverage = net.ResortPqi });
            }
        }
    }
}
