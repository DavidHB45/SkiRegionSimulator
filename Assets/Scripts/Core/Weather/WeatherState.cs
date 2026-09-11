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
        /// <summary>Autocorrelated daily anomalies carried between generated days.</summary>
        public float TempAnomalyC;
        public float WindAnomaly;
        public bool StormYesterday;
        public int StormDaysRun;

        public WeatherSample At(long absoluteHour)
        {
            long i = absoluteHour - TimelineStartHour;
            if (i < 0 || i >= Timeline.Count) return Current;
            return Timeline[(int)i];
        }
    }

    public static class WetBulb
    {
        /// <summary>Psychrometric constant for a ventilated wet bulb (per degC, times pressure in hPa). WMO Guide No. 8, 1 hPa/degC basis.</summary>
        private const double PsychrometricA = 0.000662;

        /// <summary>Saturation vapour pressure over water in hPa (Magnus form, WMO 2008 coefficients).</summary>
        public static float SaturationVapourPressureHpa(float tempC)
            => (float)(6.112 * System.Math.Exp(17.62 * tempC / (243.12 + tempC)));

        /// <summary>ICAO standard-atmosphere pressure at an elevation (hPa): 1013.25 at sea level, about 856 at 1400 m.</summary>
        public static float PressureAtElevationHpa(float elevationM)
            => (float)(1013.25 * System.Math.Pow(1.0 - 2.25577e-5 * elevationM, 5.25588));

        /// <summary>
        /// Wet-bulb temperature by solving the psychrometric equation es(Tw) - A * P * (T - Tw) = e for Tw
        /// (bisection, monotonic). Valid over the full alpine range, unlike the Stull regression which
        /// drifts above the dry-bulb below about -20 C.
        /// </summary>
        public static float FromTempAndHumidity(float tempC, float rhPct, float pressureHpa = 1013.25f)
        {
            float t = System.Math.Max(-45f, System.Math.Min(50f, tempC));
            float rh = System.Math.Max(1f, System.Math.Min(100f, rhPct));
            double e = rh / 100.0 * SaturationVapourPressureHpa(t);
            double lo = t - 45.0, hi = t;
            for (int i = 0; i < 40; i++)
            {
                double mid = (lo + hi) * 0.5;
                double f = SaturationVapourPressureHpa((float)mid) - PsychrometricA * pressureHpa * (t - mid) - e;
                if (f > 0.0) hi = mid; else lo = mid;
            }
            return (float)((lo + hi) * 0.5);
        }

    public static float TempAtElevation(float baseTempC, float baseElevationM, float elevationM, float lapsePer100m)
            => baseTempC - (elevationM - baseElevationM) * 0.01f * lapsePer100m;
    }
}

namespace AlpineSim.Core.Weather
{
    /// <summary>One day of the forecast panel.</summary>
    [System.Serializable]
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
