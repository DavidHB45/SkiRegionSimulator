using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Lifts
{
    /// <summary>
    /// Lift operations: queue simulation with maze/singles line, loading at rated pph (capacity x
    /// options x condition), ride time = length / ride speed, unloading to the top node, wind /
    /// lightning / cold holds per type, MTBF breakdowns with repair downtime, daily pre-open
    /// inspection, periodic and annual inspections, hourly opex/power posting to the economy,
    /// and lift nodes in the piste network. Ticks after Roads, before Construction.
    ///
    /// Throughput contract: with a saturated queue a lift unloads within 5% of its rated pph over
    /// a sim hour (tests check every type). Ride time drives lap rate drives snow wear: halving
    /// ride time on the same runs roughly doubles the traffic guests write into the snow grid.
    /// Hold ordering as wind rises: surface < fixed-grip < detachable < monocable gondola < funitel < 3S/tram.
    /// </summary>
    public sealed class LiftSystem : ISimSystem
    {
        public string Name => "Lifts";

        public void Initialize(SimContext ctx, bool newGame) => throw new NotImplementedException("M3: LiftSystem.Initialize");
        public void Tick(SimContext ctx, float dt) => throw new NotImplementedException("M3: LiftSystem.Tick");

        public IReadOnlyList<LiftState> All(SimContext ctx) => ctx.World.Lifts.Lifts;
        public LiftState Get(SimContext ctx, int id) => ctx.World.Lifts.Get(id);

        /// <summary>
        /// Creates a lift record: computes length/vertical/grade, places towers along the line
        /// (spacing from the type, shifted off NoFoundation cells), registers "lift:&lt;id&gt;:bottom"
        /// and "lift:&lt;id&gt;:top" nodes and ramp zones, and links pistes whose Top/BottomNodeId
        /// name this lift (scenario ids use "lift:&lt;scenarioLiftId&gt;:top"). Prebuilt lifts start Closed;
        /// planned ones start UnderConstruction (M4 drives them to Closed on inspection).
        /// </summary>
        public LiftState Build(SimContext ctx, string typeId, Vec2 bottom, Vec2 top, string name, List<string> options, string scenarioId, bool prebuilt) => throw new NotImplementedException("M3");
        public bool Remove(SimContext ctx, int id) => throw new NotImplementedException("M3");
        public void SetOpen(SimContext ctx, int id, bool open) => throw new NotImplementedException("M3");
        public void SetSinglesLine(SimContext ctx, int id, bool on) => throw new NotImplementedException("M3");

        // ------------------------------------------------------------------ guest interface
        /// <summary>A cohort joins the queue.</summary>
        public void Enqueue(SimContext ctx, int liftId, int agentId, int guests) => throw new NotImplementedException("M3");
        /// <summary>Removes a cohort from the queue (gave up).</summary>
        public bool LeaveQueue(SimContext ctx, int liftId, int agentId) => throw new NotImplementedException("M3");
        /// <summary>Cohorts that reached the top this tick (drained by the guest system).</summary>
        public int DequeueArrivals(SimContext ctx, int liftId, List<RiderCohort> into) => throw new NotImplementedException("M3");
        public int QueueGuests(SimContext ctx, int liftId) => throw new NotImplementedException("M3");
        public float ExpectedWaitMin(SimContext ctx, int liftId) => throw new NotImplementedException("M3");
        public float RideTimeS(SimContext ctx, LiftState lift) => throw new NotImplementedException("M3");
        public float EffectiveCapacityPph(SimContext ctx, LiftState lift) => throw new NotImplementedException("M3");
        /// <summary>Wind hold threshold including options.</summary>
        public float WindHoldKmh(SimContext ctx, LiftState lift) => throw new NotImplementedException("M3");
        /// <summary>Whether the lift would be on hold for the given weather (used by tests and the forecast panel).</summary>
        public bool WouldHold(SimContext ctx, LiftState lift, float windKmh, bool lightning, float tempC, out string reason) => throw new NotImplementedException("M3");
        /// <summary>Lifts whose bottom node is reachable from a base/junction node (for guest planning).</summary>
        public void LiftsFromNode(SimContext ctx, string nodeId, List<LiftState> into) => throw new NotImplementedException("M3");
    }
}
