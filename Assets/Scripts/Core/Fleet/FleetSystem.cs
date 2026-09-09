using System;
using System.Collections.Generic;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Fleet
{
    /// <summary>
    /// The garage. Acquisition (new, used with randomised hours/condition and hidden defects revealed by
    /// inspection, lease monthly, rent by the day, trade-in against the resale curve), operators
    /// (hire from weekly candidates, fire, train licences, assign to machines, wages per shift,
    /// competence, slope limits, accidents when unlicensed), service (interval PM vs unscheduled
    /// failure repair, workshop tiers deciding in-house vs sent out, parts inventory and lead
    /// times, rebuilds), fuel logistics (depot stock, deliveries with lead time and price
    /// volatility, contracts, the service truck dispatched to stranded machines as a Refuel/Repair
    /// task), sheltering (garage bays: unsheltered machines cold-start slower and wear faster),
    /// insurance and leases posted daily/monthly, fleet valuation. Ticks after Tasks, before Economy.
    /// </summary>
    public sealed class FleetSystem : ISimSystem
    {
        public string Name => "Fleet";

        public void Initialize(SimContext ctx, bool newGame) => throw new NotImplementedException("M6: FleetSystem.Initialize");
        public void Tick(SimContext ctx, float dt) => throw new NotImplementedException("M6: FleetSystem.Tick");

        // ------------------------------------------------------------------ market
        public IReadOnlyList<MarketListing> Market(SimContext ctx) => ctx.World.Fleet.Market;
        public void RefreshMarket(SimContext ctx) => throw new NotImplementedException("M6");
        public VehicleState BuyNew(SimContext ctx, string defId, out string reason) => throw new NotImplementedException("M6");
        public VehicleState BuyListing(SimContext ctx, int listingId, out string reason) => throw new NotImplementedException("M6");
        public VehicleState Lease(SimContext ctx, string defId, int termMonths, out string reason) => throw new NotImplementedException("M6");
        public VehicleState Rent(SimContext ctx, string defId, int days, out string reason) => throw new NotImplementedException("M6");
        /// <summary>Reveals hidden defects on a used listing for a fee.</summary>
        public bool Inspect(SimContext ctx, int listingId, out string reason) => throw new NotImplementedException("M6");
        public double ResaleValue(SimContext ctx, VehicleState v) => throw new NotImplementedException("M6");
        public bool Sell(SimContext ctx, int vehicleId, out string reason) => throw new NotImplementedException("M6");
        /// <summary>Trade a machine in against a new purchase.</summary>
        public VehicleState TradeIn(SimContext ctx, int vehicleId, string newDefId, out string reason) => throw new NotImplementedException("M6");
        public bool BuyAttachment(SimContext ctx, string attachmentId, out string reason) => throw new NotImplementedException("M6");
        public IReadOnlyList<string> OwnedAttachments(SimContext ctx) => throw new NotImplementedException("M6");

        // ------------------------------------------------------------------ operators
        public IReadOnlyList<OperatorState> Operators(SimContext ctx) => ctx.World.Fleet.Operators;
        public IReadOnlyList<OperatorState> Candidates(SimContext ctx) => ctx.World.Fleet.Candidates;
        public OperatorState Hire(SimContext ctx, int candidateId, out string reason) => throw new NotImplementedException("M6");
        public bool Fire(SimContext ctx, int operatorId, out string reason) => throw new NotImplementedException("M6");
        public bool Train(SimContext ctx, int operatorId, OperatorLicense license, out string reason) => throw new NotImplementedException("M6");
        /// <summary>Puts an operator in a machine (checks licence; unlicensed assignment is allowed but flagged and risky).</summary>
        public bool AssignOperator(SimContext ctx, int operatorId, int vehicleId, out string reason) => throw new NotImplementedException("M6");
        public void UnassignOperator(SimContext ctx, int operatorId) => throw new NotImplementedException("M6");
        public bool OperatorLicensedFor(SimContext ctx, OperatorState op, VehicleDef def) => throw new NotImplementedException("M6");

        // ------------------------------------------------------------------ service
        public IReadOnlyList<ServiceJob> Jobs(SimContext ctx) => ctx.World.Fleet.Workshop.Jobs;
        public ServiceJob SchedulePm(SimContext ctx, int vehicleId, out string reason) => throw new NotImplementedException("M6");
        public ServiceJob RequestRepair(SimContext ctx, int vehicleId, out string reason) => throw new NotImplementedException("M6");
        public ServiceJob RequestRebuild(SimContext ctx, int vehicleId, out string reason) => throw new NotImplementedException("M6");
        /// <summary>Mount/dismount through the shop: takes the attachment's mount minutes of bay time.</summary>
        public ServiceJob RequestMount(SimContext ctx, int vehicleId, string attachmentId, SlotPosition slot, out string reason) => throw new NotImplementedException("M6");
        public ServiceJob RequestDismount(SimContext ctx, int vehicleId, SlotPosition slot, out string reason) => throw new NotImplementedException("M6");
        public bool CancelJob(SimContext ctx, int jobId) => throw new NotImplementedException("M6");
        public bool OrderParts(SimContext ctx, string kind, int qty, out string reason) => throw new NotImplementedException("M6");
        public bool UpgradeWorkshop(SimContext ctx, out string reason) => throw new NotImplementedException("M6");
        public bool BuyGarageBays(SimContext ctx, out string reason) => throw new NotImplementedException("M6");
        public bool BuildWashBay(SimContext ctx, out string reason) => throw new NotImplementedException("M6");
        public float HoursToService(SimContext ctx, VehicleState v) => throw new NotImplementedException("M6");

        // ------------------------------------------------------------------ fuel logistics
        public bool OrderFuel(SimContext ctx, float liters, bool contract, out string reason) => throw new NotImplementedException("M6");
        public bool SignFuelContract(SimContext ctx, out string reason) => throw new NotImplementedException("M6");
        /// <summary>Refuels a machine from the depot when it is within the fuel landmark radius.</summary>
        public float RefuelAtDepot(SimContext ctx, int vehicleId, out string reason) => throw new NotImplementedException("M6");
        /// <summary>Opens a service call for a stranded/failed machine and creates the Refuel/Repair task for a service truck.</summary>
        public ServiceCall RequestServiceCall(SimContext ctx, int vehicleId, string reason) => throw new NotImplementedException("M6");
        public bool ExpandFuelDepot(SimContext ctx, out string reason) => throw new NotImplementedException("M6");
        public bool BuyStation(SimContext ctx, string vehicleDefId, out string reason) => throw new NotImplementedException("M6");
    }
}
