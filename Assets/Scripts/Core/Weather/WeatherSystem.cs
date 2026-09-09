using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Random;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Weather
{
    /// <summary>
    /// Hourly weather timeline generated per day from the climate profile (climate.json) with
    /// autocorrelated temperature/wind anomalies and persistent storms, all from ctx.Rng.Fork(day)
    /// so a save resumes the same weather. Publishes WeatherState.Current each hour at the reference
    /// elevation (systems use SampleAt for other elevations), keeps a 5-day forecast whose error grows
    /// with lead time, and computes wet-bulb. Ticks first.
    /// </summary>
    public sealed class WeatherSystem : ISimSystem
    {
        public string Name => "Weather";

        public const int ForecastHours = 120;
        private float _baseElevation;

        public void Initialize(SimContext ctx, bool newGame)
        {
            var w = ctx.World.Weather;
            var climate = ctx.Data.Climate;
            _baseElevation = ctx.Terrain.SampleHeight(ctx.Sim.Scenario.BaseArea.Pos);
            w.LapseRateCPer100m = climate.LapseRateCPer100m > 0f ? climate.LapseRateCPer100m : 0.65f;
            if (newGame || w.Timeline.Count == 0)
            {
                w.Timeline.Clear();
                w.TimelineStartHour = ctx.Time.AbsoluteHour - ctx.Time.HourOfDay;
                w.LastGeneratedDay = -1;
                w.TempAnomalyC = 0f; w.WindAnomaly = 0f; w.StormYesterday = false; w.StormDaysRun = 0;
            }
            EnsureThrough(ctx, ctx.Time.Day + 6);
            w.Current = Actual(ctx, ctx.Time.AbsoluteHour).Clone();
            IssueForecast(ctx);
        }

        public void Tick(SimContext ctx, float dt)
        {
            if (!ctx.Time.IsHourStart) return;
            var w = ctx.World.Weather;
            EnsureThrough(ctx, ctx.Time.Day + 6);
            w.Current = Actual(ctx, ctx.Time.AbsoluteHour).Clone();
            ctx.Events.Publish(new WeatherHourEvent { AbsoluteHour = ctx.Time.AbsoluteHour, Sample = w.Current });
            if (ctx.Time.HourOfDay % 6 == 0) IssueForecast(ctx);
            // trim old history (keep two days for the snow report)
            long keepFrom = ctx.Time.AbsoluteHour - 48;
            int drop = (int)System.Math.Min(w.Timeline.Count, System.Math.Max(0, keepFrom - w.TimelineStartHour));
            if (drop > 0) { w.Timeline.RemoveRange(0, drop); w.TimelineStartHour += drop; }
        }

        // ------------------------------------------------------------------ generation
        private void EnsureThrough(SimContext ctx, int day)
        {
            var w = ctx.World.Weather;
            while (w.LastGeneratedDay < day) GenerateDay(ctx, w.LastGeneratedDay + 1);
        }

        private ClimatePeriod PeriodFor(SimContext ctx, int seasonDay, out ClimatePeriod next, out float t)
        {
            var periods = ctx.Data.Climate.Periods;
            next = null; t = 0f;
            if (periods.Count == 0) return new ClimatePeriod();
            ClimatePeriod cur = periods[0];
            for (int i = 0; i < periods.Count; i++)
            {
                if (periods[i].SeasonDayStart <= seasonDay) { cur = periods[i]; next = i + 1 < periods.Count ? periods[i + 1] : null; }
            }
            if (next != null)
            {
                float span = MathF.Max(1f, next.SeasonDayStart - cur.SeasonDayStart);
                t = MathUtil.Clamp01((seasonDay - cur.SeasonDayStart) / span);
            }
            return cur;
        }

        private static float L(float a, float b, float t) => a + (b - a) * t;

        private void GenerateDay(SimContext ctx, int day)
        {
            var w = ctx.World.Weather;
            var c = ctx.Data.Climate;
            int seasonDay = ctx.Time.StartSeasonDay + day;
            var p = PeriodFor(ctx, seasonDay, out var n, out float t);
            if (n == null) { n = p; t = 0f; }
            float meanT = L(p.MeanTempC, n.MeanTempC, t), amp = L(p.DiurnalAmplitudeC, n.DiurnalAmplitudeC, t), sd = L(p.TempStdDevC, n.TempStdDevC, t);
            float precipProb = L(p.PrecipDayProbability, n.PrecipDayProbability, t), precipMean = L(p.PrecipMmPerDayMean, n.PrecipMmPerDayMean, t);
            float snowLine = L(p.SnowLineC, n.SnowLineC, t), windMean = L(p.WindMeanKmh, n.WindMeanKmh, t), gust = L(p.WindGustFactor, n.WindGustFactor, t);
            float windDir = L(p.WindDirMeanDeg, n.WindDirMeanDeg, t), windSpread = L(p.WindDirSpreadDeg, n.WindDirSpreadDeg, t);
            float humMean = L(p.HumidityMeanPct, n.HumidityMeanPct, t), cloudMean = L(p.CloudMean, n.CloudMean, t), lightningP = L(p.LightningProbability, n.LightningProbability, t);

            var rng = ctx.Rng.Fork(1000 + day);
            // AR(1) anomalies
            float rho = MathUtil.Clamp(c.TempAutocorrelation, 0f, 0.98f);
            w.TempAnomalyC = rho * w.TempAnomalyC + MathF.Sqrt(1f - rho * rho) * sd * rng.NextGaussian();
            float rw = MathUtil.Clamp(c.WindAutocorrelation, 0f, 0.98f);
            w.WindAnomaly = rw * w.WindAnomaly + MathF.Sqrt(1f - rw * rw) * 0.45f * rng.NextGaussian();
            // storm persistence
            bool storm = w.StormYesterday ? rng.Chance(c.StormPersistence) : rng.Chance(precipProb);
            if (storm && w.StormDaysRun >= 3 && rng.Chance(0.5f)) storm = false;
            w.StormDaysRun = storm ? w.StormDaysRun + 1 : 0;
            w.StormYesterday = storm;
            int stormStart = rng.Range(0, 24), stormHours = storm ? rng.Range(5, 19) : 0;
            float dayPrecip = storm ? precipMean * MathUtil.Lerp(0.5f, 1.8f, rng.NextFloat()) : 0f;
            float stormWindBoost = storm ? MathUtil.Lerp(1.1f, gust, rng.NextFloat()) : 1f;
            float dayCloud = storm ? 1f : MathUtil.Clamp01(cloudMean + rng.NextGaussian() * 0.25f);
            float dayDir = windDir + rng.NextGaussian() * windSpread;
            float dayHum = storm ? 92f : MathUtil.Clamp(humMean + rng.NextGaussian() * 10f, 20f, 99f);

            for (int h = 0; h < 24; h++)
            {
                var s = new WeatherSample();
                float diurnal = -MathF.Cos((h - 6f) / 24f * MathUtil.TwoPi) * 0.5f; // -0.5 at 06:00, +0.5 at 18:00 → shift peak to 14:00
                float phase = MathF.Cos((h - 14f) / 24f * MathUtil.TwoPi);
                s.TempC = meanT + w.TempAnomalyC + amp * 0.5f * phase + rng.NextGaussian() * 0.4f - (storm ? 1.5f : 0f);
                bool precipHour = storm && ((h - stormStart + 24) % 24) < stormHours;
                s.PrecipMmPerHour = precipHour ? dayPrecip / stormHours * MathUtil.Lerp(0.6f, 1.4f, rng.NextFloat()) : 0f;
                if (precipHour && s.TempC < snowLine)
                {
                    float ratio = MathUtil.Lerp(c.SnowToLiquidRatioWarm, c.SnowToLiquidRatioCold, MathUtil.Clamp01((snowLine - s.TempC) / 10f));
                    s.SnowfallCmPerHour = s.PrecipMmPerHour * ratio / 10f;
                }
                s.WindKmh = MathF.Max(0f, windMean * (1f + w.WindAnomaly) * stormWindBoost * MathUtil.Lerp(0.7f, 1.3f, rng.NextFloat()) * (h >= 11 && h <= 17 ? 1.15f : 0.9f));
                s.WindDirDeg = MathUtil.Repeat(dayDir + rng.NextGaussian() * 12f, 360f);
                s.CloudFrac = MathUtil.Clamp01(dayCloud + rng.NextGaussian() * 0.1f);
                s.HumidityPct = MathUtil.Clamp(dayHum + rng.NextGaussian() * 4f + (precipHour ? 5f : 0f), 15f, 100f);
                s.Lightning = precipHour && rng.Chance(lightningP);
                float solar = MathF.Max(0f, MathF.Sin((h - 6f) / 12f * MathUtil.Pi));
                s.SolarFrac = solar * (1f - 0.85f * s.CloudFrac);
                s.WetBulbC = WetBulb.FromTempAndHumidity(s.TempC, s.HumidityPct);
                w.Timeline.Add(s);
            }
            w.LastGeneratedDay = day;
        }

        public WeatherSample Actual(SimContext ctx, long absoluteHour)
        {
            var w = ctx.World.Weather;
            int day = (int)(absoluteHour / 24);
            EnsureThrough(ctx, day);
            long i = absoluteHour - w.TimelineStartHour;
            if (i < 0) return w.Timeline.Count > 0 ? w.Timeline[0] : w.Current;
            if (i >= w.Timeline.Count) return w.Timeline[w.Timeline.Count - 1];
            return w.Timeline[(int)i];
        }

        public WeatherSample SampleAt(SimContext ctx, float elevationM)
        {
            var w = ctx.World.Weather;
            var s = w.Current.Clone();
            s.TempC = WetBulb.TempAtElevation(w.Current.TempC, _baseElevation, elevationM, w.LapseRateCPer100m);
            s.WindKmh = w.Current.WindKmh * (1f + ctx.Tuning.F("weather.windExposurePer100m") * MathF.Max(0f, elevationM - _baseElevation) / 100f);
            s.WetBulbC = WetBulb.FromTempAndHumidity(s.TempC, s.HumidityPct);
            return s;
        }

        // ------------------------------------------------------------------ forecast
        private void IssueForecast(SimContext ctx)
        {
            var w = ctx.World.Weather;
            var c = ctx.Data.Climate;
            long now = ctx.Time.AbsoluteHour;
            var rng = ctx.Rng.Fork((int)(now * 7 + 99));
            w.Forecast.Clear();
            w.ForecastIssuedHour = now;
            EnsureThrough(ctx, (int)((now + ForecastHours) / 24) + 1);
            // errors drift smoothly with lead time rather than per-hour noise
            float tErr = 0f, wErr = 0f, pErr = 0f;
            for (int lead = 0; lead < ForecastHours; lead++)
            {
                var a = Actual(ctx, now + lead);
                var f = a.Clone();
                float days = lead / 24f;
                tErr = tErr * 0.9f + rng.NextGaussian() * c.ForecastErrorPerDayC * 0.35f;
                wErr = wErr * 0.9f + rng.NextGaussian() * c.ForecastWindErrorPerDayKmh * 0.35f;
                pErr = pErr * 0.9f + rng.NextGaussian() * c.ForecastPrecipErrorPerDay * 0.35f;
                f.TempC += tErr * days;
                f.WindKmh = MathF.Max(0f, f.WindKmh + wErr * days);
                float pScale = MathF.Max(0f, 1f + pErr * days);
                f.PrecipMmPerHour *= pScale;
                f.SnowfallCmPerHour *= pScale;
                if (days > 2f && rng.Chance(MathUtil.Clamp01(c.ForecastPrecipErrorPerDay * (days - 2f)))) { f.SnowfallCmPerHour = f.SnowfallCmPerHour > 0 ? 0f : a.SnowfallCmPerHour * 0.5f; }
                f.WetBulbC = WetBulb.FromTempAndHumidity(f.TempC, f.HumidityPct);
                w.Forecast.Add(f);
            }
            ctx.Events.Publish(new ForecastIssuedEvent { AbsoluteHour = now });
        }

        public WeatherSample Forecast(SimContext ctx, int leadHours)
        {
            var w = ctx.World.Weather;
            long offset = ctx.Time.AbsoluteHour - w.ForecastIssuedHour;
            int i = (int)(offset + leadHours);
            if (w.Forecast.Count == 0) return w.Current;
            i = System.Math.Max(0, System.Math.Min(w.Forecast.Count - 1, i));
            return w.Forecast[i];
        }

        public void DailyForecast(SimContext ctx, int days, List<DailyForecastEntry> into)
        {
            into.Clear();
            var w = ctx.World.Weather;
            int startHour = ctx.Time.HourOfDay;
            for (int d = 0; d < days; d++)
            {
                var e = new DailyForecastEntry { Day = ctx.Time.Day + d, MinC = 99f, MaxC = -99f, MinWetBulbC = 99f, Confidence = MathUtil.Clamp01(1f - d * 0.18f) };
                int h0 = d == 0 ? 0 : d * 24 - startHour;
                int h1 = (d + 1) * 24 - startHour;
                bool any = false;
                for (int lead = System.Math.Max(0, h0); lead < h1 && lead < ForecastHours; lead++)
                {
                    var s = Forecast(ctx, lead);
                    any = true;
                    e.MinC = MathF.Min(e.MinC, s.TempC); e.MaxC = MathF.Max(e.MaxC, s.TempC);
                    e.SnowfallCm += s.SnowfallCmPerHour;
                    e.MaxWindKmh = MathF.Max(e.MaxWindKmh, s.WindKmh);
                    e.MinWetBulbC = MathF.Min(e.MinWetBulbC, s.WetBulbC);
                }
                if (!any) { e.MinC = e.MaxC = w.Current.TempC; e.MinWetBulbC = w.Current.WetBulbC; }
                e.Summary = e.SnowfallCm >= 15f ? "heavy snow" : (e.SnowfallCm >= 4f ? "snow" : (e.MaxWindKmh >= 50f ? "windy" : (e.MaxC > 3f ? "mild" : "clear")));
                into.Add(e);
            }
        }
    }
}
