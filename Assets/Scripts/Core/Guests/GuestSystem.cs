using System;
using System.Collections.Generic;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Guests
{
    /// <summary>
    /// Guests as cohorts (simulation.guestsPerAgent per agent). Daily demand = f(day of week,
    /// holiday calendar, snow report, weather, PQI trend, ticket price, reputation, regional
    /// competition, act demandBase). Cohorts arrive over the morning, pick a lift, queue, ride,
    /// pick a run from the top node by archetype difficulty preference, ski it (writing
    /// SnowOps.SkierPass into the cells they cross with their lateral preference), buy food and
    /// rentals (posting to the economy), and leave when their stay is over or they are fed up.
    /// Satisfaction weights PQI, queue time, terrain match, comfort/weather exposure, food,
    /// parking (road/lot clearance) and price by archetype. At day close the mean satisfaction
    /// enters a lagging reputation (EMA over economy.reputationLagDays). Ticks after Construction, before Vehicles.
    /// </summary>
    public sealed class GuestSystem : ISimSystem
    {
        public string Name => "Guests";

        public void Initialize(SimContext ctx, bool newGame) => throw new NotImplementedException("M3: GuestSystem.Initialize");
        public void Tick(SimContext ctx, float dt) => throw new NotImplementedException("M3: GuestSystem.Tick");

        public IReadOnlyList<GuestAgent> Agents(SimContext ctx) => ctx.World.Guests.Agents;
        /// <summary>Demand (guest count) for a given day given the current state; used for the day's spawn and the UI forecast.</summary>
        public int ComputeDailyDemand(SimContext ctx, int day) => throw new NotImplementedException("M3");
        public float Reputation(SimContext ctx) => ctx.World.Guests.Reputation;
        public bool IsOpen(SimContext ctx) => throw new NotImplementedException("M3");
        /// <summary>Current mean satisfaction of guests on the mountain (0..1).</summary>
        public float LiveSatisfaction(SimContext ctx) => throw new NotImplementedException("M3");
        /// <summary>Forces the resort closed/open for the day (weather, decision).</summary>
        public void SetResortOpen(SimContext ctx, bool open) => throw new NotImplementedException("M3");
    }
}
