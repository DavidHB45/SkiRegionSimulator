using System;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Weather
{
    /// <summary>
    /// Hourly weather timeline generated per day from the climate profile (climate.json) with
    /// autocorrelated temperature/wind anomalies and persistent storms, all from ctx.Rng.Fork(day)
    /// so a save resumes the same weather. Publishes WeatherState.Current each hour (adjusted to the
    /// reference elevation; systems use SampleAt for other elevations), keeps a 5-day forecast whose
    /// error grows with lead time, and computes wet-bulb. Ticks first.
    /// </summary>
    public sealed class WeatherSystem : ISimSystem
    {
        public string Name => "Weather";

        public void Initialize(SimContext ctx, bool newGame) => throw new NotImplementedException("M5: WeatherSystem.Initialize");
        public void Tick(SimContext ctx, float dt) => throw new NotImplementedException("M5: WeatherSystem.Tick");

        /// <summary>Current conditions at an elevation (temperature lapse, wind exposure with height).</summary>
        public WeatherSample SampleAt(SimContext ctx, float elevationM) => throw new NotImplementedException("M5");
        /// <summary>Ensures the timeline covers absolute hour h (generates whole days as needed).</summary>
        public WeatherSample Actual(SimContext ctx, long absoluteHour) => throw new NotImplementedException("M5");
        /// <summary>The forecast the player sees for a lead time in hours (noisy).</summary>
        public WeatherSample Forecast(SimContext ctx, int leadHours) => throw new NotImplementedException("M5");
        /// <summary>Forecast summary per day for the forecast panel: min/max temp, snowfall cm, max wind, confidence 0..1.</summary>
        public void DailyForecast(SimContext ctx, int days, System.Collections.Generic.List<DailyForecastEntry> into) => throw new NotImplementedException("M5");
    }

    [Serializable]
    public sealed class DailyForecastEntry
    {
        public int Day;
        public float MinC;
        public float MaxC;
        public float SnowfallCm;
        public float MaxWindKmh;
        public float MinWetBulbC;
        public float Confidence;
        public string Summary = "";
    }

    public struct WeatherHourEvent { public long AbsoluteHour; public WeatherSample Sample; }
    public struct ForecastIssuedEvent { public long AbsoluteHour; }
}
