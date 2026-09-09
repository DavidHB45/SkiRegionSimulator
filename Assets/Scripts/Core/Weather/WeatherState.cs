using System;
using System.Collections.Generic;

namespace AlpineSim.Core.Weather
{
    /// <summary>One hour of weather at the resort reference elevation (base area).</summary>
    [Serializable]
    public sealed class WeatherSample
    {
        public float TempC;
        public float HumidityPct = 70f;
        public float WindKmh;
        public float WindDirDeg;      // meteorological: direction the wind blows FROM, 0 = north, 90 = east
        public float CloudFrac;
        public float PrecipMmPerHour; // water equivalent
        public float SnowfallCmPerHour;
        public bool Lightning;
        public float WetBulbC;
        public float SolarFrac;       // 0..1 clear-sky solar irradiance factor for the hour

        public WeatherSample Clone() => (WeatherSample)MemberwiseClone();
    }

    /// <summary>
    /// Persisted weather. M1 fills Current from the scenario's simple diurnal model; M5's WeatherSystem
    /// generates the hourly timeline and the degrading forecast.
    /// </summary>
    [Serializable]
    public sealed class WeatherState
    {
        public WeatherSample Current = new WeatherSample();
        /// <summary>Generated hours, indexed by (absoluteHour - TimelineStartHour).</summary>
        public List<WeatherSample> Timeline = new List<WeatherSample>();
        public long TimelineStartHour;
        /// <summary>Forecast for the next 120 hours as the player sees it (with lead-time error).</summary>
        public List<WeatherSample> Forecast = new List<WeatherSample>();
        public long ForecastIssuedHour = -1;
        public float SeasonSnowfallCm;
        public float LapseRateCPer100m = 0.65f;
        public int LastGeneratedDay = -1;

        public WeatherSample At(long absoluteHour)
        {
            long i = absoluteHour - TimelineStartHour;
            if (i < 0 || i >= Timeline.Count) return Current;
            return Timeline[(int)i];
        }
    }

    public static class WetBulb
    {
        /// <summary>
        /// Wet-bulb temperature (°C) from dry-bulb temperature and relative humidity, Stull (2011).
        /// Valid roughly -20..50 °C, 5..99 % RH; clamped inputs. Snowmaking gates on this value.
        /// </summary>
        public static float FromTempAndHumidity(float tempC, float rhPct)
        {
            float t = System.Math.Max(-40f, System.Math.Min(50f, tempC));
            float rh = System.Math.Max(1f, System.Math.Min(100f, rhPct));
            double tw = t * System.Math.Atan(0.151977 * System.Math.Sqrt(rh + 8.313659))
                        + System.Math.Atan(t + rh) - System.Math.Atan(rh - 1.676331)
                        + 0.00391838 * System.Math.Pow(rh, 1.5) * System.Math.Atan(0.023101 * rh)
                        - 4.686035;
            return (float)tw;
        }

        /// <summary>Air temperature adjusted for elevation with a lapse rate (°C per 100 m).</summary>
        public static float TempAtElevation(float baseTempC, float baseElevationM, float elevationM, float lapsePer100m)
            => baseTempC - (elevationM - baseElevationM) * 0.01f * lapsePer100m;
    }
}
