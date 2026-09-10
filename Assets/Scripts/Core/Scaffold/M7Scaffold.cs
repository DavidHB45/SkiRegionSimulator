using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Scaffold
{
    // ------------------------------------------------------------------------------------------
    // M7 scaffold: winch cats, terrain park shaping, avalanche control.
    // Interfaces and data schemas only. Every behaviour throws NotImplementedException so nothing
    // half-wired can leak into play; the system ticks as a no-op so the tick order is already final.
    // See docs/BUILD_ORDER.md (M7) for the intended design and kill criteria.
    // ------------------------------------------------------------------------------------------

    /// <summary>A fixed winch anchor (winch.json): a bollard or tower a winch cat can hook to.</summary>
    [Serializable]
    public sealed class WinchAnchorDef
    {
        public string Id = "";
        public float X;
        public float Y;
        public float RatedPullKn = 60f;
        public double InstallCost = 9000;
        public string Comment = "";
        public Vec2 Pos => new Vec2(X, Y);
    }

    /// <summary>A shapeable park feature (park.json): kicker, rail, box, pipe.</summary>
    [Serializable]
    public sealed class ParkFeatureDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>kicker | tabletop | rail | box | halfpipe | quarterpipe | rollers</summary>
        public string Kind = "kicker";
        public float LengthM = 12f;
        public float WidthM = 6f;
        public float HeightM = 1.5f;
        public float SnowM3 = 180f;
        public float ShapeHours = 3f;
        public float RebuildDays = 5f;
        public float GuestAppeal = 1f;
        public string Comment = "";
    }

    /// <summary>An avalanche path (avalanche.json): start zone, track and runout with hazard parameters.</summary>
    [Serializable]
    public sealed class AvalanchePathDef
    {
        public string Id = "";
        public string DisplayName = "";
        public List<Vec2> StartZone = new List<Vec2>();
        public List<Vec2> Runout = new List<Vec2>();
        public float SlopeDeg = 36f;
        public float AspectDeg = 20f;
        public float LoadingSnowCmPerDay = 30f;
        public string ThreatensPisteId = "";
        public string ThreatensRoadId = "";
        public string Comment = "";
    }

    /// <summary>Winch cat operations: hooking to an anchor, rope tension and the pull-assisted climb model.</summary>
    public interface IWinchOperations
    {
        IReadOnlyList<WinchAnchorDef> Anchors(SimContext ctx);
        bool Hook(SimContext ctx, int vehicleId, string anchorId, out string reason);
        void Release(SimContext ctx, int vehicleId);
        float RopeTensionKn(SimContext ctx, int vehicleId);
        float PullAssistFactor(SimContext ctx, int vehicleId, float gradeDeg);
    }

    /// <summary>Terrain park shaping: build features from pushed snow, wear them down under traffic, rebuild.</summary>
    public interface IParkShaping
    {
        IReadOnlyList<ParkFeatureDef> Catalogue(SimContext ctx);
        int Build(SimContext ctx, string featureId, Vec2 pos, float headingDeg, out string reason);
        float FeatureCondition(SimContext ctx, int featureInstanceId);
        void Remove(SimContext ctx, int featureInstanceId);
    }

    /// <summary>Avalanche control: hazard rating, closures and explosive or gas control missions.</summary>
    public interface IAvalancheControl
    {
        IReadOnlyList<AvalanchePathDef> Paths(SimContext ctx);
        int HazardRating(SimContext ctx, string pathId);
        bool ScheduleControl(SimContext ctx, string pathId, out string reason);
        bool IsClosed(SimContext ctx, string pathId);
    }

    /// <summary>
    /// Registered under the "Scaffold" tick slot. Ticks as a no-op; every operation throws until M7 is built.
    /// </summary>
    public sealed class M7ScaffoldSystem : ISimSystem, IWinchOperations, IParkShaping, IAvalancheControl
    {
        public string Name => "Scaffold";
        public const string NotBuilt = "M7 (winch cats, terrain park, avalanche control) is scaffolded but not implemented; see docs/BUILD_ORDER.md";

        public void Initialize(SimContext ctx, bool newGame) { }
        public void Tick(SimContext ctx, float dt) { }

        public IReadOnlyList<WinchAnchorDef> Anchors(SimContext ctx) => ctx.Data.WinchAnchors;
        public bool Hook(SimContext ctx, int vehicleId, string anchorId, out string reason) => throw new NotImplementedException(NotBuilt);
        public void Release(SimContext ctx, int vehicleId) => throw new NotImplementedException(NotBuilt);
        public float RopeTensionKn(SimContext ctx, int vehicleId) => throw new NotImplementedException(NotBuilt);
        public float PullAssistFactor(SimContext ctx, int vehicleId, float gradeDeg) => throw new NotImplementedException(NotBuilt);

        public IReadOnlyList<ParkFeatureDef> Catalogue(SimContext ctx) => ctx.Data.ParkFeatures;
        public int Build(SimContext ctx, string featureId, Vec2 pos, float headingDeg, out string reason) => throw new NotImplementedException(NotBuilt);
        public float FeatureCondition(SimContext ctx, int featureInstanceId) => throw new NotImplementedException(NotBuilt);
        public void Remove(SimContext ctx, int featureInstanceId) => throw new NotImplementedException(NotBuilt);

        public IReadOnlyList<AvalanchePathDef> Paths(SimContext ctx) => ctx.Data.AvalanchePaths;
        public int HazardRating(SimContext ctx, string pathId) => throw new NotImplementedException(NotBuilt);
        public bool ScheduleControl(SimContext ctx, string pathId, out string reason) => throw new NotImplementedException(NotBuilt);
        public bool IsClosed(SimContext ctx, string pathId) => throw new NotImplementedException(NotBuilt);
    }
}
