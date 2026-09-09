using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;

namespace AlpineSim.Core.Data
{
    [Serializable]
    public sealed class PisteDef
    {
        public string Id = "";
        public string Name = "";
        public PisteDifficulty Difficulty = PisteDifficulty.Blue;
        public float WidthM = 40f;
        public List<MapPoint> Points = new List<MapPoint>();
        public string TopNodeId = "";
        public string BottomNodeId = "";
        /// <summary>Act in which the run exists at scenario start (0 = not built yet, buildable later).</summary>
        public int ExistsFromAct = 1;
        public string Comment = "";
    }

    [Serializable]
    public sealed class ZoneDef
    {
        public string Id = "";
        public ZoneKind Kind = ZoneKind.Lot;
        public List<MapPoint> Points = new List<MapPoint>();
        public float WidthM = 6f;
        public float RadiusM = 40f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class InitialSnowDef
    {
        public float PackedMm = 400f;
        public float PackedDensity = 380f;
        public float LooseMm = 60f;
        public float OffPisteLooseMm = 200f;
        public float OffPistePackedMm = 500f;
        public float OffPisteDensity = 220f;
        public float Roughness = 0.35f;
        public float TempC = -6f;
        public string Comment = "";
    }

    /// <summary>Scripted weather used until the full weather system (M5) exists, and for tests.</summary>
    [Serializable]
    public sealed class WeatherScriptEntry
    {
        public int Day;
        public int StartHour;
        public int Hours;
        public float SnowfallCmPerHour;
        public float TempC = -5f;
        public float WindKmh = 10f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class SimpleWeatherDef
    {
        public float DayHighC = -2f;
        public float NightLowC = -12f;
        public float HumidityPct = 70f;
        public float WindKmh = 12f;
        public float WindDirDeg = 270f;
        public List<WeatherScriptEntry> Script = new List<WeatherScriptEntry>();
        public string Comment = "";
    }

    public sealed partial class ScenarioData
    {
        public List<PisteDef> Pistes = new List<PisteDef>();
        public List<ZoneDef> Zones = new List<ZoneDef>();
        public InitialSnowDef InitialSnow = new InitialSnowDef();
        public SimpleWeatherDef SimpleWeather = new SimpleWeatherDef();

        partial void CollectCorridorsM1(List<TerrainCorridor> into)
        {
            foreach (var p in Pistes)
            {
                if (p.Points.Count < 2) continue;
                var pts = new Vec2[p.Points.Count];
                for (int i = 0; i < pts.Length; i++) pts[i] = p.Points[i].Pos;
                into.Add(new TerrainCorridor { Points = pts, HalfWidthM = p.WidthM * 0.5f, BlendM = 12f });
            }
            foreach (var z in Zones)
            {
                if (z.Kind == ZoneKind.Road || z.Kind == ZoneKind.CatTrack)
                {
                    if (z.Points.Count < 2) continue;
                    var pts = new Vec2[z.Points.Count];
                    for (int i = 0; i < pts.Length; i++) pts[i] = z.Points[i].Pos;
                    into.Add(new TerrainCorridor { Points = pts, HalfWidthM = z.WidthM * 0.5f, BlendM = 6f });
                }
                else if (z.Kind == ZoneKind.Lot && z.Points.Count > 0)
                {
                    into.Add(new TerrainCorridor { Points = new[] { z.Points[0].Pos }, HalfWidthM = z.RadiusM, BlendM = 30f, IsDisc = true });
                }
            }
        }
    }
}
