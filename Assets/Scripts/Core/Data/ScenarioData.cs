using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;

namespace AlpineSim.Core.Data
{
    [Serializable]
    public sealed class TerrainParams
    {
        public int SizeM = 2048;
        public float BaseElevationM = 1400f;
        public float RidgeElevationM = 2250f;
        public float RidgeY = 1850f;
        public float RidgeFalloffM = 400f;
        public float BackSlopeDropM = 120f;
        public float LateralAmplitudeM = 60f;
        public float LateralWavelengthM = 700f;
        public float NoiseAmplitudeM = 35f;
        public float NoiseFrequency = 0.0035f;
        public int NoiseOctaves = 5;
        public float NoiseGain = 0.5f;
        public float NoiseLacunarity = 2.05f;
        public float RuggednessWithElevation = 1.2f;
        public int SeedOffset = 0;
        public GorgeParams Gorge = new GorgeParams();
        public List<FlatArea> FlatAreas = new List<FlatArea>();
        public float NoBuildSlopeDeg = 42f;
        /// <summary>Steepest bank a graded corridor leaves at its edge, whatever the depth of cut or fill.</summary>
        public float CorridorBankDeg = 26f;
        /// <summary>Deepest cut or fill a run's grading may make; beyond that the run follows the mountain.</summary>
        public float PisteEarthworkM = 5f;
        /// <summary>Deepest cut or fill for a cat track.</summary>
        public float TrackEarthworkM = 8f;
        /// <summary>Deepest cut or fill for an access road.</summary>
        public float RoadEarthworkM = 12f;
        /// <summary>The base pad keeps this fraction of the ground's mean gradient instead of being dead level.</summary>
        public float BaseAreaTiltFrac = 0.35f;
        /// <summary>Bank angle around parking lots: cars and pickups have to be able to drive off the edge of a lot.</summary>
        public float LotBankDeg = 12f;
    }

    [Serializable]
    public sealed class GorgeParams
    {
        public bool Enabled = true;
        public float Y = 1150f;
        public float XStart = 1250f;
        public float XEnd = 2047f;
        public float WidthM = 150f;
        public float DepthM = 190f;
        public float NoBuildDepthM = 25f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class FlatArea
    {
        public string Id = "";
        public float X;
        public float Y;
        public float RadiusM = 100f;
        public float ElevationM = 1400f;
        public float BlendM = 60f;
        public string Comment = "";
    }

    /// <summary>A named point on the map (base lodge, lot, hydrant, tower site...).</summary>
    [Serializable]
    public sealed class MapPoint
    {
        public string Id = "";
        public float X;
        public float Y;
        public Vec2 Pos => new Vec2(X, Y);
    }

    /// <summary>
    /// Scenario definition (scenarios.json). Milestone partial files extend it with pistes, lifts,
    /// starting fleet, hydrants and so on; unknown JSON members are ignored so the data file may be
    /// complete before every consumer exists.
    /// </summary>
    /// <summary>A corridor (or disc) the terrain generator benches so machines and guests get legible surfaces.</summary>
    public sealed class TerrainCorridor
    {
        public Vec2[] Points;
        public float HalfWidthM;
        public float BlendM;
        public bool IsDisc;
        /// <summary>Cut-and-fill grade limit along the centreline (0 = natural profile).</summary>
        public float MaxGradeDeg;
        /// <summary>Vertices that tie into another corridor (run bottoms, the base): grading never moves them.</summary>
        public bool[] Pinned;
        /// <summary>Deepest cut or fill grading may make (0 = unbounded): the profile follows the ground within this.</summary>
        public float MaxEarthworkM;
    }

    [Serializable]
    public sealed partial class ScenarioData
    {
        /// <summary>Corridors contributed by milestone partials (pistes, roads, lots) in a fixed order.</summary>
        public List<TerrainCorridor> GetCorridors()
        {
            var list = new List<TerrainCorridor>();
            CollectCorridorsM1(list);
            return list;
        }

        partial void CollectCorridorsM1(List<TerrainCorridor> into);

        public string Id = "default";
        public string DisplayName = "";
        public string Description = "";
        public int StartHour = 18;
        public int StartDayOfWeek = 4;
        public int StartSeasonDay = 0;
        public float StartCash = 250000f;
        public int StartingAct = 1;
        public TerrainParams Terrain = new TerrainParams();
        public MapPoint BaseArea = new MapPoint { Id = "base", X = 1024f, Y = 300f };
        public float BaseAreaRadiusM = 140f;
        public List<MapPoint> Landmarks = new List<MapPoint>();
        public string Comment = "";
    }
}
