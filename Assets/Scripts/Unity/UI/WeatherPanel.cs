using System.Collections.Generic;
using AlpineSim.Core.Weather;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>Current conditions at base and summit, the next 24 hours, and the five-day forecast with its confidence.</summary>
    public sealed class WeatherPanel : UiPanel
    {
        private Text _now;
        private RectTransform _hourly;
        private RectTransform _daily;
        private ScrollRect _scrollA, _scrollB;
        private float _nextRefresh;
        private readonly List<DailyForecastEntry> _entries = new List<DailyForecastEntry>();

        public WeatherPanel() : base("Weather & Forecast") { }

        protected override void BuildBody(RectTransform body)
        {
            _now = UiFactory.Label("Now", body, "", 13);
            var split = UiFactory.CreateRect("Split", body);
            split.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            UiFactory.HorizontalRow(split, 6f);
            _hourly = UiFactory.ScrollView("Hourly", split, out _scrollA);
            _daily = UiFactory.ScrollView("Daily", split, out _scrollB);
        }

        public override void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 2f;
            var ctx = Boot.Sim.Ctx;
            if (!Boot.Sim.TryGetSystem<WeatherSystem>(out var ws)) return;
            var scen = Boot.Sim.Scenario;
            var w = Boot.Sim.World.Weather;
            var baseS = ws.SampleAt(ctx, scen.Terrain.BaseElevationM);
            var topS = ws.SampleAt(ctx, scen.Terrain.RidgeElevationM);
            _now.text = "<b>Base " + UiFactory.F(scen.Terrain.BaseElevationM, 0) + " m:</b> " + Describe(baseS) + "\n<b>Summit " + UiFactory.F(scen.Terrain.RidgeElevationM, 0) + " m:</b> " + Describe(topS)
                + "\nseason snowfall " + UiFactory.F(w.SeasonSnowfallCm, 0) + " cm   forecast issued " + (w.ForecastIssuedHour >= 0 ? (ctx.Time.AbsoluteHour - w.ForecastIssuedHour) + " h ago" : "never") + "   snowmaking window " + (baseS.WetBulbC <= Boot.Data.Tuning.F("snowmaking.wetBulbMaxC") ? UiFactory.ColorTag(UiFactory.Good, "OPEN at base") : UiFactory.ColorTag(UiFactory.Warn, "closed at base")) + (topS.WetBulbC <= Boot.Data.Tuning.F("snowmaking.wetBulbMaxC") ? UiFactory.ColorTag(UiFactory.Good, ", open at summit") : UiFactory.ColorTag(UiFactory.Warn, ", closed at summit"));

            ClearChildren(_hourly);
            UiFactory.Label("H", _hourly, "<b>Next 24 hours (forecast, base)</b>", 12);
            var header = UiFactory.Row("hh", _hourly, 18f);
            UiFactory.RowLabel(header, "hour", 40f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "temp", 50f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "wet-bulb", 60f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "wind", 60f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "snow", 60f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "cloud", 50f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            for (int h = 1; h <= 24; h++)
            {
                var f = ws.Forecast(ctx, h);
                var row = UiFactory.Row("h" + h, _hourly, 17f);
                UiFactory.RowLabel(row, ((ctx.Time.HourOfDay + h) % 24).ToString("00") + "h", 40f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, UiFactory.F(f.TempC, 1), 50f, TextAnchor.MiddleRight, 11);
                UiFactory.RowLabel(row, WetBulbTag(f.WetBulbC), 60f, TextAnchor.MiddleRight, 11);
                UiFactory.RowLabel(row, UiFactory.F(f.WindKmh, 0) + " km/h", 60f, TextAnchor.MiddleRight, 11);
                UiFactory.RowLabel(row, f.SnowfallCmPerHour > 0.05f ? UiFactory.ColorTag(UiFactory.Accent, UiFactory.F(f.SnowfallCmPerHour, 1) + " cm") : (f.PrecipMmPerHour > 0.05f ? UiFactory.ColorTag(UiFactory.Warn, "rain") : "-"), 60f, TextAnchor.MiddleRight, 11);
                UiFactory.RowLabel(row, UiFactory.F(f.CloudFrac * 100f, 0) + "%", 50f, TextAnchor.MiddleRight, 11);
            }

            ClearChildren(_daily);
            UiFactory.Label("D", _daily, "<b>Five-day outlook</b>  confidence falls with lead time; plan snowmaking on the wet-bulb column", 12);
            ws.DailyForecast(ctx, 5, _entries);
            foreach (var e in _entries)
            {
                var row = UiFactory.Row("d" + e.Day, _daily, 20f);
                UiFactory.RowLabel(row, "Day " + (e.Day + 1), 60f, TextAnchor.MiddleLeft, 12);
                UiFactory.RowLabel(row, UiFactory.F(e.MinC, 0) + " / " + UiFactory.F(e.MaxC, 0) + " C", 90f, TextAnchor.MiddleLeft, 12);
                UiFactory.RowLabel(row, "wb " + WetBulbTag(e.MinWetBulbC), 70f, TextAnchor.MiddleLeft, 12);
                UiFactory.RowLabel(row, e.SnowfallCm > 0.5f ? UiFactory.ColorTag(UiFactory.Accent, UiFactory.F(e.SnowfallCm, 0) + " cm") : "dry", 60f, TextAnchor.MiddleLeft, 12);
                UiFactory.RowLabel(row, "gusts " + UiFactory.F(e.MaxWindKmh, 0), 80f, TextAnchor.MiddleLeft, 12);
                UiFactory.RowLabel(row, UiFactory.ColorTag(e.Confidence > 0.6f ? UiFactory.Good : (e.Confidence > 0.3f ? UiFactory.Warn : UiFactory.Bad), UiFactory.F(e.Confidence * 100f, 0) + "%"), 50f, TextAnchor.MiddleLeft, 12);
                UiFactory.FlexLabel(row, e.Summary, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            }
        }

        private static string Describe(WeatherSample s)
        {
            return UiFactory.F(s.TempC, 1) + " C, wet-bulb " + WetBulbTag(s.WetBulbC) + ", RH " + UiFactory.F(s.HumidityPct, 0) + "%, wind " + UiFactory.F(s.WindKmh, 0) + " km/h from " + Compass(s.WindDirDeg)
                + ", cloud " + UiFactory.F(s.CloudFrac * 100f, 0) + "%" + (s.SnowfallCmPerHour > 0.05f ? ", snowing " + UiFactory.F(s.SnowfallCmPerHour, 1) + " cm/h" : (s.PrecipMmPerHour > 0.05f ? ", rain " + UiFactory.F(s.PrecipMmPerHour, 1) + " mm/h" : ""))
                + (s.Lightning ? UiFactory.ColorTag(UiFactory.Bad, ", LIGHTNING") : "");
        }

        private static string WetBulbTag(float wb)
        {
            Color c = wb <= -6f ? UiFactory.Good : (wb <= -2f ? UiFactory.Warn : UiFactory.Bad);
            return UiFactory.ColorTag(c, UiFactory.F(wb, 1));
        }

        private static string Compass(float deg)
        {
            string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return names[Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 45f) % 8];
        }
    }
}
