using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Weather;

namespace AlpineSim.Core.Snow
{
    /// <summary>
    /// Owns the snow grid's weather-driven evolution and the hourly PQI publish. Every chunk is
    /// visited exactly once per sim hour (amortised across the hour's ticks) with snowfall
    /// deposition, sun/temperature melt, wind scour of loose snow, overnight refreeze, settling of
    /// loose snow into the pack, salt melt on roads and lots, and cell temperature relaxation.
    /// Vehicles, guests, guns and blowers mutate the grid through SnowOps from their own systems.
    ///
    /// Until the full WeatherSystem (M5) is registered, a scripted diurnal weather model from the
    /// scenario drives WorldState.Weather.Current.
    /// </summary>
    public sealed class SnowSystem : ISimSystem
    {
        public string Name => "Snow";

        private bool _hasWeatherSystem;
        private float _baseElevation;

        public void Initialize(SimContext ctx, bool newGame)
        {
            var t = ctx.Tuning;
            var world = ctx.World;
            _baseElevation = ctx.Terrain.SampleHeight(ctx.Sim.Scenario.BaseArea.Pos);
            if (newGame)
            {
                world.Snow = new SnowGrid(ctx.Terrain.SizeM, t.F("terrain.snowCellSizeM"), t.F("snow.freshDensityKgM3"));
                world.Pistes = new PisteNetwork();
                PisteNetworkBuilder.BuildFromScenario(ctx);
                SimpleWeather.Apply(ctx, world.Weather);
                PqiCalculator.PublishAll(ctx);
            }
            else
            {
                if (world.Snow == null) world.Snow = new SnowGrid(ctx.Terrain.SizeM, t.F("terrain.snowCellSizeM"), t.F("snow.freshDensityKgM3"));
                if (world.Pistes == null) world.Pistes = new PisteNetwork();
                world.Snow.FreshDensity = t.F("snow.freshDensityKgM3");
                PisteNetworkBuilder.RebuildDerived(ctx);
                world.Snow.MarkAllDirty();
            }
            _hasWeatherSystem = HasWeatherSystem(ctx);
        }

        private static bool HasWeatherSystem(SimContext ctx)
        {
            foreach (var s in ctx.Sim.Systems) if (s.Name == "Weather") return true;
            return false;
        }

        public void Tick(SimContext ctx, float dt)
        {
            var world = ctx.World;
            var grid = world.Snow;
            var time = ctx.Time;

            if (!_hasWeatherSystem && time.IsHourStart) SimpleWeather.Apply(ctx, world.Weather);

            // Amortised weather pass: spread all chunks over the hour. The window is derived from the
            // tick (not kept as instance state) so a loaded save continues bit-identically.
            int chunks = grid.ChunkCount;
            long tickInHour = time.Tick % SimTime.TicksPerHour;
            if (chunks > 0)
            {
                int from = (int)(tickInHour * (long)chunks / SimTime.TicksPerHour);
                int to = (int)((tickInHour + 1) * (long)chunks / SimTime.TicksPerHour);
                for (int c = from; c < to && c < chunks; c++) ProcessChunk(ctx, c, 1f);
            }

            // Background snowpack (outside the envelope), once per hour.
            if (time.IsHourStart) UpdateBackground(ctx, grid, world.Weather.Current);

            // PQI: spread segments over the hour, publish at the top of the hour.
            var net = world.Pistes;
            int segs = net.Segments.Count;
            if (segs > 0)
            {
                int from = (int)(tickInHour * (long)segs / SimTime.TicksPerHour);
                int to = (int)((tickInHour + 1) * (long)segs / SimTime.TicksPerHour);
                for (int i = from; i < to && i < segs; i++)
                {
                    var seg = net.Segments[i];
                    seg.Pqi = PqiCalculator.ComputeSegment(grid, seg, time.Tick, ctx.Tuning);
                    seg.LastPqiTick = time.Tick;
                }
            }
            if (time.IsHourStart) PqiCalculator.PublishAll(ctx);
            if (time.IsDayStart)
            {
                foreach (var p in net.Pistes) p.TrafficToday = 0f;
                foreach (var s in net.Segments) s.TrafficToday = 0f;
            }
        }

        /// <summary>Applies one hour's weather to a chunk. <paramref name="hours"/> scales the effect (tests may call with larger steps).</summary>
        public void ProcessChunk(SimContext ctx, int chunk, float hours)
        {
            var grid = ctx.World.Snow;
            var w = ctx.World.Weather.Current;
            var t = ctx.Tuning;
            int b = chunk * SnowGrid.CellsPerChunk;
            float fresh = grid.FreshDensity;
            float snowMassPerHour = w.SnowfallCmPerHour * 10f * t.F("snow.snowfallDensityKgM3") * 0.001f; // kg/m² per hour
            float rainMm = w.PrecipMmPerHour - w.SnowfallCmPerHour * 10f * t.F("snow.snowfallDensityKgM3") / 1000f; // liquid part
            if (rainMm < 0f) rainMm = 0f;
            float lapse = ctx.World.Weather.LapseRateCPer100m;
            float meltPerDeg = t.F("snow.meltMmPerHourPerDegC");
            float sunMelt = t.F("snow.sunMeltMmPerHour");
            float windScour = t.F("snow.windScourFracPerHourAt50kmh") * (w.WindKmh / 50f) * (w.WindKmh / 50f);
            float settle = t.F("snow.settleFracPerHour");
            float settledDensity = t.F("snow.settledLooseDensity");
            float refreezeRate = t.F("snow.refreezeDensityPerHour");
            float tempRelax = t.F("snow.cellTempRelaxPerHour");
            float saltMelt = t.F("roads.saltMeltMmPerHourPerKgM2");
            float saltDecay = t.F("roads.saltDecayFracPerHour");
            float meltDensity = t.F("snow.meltRunoffDensity");
            bool changed = false;
            for (int i = 0; i < SnowGrid.CellsPerChunk; i++)
            {
                int id = b + i;
                float airT = WetBulb.TempAtElevation(w.TempC, _baseElevation, grid.Elevation[id], lapse);
                // snowfall
                if (snowMassPerHour > 0f)
                {
                    grid.LooseMm[id] += snowMassPerHour * hours / fresh * 1000f;
                    changed = true;
                }
                // cell temperature relaxes toward air temperature (sun adds a little)
                float sun = w.SolarFrac * grid.SunExposure[id];
                float targetT = airT + sun * t.F("snow.sunWarmingC");
                grid.TempC[id] += (targetT - grid.TempC[id]) * MathUtil.Clamp01(tempRelax * hours);
                // melt
                float depth = grid.LooseMm[id] + grid.PackedMm[id];
                if (depth > 0f)
                {
                    float melt = 0f;
                    if (airT > 0f) melt += meltPerDeg * airT * hours;
                    melt += sunMelt * sun * MathUtil.Clamp01((grid.TempC[id] + 4f) / 6f) * hours;
                    if (rainMm > 0f) melt += rainMm * t.F("snow.rainMeltFactor") * hours;
                    if (grid.Salt[id] > 0f && airT > t.F("roads.saltEffectiveMinC")) melt += saltMelt * grid.Salt[id] * hours;
                    if (melt > 0f)
                    {
                        float mass = melt * meltDensity * 0.001f;
                        SnowOps.Remove(grid, id, mass);
                        grid.Moisture[id] = MathUtil.Clamp01(grid.Moisture[id] + melt * t.F("snow.moisturePerMeltMm"));
                        changed = true;
                    }
                    // wind scour of loose snow (mass leaves the envelope: drifts off-piste)
                    if (windScour > 0f && grid.LooseMm[id] > 0f)
                    {
                        float take = grid.LooseMm[id] * MathUtil.Clamp01(windScour * hours);
                        grid.LooseMm[id] -= take;
                        changed = true;
                    }
                    // settling: loose becomes pack at a higher density (mass conserved)
                    if (grid.LooseMm[id] > 0f && settle > 0f)
                    {
                        float mm = grid.LooseMm[id] * MathUtil.Clamp01(settle * hours);
                        float m = mm * fresh * 0.001f;
                        grid.LooseMm[id] -= mm;
                        SnowOps.DepositPacked(grid, id, m, settledDensity);
                        changed = true;
                    }
                    // refreeze: wet snow hardening in the cold
                    if (grid.TempC[id] < t.F("snow.refreezeBelowC") && grid.Moisture[id] > 0.01f && grid.PackedMm[id] > 0f)
                    {
                        float mass = grid.PackedMm[id] * grid.Density[id] * 0.001f;
                        float dd = refreezeRate * grid.Moisture[id] * hours;
                        grid.Density[id] = MathUtil.Clamp(grid.Density[id] + dd, SnowOps.MinDensity, SnowOps.MaxDensity);
                        grid.PackedMm[id] = mass / grid.Density[id] * 1000f;
                        grid.Moisture[id] = MathUtil.Clamp01(grid.Moisture[id] - t.F("snow.moistureFreezePerHour") * hours);
                        changed = true;
                    }
                    else if (grid.TempC[id] < 0f && grid.Moisture[id] > 0f)
                    {
                        grid.Moisture[id] = MathUtil.Clamp01(grid.Moisture[id] - t.F("snow.moistureFreezePerHour") * 0.25f * hours);
                    }
                }
                if (grid.Salt[id] > 0f)
                {
                    grid.Salt[id] *= MathUtil.Clamp01(1f - saltDecay * hours);
                    if (grid.Salt[id] < 1e-4f) grid.Salt[id] = 0f;
                }
            }
            if (changed) grid.MarkChunkDirty(chunk);
        }

        private void UpdateBackground(SimContext ctx, SnowGrid grid, WeatherSample w)
        {
            var t = ctx.Tuning;
            float fresh = grid.FreshDensity;
            if (w.SnowfallCmPerHour > 0f) grid.BackgroundLooseMm += w.SnowfallCmPerHour * 10f * t.F("snow.snowfallDensityKgM3") / fresh;
            if (w.TempC > 0f)
            {
                float melt = t.F("snow.meltMmPerHourPerDegC") * w.TempC;
                float fromLoose = MathF.Min(grid.BackgroundLooseMm, melt);
                grid.BackgroundLooseMm -= fromLoose;
                grid.BackgroundPackedMm = MathF.Max(0f, grid.BackgroundPackedMm - (melt - fromLoose));
            }
            float settle = t.F("snow.settleFracPerHour");
            float mm = grid.BackgroundLooseMm * settle;
            grid.BackgroundLooseMm -= mm;
            grid.BackgroundPackedMm += mm * fresh / t.F("snow.settledLooseDensity");
            ctx.World.Weather.SeasonSnowfallCm += w.SnowfallCmPerHour;
        }
    }

    /// <summary>Scenario-scripted diurnal weather used before the WeatherSystem exists (and by tests).</summary>
    public static class SimpleWeather
    {
        public static void Apply(SimContext ctx, WeatherState state)
        {
            var def = ctx.Sim.Scenario.SimpleWeather;
            var time = ctx.Time;
            var s = state.Current;
            float h = time.HourOfDayF;
            // coldest 06:00, warmest 14:00
            float phase = MathF.Cos((h - 14f) / 24f * MathUtil.TwoPi);
            s.TempC = MathUtil.Lerp(def.NightLowC, def.DayHighC, 0.5f + 0.5f * phase);
            s.HumidityPct = def.HumidityPct;
            s.WindKmh = def.WindKmh;
            s.WindDirDeg = def.WindDirDeg;
            s.CloudFrac = 0.3f;
            s.SnowfallCmPerHour = 0f;
            s.PrecipMmPerHour = 0f;
            s.Lightning = false;
            foreach (var e in def.Script)
            {
                if (e.Day != time.Day) continue;
                if (time.HourOfDay < e.StartHour || time.HourOfDay >= e.StartHour + e.Hours) continue;
                s.SnowfallCmPerHour = e.SnowfallCmPerHour;
                s.PrecipMmPerHour = e.SnowfallCmPerHour * 10f * 0.1f;
                s.TempC = e.TempC;
                s.WindKmh = e.WindKmh;
                s.CloudFrac = 1f;
            }
            float solar = MathF.Max(0f, MathF.Sin((h - 6f) / 12f * MathUtil.Pi));
            s.SolarFrac = solar * (1f - 0.8f * s.CloudFrac);
            s.WetBulbC = WetBulb.FromTempAndHumidity(s.TempC, s.HumidityPct);
        }
    }
}
