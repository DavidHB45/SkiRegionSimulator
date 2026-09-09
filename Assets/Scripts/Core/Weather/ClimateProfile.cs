using System;
using System.Collections.Generic;

namespace AlpineSim.Core.Weather
{
    /// <summary>Climate statistics for a slice of the season (climate.json). The generator interpolates between periods.</summary>
    [Serializable]
    public sealed class ClimatePeriod
    {
        public int SeasonDayStart;
        public string Label = "";
        public float MeanTempC = -5f;
        public float DiurnalAmplitudeC = 6f;
        public float TempStdDevC = 4f;
        public float PrecipDayProbability = 0.3f;
        public float PrecipMmPerDayMean = 8f;
        public float SnowLineC = 1.5f;
        public float WindMeanKmh = 15f;
        public float WindGustFactor = 1.8f;
        public float WindDirMeanDeg = 280f;
        public float WindDirSpreadDeg = 40f;
        public float HumidityMeanPct = 70f;
        public float CloudMean = 0.45f;
        public float LightningProbability = 0.01f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class ClimateProfile
    {
        public string Id = "alpine";
        public string DisplayName = "";
        public float ReferenceElevationM = 1400f;
        public float LapseRateCPer100m = 0.65f;
        /// <summary>Lag-1 autocorrelation of the daily temperature anomaly (0..1).</summary>
        public float TempAutocorrelation = 0.75f;
        public float WindAutocorrelation = 0.6f;
        public float StormPersistence = 0.55f;
        public int SeasonLengthDays = 150;
        public float ForecastErrorPerDayC = 1.4f;
        public float ForecastPrecipErrorPerDay = 0.15f;
        public float ForecastWindErrorPerDayKmh = 6f;
        public float SnowToLiquidRatioCold = 14f;
        public float SnowToLiquidRatioWarm = 8f;
        public List<ClimatePeriod> Periods = new List<ClimatePeriod>();
        public string Comment = "";
    }
}
