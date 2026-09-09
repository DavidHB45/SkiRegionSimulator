using System;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Roads
{
    /// <summary>
    /// Access road, parking lots and lift ramps. Computes each zone's ClearanceScore hourly from
    /// snow depth, salt and ice on its cells (guests' parking satisfaction and arrival throughput
    /// read it), auto-generates PlowRoad / ClearLot / Spread / BlowRamp tasks when thresholds in
    /// tuning roads.* are crossed, and applies plow/spreader effects reported by the vehicle
    /// system. Ticks after Snow, before Lifts.
    /// </summary>
    public sealed class RoadSystem : ISimSystem
    {
        public string Name => "Roads";

        public void Initialize(SimContext ctx, bool newGame) => throw new NotImplementedException("M6: RoadSystem.Initialize");
        public void Tick(SimContext ctx, float dt) => throw new NotImplementedException("M6: RoadSystem.Tick");

        public float Clearance(SimContext ctx, SurfaceZone zone) => zone.ClearanceScore;
        /// <summary>0..1 access factor for guest arrivals (road clearance) and parking (lots).</summary>
        public float AccessFactor(SimContext ctx) => throw new NotImplementedException("M6");
        public float ParkingFactor(SimContext ctx) => throw new NotImplementedException("M6");
        /// <summary>Whether a lift's loading ramp is drifted in (blocks opening until blown out).</summary>
        public bool RampBlocked(SimContext ctx, int liftId) => throw new NotImplementedException("M6");
    }
}
