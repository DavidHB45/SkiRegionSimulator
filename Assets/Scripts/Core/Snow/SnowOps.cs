using System;
using AlpineSim.Core.Data;
using AlpineSim.Core.Math;

    /// <summary>
    /// The tuning anchors the per-cell snow operations read, gathered once per tick by their callers: a working
    /// machine touches a few dozen cells a tick and every anchor lookup is a locked dictionary access, which had
    /// made the snow contact the most expensive thing in the simulation.
    /// </summary>
    public struct SnowParams
    {
        public float CompactionReferenceKpa, CompactLooseFractionPerPass, CompactedLooseDensity, CompactionRatePerPass, RutSoftDensityLo, RutSoftDensityRange, RutPerVehiclePass;
        public float CorduroyDensityHi, TillerHardeningRange, TillerMoistureRetain;
        public float SkierIceRoughnessFactor, RoughnessPerSkierPass, SkierLoosenKgPerPass, ScrapeSlopeRefDeg, ScrapeKgPerSkierPass, ScrapeMaxFractionPerPass, ScrapeEdgeShare;

        public static SnowParams From(TuningData t) => new SnowParams
        {
            CompactionReferenceKpa = t.F("snow.compactionReferenceKpa"),
            CompactLooseFractionPerPass = t.F("snow.compactLooseFractionPerPass"),
            CompactedLooseDensity = t.F("snow.compactedLooseDensity"),
            CompactionRatePerPass = t.F("snow.compactionRatePerPass"),
            RutSoftDensityLo = t.F("snow.rutSoftDensityLo"),
            RutSoftDensityRange = t.F("snow.rutSoftDensityRange"),
            RutPerVehiclePass = t.F("snow.rutPerVehiclePass"),
            CorduroyDensityHi = t.F("snow.corduroyDensityHi"),
            TillerHardeningRange = t.F("snow.tillerHardeningRange"),
            TillerMoistureRetain = t.F("snow.tillerMoistureRetain"),
            SkierIceRoughnessFactor = t.F("snow.skierIceRoughnessFactor"),
            RoughnessPerSkierPass = t.F("snow.roughnessPerSkierPass"),
            SkierLoosenKgPerPass = t.F("snow.skierLoosenKgPerPass"),
            ScrapeSlopeRefDeg = t.F("snow.scrapeSlopeRefDeg"),
            ScrapeKgPerSkierPass = t.F("snow.scrapeKgPerSkierPass"),
            ScrapeMaxFractionPerPass = t.F("snow.scrapeMaxFractionPerPass"),
            ScrapeEdgeShare = t.F("snow.scrapeEdgeShare"),
        };
    }

namespace AlpineSim.Core.Snow
{
    /// <summary>
    /// Every mutation of the snow grid goes through here. Groomers, guests, weather, guns, blowers,
    /// spreaders and haulers all call these; nothing else touches the arrays.
    ///
    /// Mass rules: Deposit* and Remove* change mass. Cut/Compact/Till/SkierPass/Move conserve it.
    /// All coefficients come from tuning.json ("snow.*").
    /// </summary>
    public static class SnowOps
    {
        public const float MaxDensity = 700f;   // glacier ice; nothing on a piste exceeds this
        public const float MinDensity = 60f;

        // ------------------------------------------------------------------ deposit / remove
        /// <summary>Adds fresh, loose snow (snowfall, blower throw, tiller spill) by mass.</summary>
        public static void DepositLoose(SnowGrid g, int id, float massKgPerM2)
        {
            if (id < 0 || massKgPerM2 <= 0f) return;
            g.LooseMm[id] += massKgPerM2 / g.FreshDensity * 1000f;
            g.MarkDirty(id);
        }

        /// <summary>Adds snow of a given density into the packed layer, mixing densities by mass.</summary>
        public static void DepositPacked(SnowGrid g, int id, float massKgPerM2, float density)
        {
            if (id < 0 || massKgPerM2 <= 0f) return;
            density = MathUtil.Clamp(density, MinDensity, MaxDensity);
            float oldMass = g.PackedMm[id] * g.Density[id] * 0.001f;
            float newMass = oldMass + massKgPerM2;
            float newDepth = g.PackedMm[id] + massKgPerM2 / density * 1000f;
            g.Density[id] = newDepth > 1e-6f ? MathUtil.Clamp(newMass / newDepth * 1000f, MinDensity, MaxDensity) : density;
            g.PackedMm[id] = newDepth;
            g.MarkDirty(id);
        }

        /// <summary>Removes up to the requested mass, loose layer first. Returns the mass actually removed.</summary>
        public static float Remove(SnowGrid g, int id, float massKgPerM2)
        {
            if (id < 0 || massKgPerM2 <= 0f) return 0f;
            float removed = 0f;
            float looseMass = g.LooseMm[id] * g.FreshDensity * 0.001f;
            if (looseMass > 0f)
            {
                float take = MathF.Min(looseMass, massKgPerM2);
                g.LooseMm[id] -= take / g.FreshDensity * 1000f;
                if (g.LooseMm[id] < 1e-5f) g.LooseMm[id] = 0f;
                removed += take;
            }
            float remaining = massKgPerM2 - removed;
            if (remaining > 0f)
            {
                float packedMass = g.PackedMm[id] * g.Density[id] * 0.001f;
                float take = MathF.Min(packedMass, remaining);
                if (packedMass > 1e-9f) g.PackedMm[id] -= take / g.Density[id] * 1000f;
                if (g.PackedMm[id] < 1e-5f) g.PackedMm[id] = 0f;
                removed += take;
            }
            g.MarkDirty(id);
            return removed;
        }

        /// <summary>Removes up to a depth of snow from the top (loose first). Returns mass removed (kg/m²) and its mean density.</summary>
        public static float CutDepth(SnowGrid g, int id, float depthMm, out float meanDensity)
        {
            meanDensity = g.FreshDensity;
            if (id < 0 || depthMm <= 0f) return 0f;
            float mass = 0f, depth = 0f;
            float takeLoose = MathF.Min(g.LooseMm[id], depthMm);
            if (takeLoose > 0f)
            {
                g.LooseMm[id] -= takeLoose;
                mass += takeLoose * g.FreshDensity * 0.001f;
                depth += takeLoose;
            }
            float left = depthMm - takeLoose;
            if (left > 0f)
            {
                float takePacked = MathF.Min(g.PackedMm[id], left);
                g.PackedMm[id] -= takePacked;
                mass += takePacked * g.Density[id] * 0.001f;
                depth += takePacked;
            }
            if (depth > 1e-6f) meanDensity = mass / depth * 1000f;
            g.MarkDirty(id);
            return mass;
        }

        /// <summary>Moves mass from one cell to another keeping the source's mean density. Conserves mass.</summary>
        public static float Move(SnowGrid g, int from, int to, float massKgPerM2)
        {
            if (from < 0 || massKgPerM2 <= 0f) return 0f;
            float density = g.ColumnDensity(from);
            float moved = Remove(g, from, massKgPerM2);
            if (to >= 0 && moved > 0f) DepositPacked(g, to, moved, MathF.Max(density, MinDensity));
            else if (moved > 0f) DepositPacked(g, from, moved, MathF.Max(density, MinDensity)); // nowhere to go: put it back
            return moved;
        }

        // ------------------------------------------------------------------ compaction / grooming
        /// <summary>
        /// Track or tyre pressure on a cell. Converts part of the loose layer into packed snow and
        /// drives the packed density toward the pressure-dependent target; ruts soft snow.
        /// <paramref name="exposure"/> is the fraction of a full pass (0..1) this call represents.
        /// </summary>
        public static void Compact(SnowGrid g, int id, float pressureKpa, float exposure, TuningData t)
            => Compact(g, id, pressureKpa, exposure, t.Curve("snow.compactionTargetDensityByKpa", pressureKpa), SnowParams.From(t));

        /// <summary>One running-gear pass over a cell with the anchors and the pressure's target density resolved by the caller.</summary>
        public static void Compact(SnowGrid g, int id, float pressureKpa, float exposure, float targetDensity, in SnowParams p)
        {
            if (id < 0 || exposure <= 0f) return;
            float refP = p.CompactionReferenceKpa;
            float pf = MathUtil.Clamp01(pressureKpa / refP);
            // loose -> packed
            float frac = MathUtil.Clamp01(p.CompactLooseFractionPerPass * (0.4f + 0.6f * pf) * exposure);
            float looseMass = g.LooseMm[id] * g.FreshDensity * 0.001f;
            if (looseMass > 0f && frac > 0f)
            {
                float m = looseMass * frac;
                g.LooseMm[id] -= m / g.FreshDensity * 1000f;
                DepositPacked(g, id, m, p.CompactedLooseDensity);
            }
            // densify packed layer toward target
            float target = targetDensity;
            float k = p.CompactionRatePerPass * exposure;
            if (g.Density[id] < target)
            {
                float mass = g.PackedMm[id] * g.Density[id] * 0.001f;
                g.Density[id] = MathUtil.Clamp(g.Density[id] + (target - g.Density[id]) * k, MinDensity, MaxDensity);
                if (mass > 0f) g.PackedMm[id] = mass / g.Density[id] * 1000f;
            }
            // rutting
            float soft = MathUtil.Clamp01(1f - (g.Density[id] - p.RutSoftDensityLo) / p.RutSoftDensityRange);
            g.Roughness[id] = MathUtil.Clamp01(g.Roughness[id] + p.RutPerVehiclePass * soft * exposure);
            g.MarkDirty(id);
        }

        /// <summary>
        /// One tiller pass over a cell. Mixes the loose layer into the pack, relaxes density toward
        /// the tiller's target with an efficiency that falls as the pack hardens past the corduroy
        /// band (so repeated passes creep toward ice), adds the compaction-bar increment, resets
        /// roughness by finish quality and stamps the groom time/direction.
        /// </summary>
        public static void Till(SnowGrid g, int id, float targetDensity, float efficiency, float compactionPerPass, float finishQuality, byte groomDir, int tick, TuningData t)
            => Till(g, id, targetDensity, efficiency, compactionPerPass, finishQuality, groomDir, tick, SnowParams.From(t));

        public static void Till(SnowGrid g, int id, float targetDensity, float efficiency, float compactionPerPass, float finishQuality, byte groomDir, int tick, in SnowParams p)
        {
            if (id < 0) return;
            float looseMass = g.LooseMm[id] * g.FreshDensity * 0.001f;
            float packedMass = g.PackedMm[id] * g.Density[id] * 0.001f;
            float total = looseMass + packedMass;
            if (total <= 1e-6f)
            {
                g.LooseMm[id] = 0f; g.PackedMm[id] = 0f;
                g.Roughness[id] = MathUtil.Clamp01(g.Roughness[id] * (1f - finishQuality));
                g.LastGroomTick[id] = tick;
                g.GroomDir[id] = groomDir;
                g.MarkDirty(id);
                return;
            }
            float depth = g.LooseMm[id] + g.PackedMm[id];
            float mixed = total / depth * 1000f; // mass-weighted mean density after pulverising
            float bandHi = p.CorduroyDensityHi;
            float iceRange = p.TillerHardeningRange;
            float d;
            if (mixed < targetDensity)
            {
                // soft snow: the tiller re-lays it near its target, plus the compaction bar
                d = mixed + (targetDensity - mixed) * MathUtil.Clamp01(efficiency) + compactionPerPass;
            }
            else
            {
                // already at or past target: each pass work-hardens the pack; the effect fades toward ice
                d = mixed + compactionPerPass * MathUtil.Clamp01(1f - (mixed - bandHi) / iceRange);
            }
            d = MathUtil.Clamp(d, MinDensity, MaxDensity);
            g.Density[id] = d;
            g.PackedMm[id] = total / d * 1000f;
            g.LooseMm[id] = 0f;
            g.Roughness[id] = MathUtil.Clamp01(g.Roughness[id] * (1f - finishQuality));
            g.Moisture[id] = MathUtil.Clamp01(g.Moisture[id] * p.TillerMoistureRetain);
            g.LastGroomTick[id] = tick;
            g.GroomDir[id] = groomDir;
            g.MarkDirty(id);
        }

        public static void AddRoughness(SnowGrid g, int id, float amount)
        {
            if (id < 0) return;
            g.Roughness[id] = MathUtil.Clamp01(g.Roughness[id] + amount);
            g.MarkDirty(id);
        }

        /// <summary>
        /// Skier traffic over a cell: <paramref name="traffic"/> skier-passes. Adds roughness on soft
        /// snow, scrapes packed snow downhill along <paramref name="moveDir"/> and pushes some to the
        /// run edge (away from the centreline). Conserves mass; when a target cell is outside the
        /// allocated envelope the mass stays where it is.
        /// </summary>
        public static void SkierPass(SnowGrid g, int id, float traffic, Vec2 moveDir, TuningData t) => SkierPass(g, id, traffic, moveDir, SnowParams.From(t));

        public static void SkierPass(SnowGrid g, int id, float traffic, Vec2 moveDir, in SnowParams p)
        {
            if (id < 0 || traffic <= 0f) return;
            float density = g.ColumnDensity(id);
            float soft = MathUtil.Clamp01(1f - (density - p.RutSoftDensityLo) / p.RutSoftDensityRange);
            float iceBonus = density > p.CorduroyDensityHi ? p.SkierIceRoughnessFactor : 1f;
            g.Roughness[id] = MathUtil.Clamp01(g.Roughness[id] + p.RoughnessPerSkierPass * traffic * (0.4f + 0.6f * soft) * iceBonus);

            // edges chatter the surface into loose chop (mass conserved: packed -> loose)
            float loosen = p.SkierLoosenKgPerPass * traffic * (0.5f + 0.5f * soft);
            float packedMass = g.PackedMm[id] * g.Density[id] * 0.001f;
            if (loosen > packedMass * 0.05f) loosen = packedMass * 0.05f;
            if (loosen > 0f && packedMass > 1e-6f)
            {
                g.PackedMm[id] -= loosen / g.Density[id] * 1000f;
                g.LooseMm[id] += loosen / g.FreshDensity * 1000f;
            }

            float slopeFactor = MathUtil.Clamp01(g.SlopeDeg[id] / p.ScrapeSlopeRefDeg);
            float scrape = p.ScrapeKgPerSkierPass * traffic * (0.3f + 0.7f * soft) * (0.3f + 0.7f * slopeFactor);
            float available = g.MassKgPerM2(id);
            if (scrape > available * p.ScrapeMaxFractionPerPass) scrape = available * p.ScrapeMaxFractionPerPass;
            if (scrape <= 1e-6f) { g.MarkDirty(id); return; }

            g.CellCoordsOf(id, out int cx, out int cy);
            Vec2 d = moveDir.Normalized;
            int dx = MathF.Abs(d.X) >= 0.5f ? System.Math.Sign(d.X) : 0;
            int dy = MathF.Abs(d.Y) >= 0.5f ? System.Math.Sign(d.Y) : 0;
            int downhill = g.CellId(cx + dx, cy + dy);
            // lateral push: perpendicular to travel, away from the centreline
            float lat = g.Lateral[id];
            float side = lat >= 0f ? 1f : -1f;
            Vec2 perp = d.Perp * side;
            int px = MathF.Abs(perp.X) >= 0.5f ? System.Math.Sign(perp.X) : 0;
            int py = MathF.Abs(perp.Y) >= 0.5f ? System.Math.Sign(perp.Y) : 0;
            int edge = g.CellId(cx + px, cy + py);

            float edgeShare = p.ScrapeEdgeShare;
            float toEdge = scrape * edgeShare;
            float toDown = scrape - toEdge;
            float srcDensity = MathF.Max(density, MinDensity);
            float removed = Remove(g, id, scrape);
            float actualEdge = removed * (toEdge / scrape);
            float actualDown = removed - actualEdge;
            if (edge >= 0) DepositPacked(g, edge, actualEdge, srcDensity); else DepositPacked(g, id, actualEdge, srcDensity);
            if (downhill >= 0) DepositPacked(g, downhill, actualDown, srcDensity); else DepositPacked(g, id, actualDown, srcDensity);
        }

        /// <summary>Salt/brine applied to a road or lot cell (kg/m² of NaCl equivalent).</summary>
        public static void AddSalt(SnowGrid g, int id, float kgPerM2)
        {
            if (id < 0 || kgPerM2 <= 0f) return;
            g.Salt[id] += kgPerM2;
            g.MarkDirty(id);
        }
    }
}
