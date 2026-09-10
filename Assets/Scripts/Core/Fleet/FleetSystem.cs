using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Economy;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Tasks;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Fleet
{
    /// <summary>
    /// The garage. Acquisition (new, used with randomised hours/condition and hidden defects revealed
    /// by inspection, lease monthly, rent by the day, trade-in against the resale curve), operators
    /// (weekly candidates, hire/fire, licence training, assignment, wages per shift, competence,
    /// slope limits, accidents when unlicensed), service (interval PM vs unscheduled repair, workshop
    /// tiers deciding in-house vs sent out, parts inventory with lead times, rebuilds, mount/dismount
    /// through the shop), fuel logistics (depot stock, deliveries with lead time and price
    /// volatility, contracts, service truck refuel/repair tasks for stranded machines), sheltering
    /// (garage bays), insurance and leases, fleet valuation. Ticks after Tasks, before Economy.
    /// </summary>
    public sealed class FleetSystem : ISimSystem
    {
        public string Name => "Fleet";

        private Action<VehicleStrandedEvent> _onStranded;
        private Action<VehicleFailureEvent> _onFailure;
        private Action<TaskCompletedEvent> _onTask;

        public void Initialize(SimContext ctx, bool newGame)
        {
            var f = ctx.World.Fleet;
            var scen = ctx.Sim.Scenario;
            var stations = ctx.Data.Stations;
            var vs = ctx.System<VehicleSystem>();
            if (newGame)
            {
                f.Workshop.Tier = System.Math.Max(1, scen.StartWorkshopTier);
                f.Workshop.GarageBays = System.Math.Max(0, scen.StartGarageBays);
                f.Fuel.DieselCapacityL = stations.FuelDepot.DieselCapacityL;
                f.Fuel.GasolineCapacityL = stations.FuelDepot.GasolineCapacityL;
                f.Fuel.DieselL = MathF.Min(scen.StartDieselL, f.Fuel.DieselCapacityL);
                f.Fuel.GasolineL = MathF.Min(scen.StartGasolineL, f.Fuel.GasolineCapacityL);
                f.Fuel.DieselPricePerL = stations.FuelDepot.BasePricePerL;
                f.Fuel.GasolinePricePerL = ctx.Tuning.F("fuel.gasolinePricePerL");
                foreach (var kv in scen.StartParts) f.Parts.Stock[kv.Key] = kv.Value;
                foreach (var a in scen.StartingAttachments) if (ctx.Data.Attachment(a) != null) f.OwnedAttachments.Add(a);
                // the player is operator 0 with every licence the scenario grants at Act I
                f.Operators.Add(new OperatorState { Id = f.NextOperatorId++, Name = "You", Competence = 0.85f, WagePerHour = 0f, MaxSlopeDeg = 34f, IsPlayer = true, HiredDay = 0, Licenses = new List<OperatorLicense> { OperatorLicense.Basic, OperatorLicense.Groomer } });
                foreach (var so in scen.StartingOperators)
                {
                    var op = new OperatorState { Id = f.NextOperatorId++, Name = so.Name, Competence = MathUtil.Clamp(so.Competence, 0.5f, 0.99f), HiredDay = 0 };
                    op.Licenses.Add(OperatorLicense.Basic);
                    foreach (var l in so.Licenses) if (!op.Licenses.Contains(l)) op.Licenses.Add(l);
                    op.WagePerHour = WageFor(ctx, op);
                    op.MaxSlopeDeg = SlopeFor(ctx, op.Competence);
                    f.Operators.Add(op);
                }
                RefreshCandidates(ctx);
                RefreshMarket(ctx);
                foreach (var v in ctx.World.Vehicles.List) { var d = ctx.Data.Vehicle(v.DefId); if (d != null && d.HasRole(VehicleRole.Refuel)) v.FuelCargoL = d.TankL > 0f ? d.TankL : d.SpecOr("fuelTankL", 2000f); }
            }
            vs.OperatorLookup = Profile;
            vs.DepotArrival = (c, v) => { RefuelAtDepot(c, v.Id, out _); };
            vs.ExtraWearFactor = (c, v) => c.World.Fleet.Workshop.WashBay ? 1f - c.Data.Stations.WashBay.WearReductionPct / 100f : 1f;
            if (ctx.TryGetSystem<TaskSystem>(out var ts)) { ts.LicenceCheck = LicenceCheck; ts.CompetenceLookup = (c, v) => Profile(c, v).Competence; }
            if (ctx.TryGetSystem<EconomySystem>(out var eco)) eco.FleetValuation = c => { double s = 0; foreach (var v in c.World.Vehicles.List) s += ResaleValue(c, v); return s; };
            _onStranded = e => { var v = ctx.World.Vehicles.Get(e.VehicleId); if (v != null) RequestServiceCall(ctx, v.Id, e.Reason); };
            _onFailure = e => OnFailure(ctx, e);
            _onTask = e => OnTaskCompleted(ctx, e);
            ctx.Events.Subscribe(_onStranded);
            ctx.Events.Subscribe(_onFailure);
            ctx.Events.Subscribe(_onTask);
            if (newGame) UpdateShelter(ctx);
        }

        // ------------------------------------------------------------------ operators
        private VehicleSystem.OperatorProfile Profile(SimContext ctx, VehicleState v)
        {
            var f = ctx.World.Fleet;
            var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
            OperatorState op = v.PlayerControlled ? PlayerOperator(f) : f.Operator(v.OperatorId);
            if (op == null)
            {
                var d = ctx.Data.Operators;
                return new VehicleSystem.OperatorProfile { Competence = d.CompetenceMin, MaxSlopeDeg = d.MaxSlopeDegAtMinCompetence, Licensed = def == null || def.OperatorLicense == OperatorLicense.Basic, OperatorId = -1 };
            }
            return new VehicleSystem.OperatorProfile { Competence = op.Competence, MaxSlopeDeg = op.IsPlayer ? 90f : op.MaxSlopeDeg, Licensed = def == null || OperatorLicensedFor(ctx, op, def), OperatorId = op.Id };
        }

        private static OperatorState PlayerOperator(FleetState f)
        {
            foreach (var o in f.Operators) if (o.IsPlayer) return o;
            return null;
        }

        private bool LicenceCheck(SimContext ctx, VehicleState v, OperatorLicense required)
        {
            if (required == OperatorLicense.Basic) return true;
            var f = ctx.World.Fleet;
            var op = v.PlayerControlled ? PlayerOperator(f) : f.Operator(v.OperatorId);
            return op != null && (op.Has(required) || op.IsPlayer && required == OperatorLicense.Groomer);
        }

        public IReadOnlyList<OperatorState> Operators(SimContext ctx) => ctx.World.Fleet.Operators;
        public IReadOnlyList<OperatorState> Candidates(SimContext ctx) => ctx.World.Fleet.Candidates;

        private float WageFor(SimContext ctx, OperatorState op)
        {
            var d = ctx.Data.Operators;
            float w = d.BaseWagePerHour + (op.Competence - d.CompetenceMin) * d.WagePerCompetencePoint;
            foreach (var l in op.Licenses) { var ld = d.License(l); if (ld != null) w += ld.WagePremiumPerHour; }
            return w;
        }

        private float SlopeFor(SimContext ctx, float competence)
        {
            var d = ctx.Data.Operators;
            float t = MathUtil.InverseLerp(d.CompetenceMin, d.CompetenceMax, competence);
            return MathUtil.Lerp(d.MaxSlopeDegAtMinCompetence, d.MaxSlopeDegAtMaxCompetence, t);
        }

        private void RefreshCandidates(SimContext ctx)
        {
            var f = ctx.World.Fleet;
            var d = ctx.Data.Operators;
            f.Candidates.Clear();
            int n = System.Math.Max(1, d.CandidatesPerWeek);
            for (int i = 0; i < n; i++)
            {
                string first = d.FirstNames.Count > 0 ? d.FirstNames[ctx.Rng.Range(0, d.FirstNames.Count)] : "Operator";
                string last = d.LastNames.Count > 0 ? d.LastNames[ctx.Rng.Range(0, d.LastNames.Count)] : (f.NextOperatorId).ToString();
                var op = new OperatorState { Id = f.NextOperatorId++, Name = first + " " + last, Competence = ctx.Rng.Range(d.CompetenceMin, d.CompetenceMax) };
                op.Licenses.Add(OperatorLicense.Basic);
                if (ctx.Rng.Chance(0.5f)) op.Licenses.Add(OperatorLicense.Groomer);
                if (ctx.Rng.Chance(0.3f)) op.Licenses.Add(OperatorLicense.Cdl);
                if (ctx.Rng.Chance(0.15f)) op.Licenses.Add(OperatorLicense.Heavy);
                if (ctx.Rng.Chance(0.08f) && op.Licenses.Contains(OperatorLicense.Groomer)) op.Licenses.Add(OperatorLicense.WinchCat);
                if (ctx.Rng.Chance(0.08f)) op.Licenses.Add(OperatorLicense.Crane);
                if (ctx.Rng.Chance(0.25f)) op.Licenses.Add(OperatorLicense.Sled);
                op.WagePerHour = WageFor(ctx, op);
                op.MaxSlopeDeg = SlopeFor(ctx, op.Competence);
                f.Candidates.Add(op);
            }
            f.CandidatesRefreshDay = ctx.Time.Day;
        }

        public OperatorState Hire(SimContext ctx, int candidateId, out string reason)
        {
            reason = "";
            var f = ctx.World.Fleet;
            OperatorState cand = null;
            foreach (var c in f.Candidates) if (c.Id == candidateId) cand = c;
            if (cand == null) { reason = "candidate no longer available"; return null; }
            f.Candidates.Remove(cand);
            cand.HiredDay = ctx.Time.Day;
            f.Operators.Add(cand);
            ctx.Events.Publish(new OperatorEvent { OperatorId = cand.Id, What = "hired" });
            ctx.Sim.Log("Hired " + cand.Name + " (competence " + MathF.Round(cand.Competence * 100f) + "%, " + LicenceList(cand) + ") at $" + cand.WagePerHour.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "/h.");
            return cand;
        }

        private static string LicenceList(OperatorState op) => string.Join("/", op.Licenses.ConvertAll(l => l.ToString()).ToArray());

        public bool Fire(SimContext ctx, int operatorId, out string reason)
        {
            reason = "";
            var f = ctx.World.Fleet;
            var op = f.Operator(operatorId);
            if (op == null) { reason = "no such operator"; return false; }
            if (op.IsPlayer) { reason = "you cannot fire yourself"; return false; }
            UnassignOperator(ctx, operatorId);
            f.Operators.Remove(op);
            ctx.Events.Publish(new OperatorEvent { OperatorId = op.Id, What = "fired" });
            return true;
        }

        public bool Train(SimContext ctx, int operatorId, OperatorLicense license, out string reason)
        {
            reason = "";
            var f = ctx.World.Fleet;
            var op = f.Operator(operatorId);
            if (op == null) { reason = "no such operator"; return false; }
            if (op.Has(license)) { reason = op.Name + " already holds " + license; return false; }
            if (op.TrainingLicensePending >= 0) { reason = op.Name + " is already in training"; return false; }
            var ld = ctx.Data.Operators.License(license);
            if (ld == null) { reason = "no course for " + license; return false; }
            foreach (var req in ld.Requires) if (!op.Has(req)) { reason = license + " requires " + req + " first"; return false; }
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Training, ld.TrainingCost, op.Name + ": " + license + " course", out reason)) return false;
            op.TrainingLicensePending = (int)license;
            op.TrainingDoneDay = ctx.Time.Day + System.Math.Max(1, (int)MathF.Ceiling(ld.TrainingDays));
            UnassignOperator(ctx, operatorId);
            ctx.Sim.Log(op.Name + " starts the " + license + " course; back in " + MathF.Ceiling(ld.TrainingDays) + " days.");
            return true;
        }

        public bool AssignOperator(SimContext ctx, int operatorId, int vehicleId, out string reason)
        {
            reason = "";
            var f = ctx.World.Fleet;
            var op = f.Operator(operatorId);
            var v = ctx.World.Vehicles.Get(vehicleId);
            if (op == null || v == null) { reason = "no such operator or machine"; return false; }
            if (op.IsPlayer) { reason = "press Enter near the machine to drive it yourself"; return false; }
            if (op.TrainingLicensePending >= 0) { reason = op.Name + " is in training"; return false; }
            var def = ctx.Data.Vehicle(v.DefId);
            if (def != null && def.IsStationary) { reason = "fixed installation"; return false; }
            UnassignOperator(ctx, operatorId);
            var prev = f.Operator(v.OperatorId);
            if (prev != null) prev.AssignedVehicleId = -1;
            op.AssignedVehicleId = vehicleId;
            v.OperatorId = operatorId;
            if (def != null && !OperatorLicensedFor(ctx, op, def)) ctx.Sim.Log(op.Name + " is not licensed for " + def.DisplayName + " (" + def.OperatorLicense + "): accident risk.", LogLevel.Warning);
            ctx.Events.Publish(new OperatorEvent { OperatorId = op.Id, What = "assigned:" + vehicleId });
            return true;
        }

        /// <summary>Puts a free, rested operator who holds the machine's licence (and the job's) on the machine; false when nobody qualifies.</summary>
        public bool StaffMachine(SimContext ctx, VehicleState v, OperatorLicense jobLicence)
        {
            var f = ctx.World.Fleet;
            var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
            if (def == null) return false;
            // the most competent qualified operator, but a specialist is kept back for the machine that needs the
            // licence: putting the only truck driver in the pickup left the service truck unstaffable all winter
            OperatorState best = null; float bestScore = float.MinValue;
            foreach (var op in f.Operators)
            {
                if (op.IsPlayer || op.AssignedVehicleId >= 0 || op.TrainingLicensePending >= 0) continue;
                if (op.HoursToday >= ctx.Data.Operators.ShiftHours) continue;
                if (!OperatorLicensedFor(ctx, op, def)) continue;
                if (jobLicence != OperatorLicense.Basic && !op.Has(jobLicence)) continue;
                int spare = 0;
                foreach (var l in op.Licenses) if (l != OperatorLicense.Basic && l != def.OperatorLicense && l != jobLicence) spare++;
                float score = op.Competence - 0.5f * spare;
                if (best == null || score > bestScore) { best = op; bestScore = score; }
            }
            if (best == null) return false;
            return AssignOperator(ctx, best.Id, v.Id, out _);
        }

        public void UnassignOperator(SimContext ctx, int operatorId)
        {
            var f = ctx.World.Fleet;
            var op = f.Operator(operatorId);
            if (op == null) return;
            if (op.AssignedVehicleId >= 0)
            {
                var v = ctx.World.Vehicles.Get(op.AssignedVehicleId);
                if (v != null && v.OperatorId == operatorId) { v.OperatorId = -1; if (!v.PlayerControlled) ctx.System<VehicleSystem>().ClearAi(ctx, v.Id); }
            }
            op.AssignedVehicleId = -1;
            op.OnShift = false;
        }

        public bool OperatorLicensedFor(SimContext ctx, OperatorState op, VehicleDef def) => def.OperatorLicense == OperatorLicense.Basic || op.Has(def.OperatorLicense);

        // ------------------------------------------------------------------ market
        public IReadOnlyList<MarketListing> Market(SimContext ctx) => ctx.World.Fleet.Market;

        public void RefreshMarket(SimContext ctx)
        {
            var f = ctx.World.Fleet;
            var t = ctx.Tuning;
            int act = ctx.World.Economy.Act;
            int maxTier = ctx.Data.Economy.Act(act).MaxVehicleTier;
            f.Market.RemoveAll(l => l.ExpiresDay <= ctx.Time.Day);
            int keepDays = t.I("fleet.marketRefreshDays");
            foreach (var def in ctx.Data.Vehicles)
            {
                if (def.Tier > maxTier + 1) continue; // one tier above the act shows up as aspiration, unaffordable
                bool hasNew = false;
                foreach (var l in f.Market) if (l.DefId == def.Id && l.Kind == ListingKind.New) hasNew = true;
                if (!hasNew) f.Market.Add(new MarketListing { Id = f.NextListingId++, DefId = def.Id, Kind = ListingKind.New, Price = def.PurchasePrice, Hours = 0f, ConditionPct = 100f, ExpiresDay = ctx.Time.Day + keepDays * 4, SellerNote = "factory order" });
                if (def.IsStationary) continue;
                if (def.LeaseMonthly > 0 || def.PurchasePrice > 0)
                {
                    bool hasLease = false; foreach (var l in f.Market) if (l.DefId == def.Id && l.Kind == ListingKind.Lease) hasLease = true;
                    if (!hasLease) f.Market.Add(new MarketListing { Id = f.NextListingId++, DefId = def.Id, Kind = ListingKind.Lease, Price = 0, MonthlyOrDaily = def.LeaseMonthly > 0 ? def.LeaseMonthly : def.PurchasePrice * t.F("fleet.leaseMonthlyFracOfPrice"), ConditionPct = 100f, ExpiresDay = ctx.Time.Day + keepDays * 4, SellerNote = "36-month lease" });
                    bool hasRent = false; foreach (var l in f.Market) if (l.DefId == def.Id && l.Kind == ListingKind.Rent) hasRent = true;
                    if (!hasRent) f.Market.Add(new MarketListing { Id = f.NextListingId++, DefId = def.Id, Kind = ListingKind.Rent, Price = 0, MonthlyOrDaily = def.PurchasePrice * t.F("fleet.rentDailyFracOfPrice"), Hours = 1500f, ConditionPct = 80f, ExpiresDay = ctx.Time.Day + keepDays * 4, SellerNote = "peak-week rental" });
                }
                int used = 0; foreach (var l in f.Market) if (l.DefId == def.Id && l.Kind == ListingKind.Used) used++;
                int want = ctx.Rng.Range(0, t.I("fleet.usedListingsPerDefMax") + 1);
                for (int i = used; i < want; i++)
                {
                    float rebuild = def.SpecOr("rebuildIntervalHours", 7000f);
                    float hours = rebuild * ctx.Rng.Range(t.F("fleet.usedHoursFracMin"), t.F("fleet.usedHoursFracMax"));
                    float cond = MathUtil.Clamp(100f - hours / rebuild * 55f + ctx.Rng.NextGaussian() * 8f, 20f, 95f);
                    double price = def.PurchasePrice * def.ResaleFraction(hours) * def.UsedMarketMultiplier / 0.5f * MathUtil.Lerp(0.6f, 1.05f, cond / 100f);
                    var listing = new MarketListing { Id = f.NextListingId++, DefId = def.Id, Kind = ListingKind.Used, Price = System.Math.Round(price / 100) * 100, Hours = MathF.Round(hours), ConditionPct = MathF.Round(cond), ExpiresDay = ctx.Time.Day + ctx.Rng.Range(keepDays, keepDays * 3), SellerNote = hours > rebuild * 0.7f ? "high hours, priced to move" : "one owner, serviced" };
                    int defects = ctx.Rng.Chance(t.F("fleet.hiddenDefectProbPerThousandHours") * hours / 1000f) ? ctx.Rng.Range(1, 3) : 0;
                    for (int k = 0; k < defects; k++)
                    {
                        var sub = (WearSubsystem)ctx.Rng.Range(0, 4);
                        listing.Defects.Add(new HiddenDefect { Subsystem = sub, ExtraWear = ctx.Rng.Range(0.15f, 0.4f), Description = sub + " worn beyond the hour meter" });
                    }
                    f.Market.Add(listing);
                }
            }
            f.MarketRefreshDay = ctx.Time.Day;
            ctx.Events.Publish(new MarketRefreshedEvent { Listings = f.Market.Count });
        }

        private Math.Vec2 GaragePos(SimContext ctx) => ctx.Sim.Scenario.Landmark(ctx.Sim.Scenario.GarageLandmarkId).Pos;

        public VehicleState BuyNew(SimContext ctx, string defId, out string reason)
        {
            reason = "";
            var def = ctx.Data.Vehicle(defId);
            if (def == null) { reason = "unknown machine"; return null; }
            if (ctx.TryGetSystem<EconomySystem>(out var eco))
            {
                if (!eco.CanBuyVehicleTier(ctx, def.Tier)) { reason = def.DisplayName + " is tier " + def.Tier + "; " + eco.ActDef(ctx).DisplayName + " allows up to tier " + eco.ActDef(ctx).MaxVehicleTier; return null; }
                if (!eco.TrySpend(ctx, LedgerCategory.Purchase, def.PurchasePrice, "Bought new " + def.DisplayName, out reason)) return null;
            }
            var v = SpawnOwned(ctx, def, 0f, 100f);
            ctx.Events.Publish(new VehicleAcquiredEvent { VehicleId = v.Id, Kind = ListingKind.New, Price = def.PurchasePrice });
            ctx.Sim.Log("Delivered: new " + def.DisplayName + " for " + EconomySystem.Money(def.PurchasePrice) + ".");
            return v;
        }

        private VehicleState SpawnOwned(SimContext ctx, VehicleDef def, float hours, float cond)
        {
            var vs = ctx.System<VehicleSystem>();
            var pos = GaragePos(ctx) + new Math.Vec2(ctx.Rng.Range(-12f, 12f), ctx.Rng.Range(-12f, 12f));
            var v = vs.Spawn(ctx, def.Id, pos, 0f, null, hours, cond, 0.5f);
            v.PurchasePaid = def.PurchasePrice;
            if (def.HasRole(VehicleRole.Refuel)) v.FuelCargoL = def.TankL > 0f ? def.TankL : def.SpecOr("fuelTankL", 2000f);
            if (def.IsStationary) v.Pos = StationPos(ctx, def);
            UpdateShelter(ctx);
            return v;
        }

        private Math.Vec2 StationPos(SimContext ctx, VehicleDef def)
        {
            var scen = ctx.Sim.Scenario;
            if (def.HasRole(VehicleRole.Refuel) || def.HasSpec("fuelStorageL")) return scen.Landmark(scen.FuelLandmarkId).Pos;
            if (def.HasRole(VehicleRole.Pump) || def.HasRole(VehicleRole.Compress)) return scen.Landmark("pumpHouse").Pos;
            if (def.HasRole(VehicleRole.MakeSnow)) return scen.Landmark(scen.GarageLandmarkId).Pos;
            return scen.Landmark(scen.WorkshopLandmarkId).Pos;
        }

        public VehicleState BuyListing(SimContext ctx, int listingId, out string reason)
        {
            reason = "";
            var f = ctx.World.Fleet;
            MarketListing l = null;
            foreach (var m in f.Market) if (m.Id == listingId) l = m;
            if (l == null) { reason = "listing gone"; return null; }
            var def = ctx.Data.Vehicle(l.DefId);
            if (def == null) { reason = "unknown machine"; return null; }
            switch (l.Kind)
            {
                case ListingKind.New: return BuyNew(ctx, def.Id, out reason);
                case ListingKind.Lease: return Lease(ctx, def.Id, 36, out reason);
                case ListingKind.Rent: return Rent(ctx, def.Id, ctx.Tuning.I("fleet.rentDefaultDays"), out reason);
            }
            if (ctx.TryGetSystem<EconomySystem>(out var eco))
            {
                if (!eco.CanBuyVehicleTier(ctx, def.Tier)) { reason = def.DisplayName + " is tier " + def.Tier + " (act limit " + eco.ActDef(ctx).MaxVehicleTier + ")"; return null; }
                if (!eco.TrySpend(ctx, LedgerCategory.Purchase, l.Price, "Bought used " + def.DisplayName + " (" + l.Hours + " h)", out reason)) return null;
            }
            var v = SpawnOwned(ctx, def, l.Hours, l.ConditionPct);
            v.PurchasePaid = l.Price;
            foreach (var d in l.Defects) v.Condition.HiddenDefects.Add(new HiddenDefect { Subsystem = d.Subsystem, ExtraWear = d.ExtraWear, Revealed = d.Revealed, Description = d.Description });
            foreach (var d in v.Condition.HiddenDefects) v.Condition.SetWear(d.Subsystem, v.Condition.Wear(d.Subsystem) + d.ExtraWear);
            f.Market.Remove(l);
            ctx.Events.Publish(new VehicleAcquiredEvent { VehicleId = v.Id, Kind = ListingKind.Used, Price = l.Price });
            ctx.Sim.Log("Bought used " + def.DisplayName + " with " + l.Hours + " h for " + EconomySystem.Money(l.Price) + (l.Defects.Count > 0 && !l.Inspected ? " (uninspected)" : "") + ".");
            return v;
        }

        public bool Inspect(SimContext ctx, int listingId, out string reason)
        {
            reason = "";
            var f = ctx.World.Fleet;
            MarketListing l = null;
            foreach (var m in f.Market) if (m.Id == listingId) l = m;
            if (l == null || l.Kind != ListingKind.Used) { reason = "only used listings can be inspected"; return false; }
            if (l.Inspected) return true;
            double fee = ctx.Tuning.F("fleet.inspectionFee");
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Misc, fee, "Pre-purchase inspection", out reason)) return false;
            l.Inspected = true;
            foreach (var d in l.Defects) d.Revealed = true;
            ctx.Sim.Log("Inspection of " + ctx.Data.Vehicle(l.DefId)?.DisplayName + ": " + (l.Defects.Count == 0 ? "clean." : l.Defects.Count + " hidden defect(s) found."));
            return true;
        }

        public VehicleState Lease(SimContext ctx, string defId, int termMonths, out string reason)
        {
            reason = "";
            var def = ctx.Data.Vehicle(defId);
            if (def == null) { reason = "unknown machine"; return null; }
            double monthly = def.LeaseMonthly > 0 ? def.LeaseMonthly : def.PurchasePrice * ctx.Tuning.F("fleet.leaseMonthlyFracOfPrice");
            if (ctx.TryGetSystem<EconomySystem>(out var eco))
            {
                if (!eco.CanBuyVehicleTier(ctx, def.Tier)) { reason = "tier " + def.Tier + " not available in this act"; return null; }
                if (!eco.TrySpend(ctx, LedgerCategory.Lease, monthly, "Lease first month: " + def.DisplayName, out reason)) return null;
            }
            var v = SpawnOwned(ctx, def, 0f, 100f);
            v.Leased = true;
            ctx.World.Fleet.Leases.Add(new LeaseContract { VehicleId = v.Id, Monthly = monthly, StartDay = ctx.Time.Day, TermMonths = termMonths, Residual = def.PurchasePrice * 0.35 });
            ctx.Events.Publish(new VehicleAcquiredEvent { VehicleId = v.Id, Kind = ListingKind.Lease, Price = monthly });
            ctx.Sim.Log("Leased " + def.DisplayName + " at " + EconomySystem.Money(monthly) + "/month.");
            return v;
        }

        public VehicleState Rent(SimContext ctx, string defId, int days, out string reason)
        {
            reason = "";
            var def = ctx.Data.Vehicle(defId);
            if (def == null) { reason = "unknown machine"; return null; }
            days = System.Math.Max(1, days);
            double daily = def.PurchasePrice * ctx.Tuning.F("fleet.rentDailyFracOfPrice");
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Rental, daily * days, "Rental " + days + " days: " + def.DisplayName, out reason)) return null;
            var v = SpawnOwned(ctx, def, 1500f, 80f);
            v.Rented = true;
            v.RentalEndDay = ctx.Time.Day + days;
            ctx.Events.Publish(new VehicleAcquiredEvent { VehicleId = v.Id, Kind = ListingKind.Rent, Price = daily * days });
            ctx.Sim.Log("Rented " + def.DisplayName + " for " + days + " days (" + EconomySystem.Money(daily * days) + ").");
            return v;
        }

        public double ResaleValue(SimContext ctx, VehicleState v)
        {
            var def = ctx.Data.Vehicle(v.DefId);
            if (def == null || v.Leased || v.Rented) return 0;
            double value = def.PurchasePrice * def.ResaleFraction(v.HoursMeter) * MathUtil.Lerp(0.45f, 1f, v.Condition.ConditionPct / 100f);
            if (v.Condition.IsDown) value *= 0.7;
            return System.Math.Round(value / 100) * 100;
        }

        public bool Sell(SimContext ctx, int vehicleId, out string reason)
        {
            reason = "";
            var v = ctx.World.Vehicles.Get(vehicleId);
            if (v == null) { reason = "no such machine"; return false; }
            if (v.Leased || v.Rented) { reason = "you do not own it"; return false; }
            if (v.PlayerControlled) { reason = "get out first"; return false; }
            double value = ResaleValue(ctx, v);
            var def = ctx.Data.Vehicle(v.DefId);
            ctx.TryGetSystem<EconomySystem>(out var eco);
            eco?.Post(ctx, LedgerCategory.Sale, value, "Sold " + (def != null ? def.DisplayName : v.DefId) + " (" + MathF.Round(v.HoursMeter) + " h)");
            var op = ctx.World.Fleet.Operator(v.OperatorId);
            if (op != null) op.AssignedVehicleId = -1;
            ctx.System<VehicleSystem>().Remove(ctx, vehicleId);
            ctx.Events.Publish(new VehicleSoldEvent { VehicleId = vehicleId, Price = value });
            return true;
        }

        public VehicleState TradeIn(SimContext ctx, int vehicleId, string newDefId, out string reason)
        {
            reason = "";
            var v = ctx.World.Vehicles.Get(vehicleId);
            var def = ctx.Data.Vehicle(newDefId);
            if (v == null || def == null) { reason = "no such machine"; return null; }
            if (v.Leased || v.Rented) { reason = "you do not own the trade-in"; return null; }
            double credit = ResaleValue(ctx, v) * ctx.Tuning.F("fleet.tradeInFactor");
            if (ctx.TryGetSystem<EconomySystem>(out var eco))
            {
                if (!eco.CanBuyVehicleTier(ctx, def.Tier)) { reason = "tier not available in this act"; return null; }
                if (!eco.CanAfford(ctx, def.PurchasePrice - credit)) { reason = "cannot afford " + EconomySystem.Money(def.PurchasePrice - credit) + " after trade-in"; return null; }
                eco.Post(ctx, LedgerCategory.TradeIn, credit, "Trade-in credit: " + v.Name);
                eco.Post(ctx, LedgerCategory.Purchase, -def.PurchasePrice, "Bought new " + def.DisplayName);
            }
            ctx.System<VehicleSystem>().Remove(ctx, vehicleId);
            var nv = SpawnOwned(ctx, def, 0f, 100f);
            ctx.Events.Publish(new VehicleAcquiredEvent { VehicleId = nv.Id, Kind = ListingKind.New, Price = def.PurchasePrice - credit });
            return nv;
        }

        public bool BuyAttachment(SimContext ctx, string attachmentId, out string reason)
        {
            reason = "";
            var att = ctx.Data.Attachment(attachmentId);
            if (att == null) { reason = "unknown attachment"; return false; }
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Purchase, att.PurchasePrice, "Bought " + att.DisplayName, out reason)) return false;
            ctx.World.Fleet.OwnedAttachments.Add(attachmentId);
            return true;
        }

        public IReadOnlyList<string> OwnedAttachments(SimContext ctx) => ctx.World.Fleet.OwnedAttachments;

        // ------------------------------------------------------------------ service
        public IReadOnlyList<ServiceJob> Jobs(SimContext ctx) => ctx.World.Fleet.Workshop.Jobs;

        private ServiceJob NewJob(SimContext ctx, int vehicleId, ServiceJobKind kind)
        {
            var w = ctx.World.Fleet.Workshop;
            var job = new ServiceJob { Id = w.NextJobId++, VehicleId = vehicleId, Kind = kind, CreatedDay = ctx.Time.Day };
            w.Jobs.Add(job);
            return job;
        }

        private bool AtWorkshop(SimContext ctx, VehicleState v)
        {
            var scen = ctx.Sim.Scenario;
            return Math.Vec2.Distance(v.Pos, scen.Landmark(scen.WorkshopLandmarkId).Pos) <= scen.GarageRadiusM * 2f || Math.Vec2.Distance(v.Pos, GaragePos(ctx)) <= scen.GarageRadiusM * 2f;
        }

        public ServiceJob SchedulePm(SimContext ctx, int vehicleId, out string reason)
        {
            reason = "";
            var v = ctx.World.Vehicles.Get(vehicleId);
            if (v == null) { reason = "no such machine"; return null; }
            foreach (var j in ctx.World.Fleet.Workshop.Jobs) if (j.VehicleId == vehicleId && (j.Status == ServiceJobStatus.Queued || j.Status == ServiceJobStatus.InProgress || j.Status == ServiceJobStatus.WaitingParts)) { reason = "already in the shop queue"; return null; }
            if (!AtWorkshop(ctx, v)) { reason = v.Name + " must be at the workshop (drive it or send it home)"; return null; }
            var def = ctx.Data.Vehicle(v.DefId);
            var job = NewJob(ctx, vehicleId, ServiceJobKind.PreventiveMaintenance);
            job.HoursRequired = ctx.Tuning.F("fleet.pmHours");
            job.Cost = def.PurchasePrice * ctx.Tuning.F("fleet.pmCostFracOfPrice");
            job.PartsKind = "filters"; job.PartsQty = 1;
            job.Note = "250 h service";
            ctx.Events.Publish(new ServiceJobEvent { JobId = job.Id, Status = job.Status });
            return job;
        }

        public ServiceJob RequestRepair(SimContext ctx, int vehicleId, out string reason)
        {
            reason = "";
            var v = ctx.World.Vehicles.Get(vehicleId);
            if (v == null) { reason = "no such machine"; return null; }
            if (v.Condition.ActiveFailures.Count == 0) { reason = "nothing broken"; return null; }
            if (!AtWorkshop(ctx, v)) { reason = v.Name + " is in the field; send the service truck (Repair job) or tow it"; return null; }
            var f = v.Condition.ActiveFailures[0];
            var job = NewJob(ctx, vehicleId, ServiceJobKind.Repair);
            job.Subsystem = f.Subsystem;
            job.HoursRequired = f.RepairHours;
            job.Cost = f.RepairCost;
            job.PartsKind = f.PartsKind; job.PartsQty = 1;
            job.Note = f.Description;
            var tier = ctx.Data.Stations.Workshop(ctx.World.Fleet.Workshop.Tier);
            if (!CanRepairInHouse(tier, f.Subsystem))
            {
                job.Status = ServiceJobStatus.SentOut;
                job.ReturnDay = ctx.Time.Day + (int)MathF.Ceiling(ctx.Tuning.F("fleet.sendOutDays") * tier.SendOutDaysMultiplier);
                job.Cost *= ctx.Tuning.F("fleet.sendOutCostMultiplier");
                ctx.Sim.Log(v.Name + " sent out for " + f.Subsystem + " repair; back on day " + (job.ReturnDay + 1) + ".", LogLevel.Warning);
            }
            ctx.Events.Publish(new ServiceJobEvent { JobId = job.Id, Status = job.Status });
            return job;
        }

        private static bool CanRepairInHouse(WorkshopTierDef tier, WearSubsystem sub)
        {
            string key = sub == WearSubsystem.TracksOrTires ? "tracksOrTires" : sub.ToString().Substring(0, 1).ToLowerInvariant() + sub.ToString().Substring(1);
            foreach (var s in tier.CanRepair) if (string.Equals(s, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public ServiceJob RequestRebuild(SimContext ctx, int vehicleId, out string reason)
        {
            reason = "";
            var v = ctx.World.Vehicles.Get(vehicleId);
            if (v == null) { reason = "no such machine"; return null; }
            if (!AtWorkshop(ctx, v)) { reason = v.Name + " must be at the workshop"; return null; }
            var tier = ctx.Data.Stations.Workshop(ctx.World.Fleet.Workshop.Tier);
            var def = ctx.Data.Vehicle(v.DefId);
            var job = NewJob(ctx, vehicleId, ServiceJobKind.Rebuild);
            job.HoursRequired = ctx.Tuning.F("fleet.rebuildHours");
            job.Cost = def.PurchasePrice * ctx.Tuning.F("fleet.rebuildCostFracOfPrice");
            job.PartsKind = "engineKit"; job.PartsQty = 1;
            if (!tier.CanRebuild) { job.Status = ServiceJobStatus.SentOut; job.ReturnDay = ctx.Time.Day + (int)MathF.Ceiling(ctx.Tuning.F("fleet.sendOutDays") * 3f); job.Cost *= ctx.Tuning.F("fleet.sendOutCostMultiplier"); }
            return job;
        }

        public ServiceJob RequestMount(SimContext ctx, int vehicleId, string attachmentId, SlotPosition slot, out string reason)
        {
            reason = "";
            var v = ctx.World.Vehicles.Get(vehicleId);
            var att = ctx.Data.Attachment(attachmentId);
            if (v == null || att == null) { reason = "no such machine or attachment"; return null; }
            if (!ctx.World.Fleet.OwnedAttachments.Contains(attachmentId)) { reason = "you do not own a " + att.DisplayName; return null; }
            if (!AtWorkshop(ctx, v)) { reason = v.Name + " must be at the workshop to swap implements"; return null; }
            var vs = ctx.System<VehicleSystem>();
            var def = ctx.Data.Vehicle(v.DefId);
            var s = def.Slot(slot);
            if (s == null || !att.FitsSlot(slot)) { reason = att.DisplayName + " does not fit the " + slot + " slot of " + def.DisplayName; return null; }
            var job = NewJob(ctx, vehicleId, ServiceJobKind.Mount);
            job.AttachmentId = attachmentId; job.Slot = slot;
            job.HoursRequired = att.MountMinutes / 60f + (v.Slot(slot) != null ? att.DismountMinutes / 60f : 0f);
            job.Cost = job.HoursRequired * ctx.Data.Economy.Wage("mechanic");
            job.Note = "mount " + att.DisplayName;
            return job;
        }

        public ServiceJob RequestDismount(SimContext ctx, int vehicleId, SlotPosition slot, out string reason)
        {
            reason = "";
            var v = ctx.World.Vehicles.Get(vehicleId);
            if (v == null) { reason = "no such machine"; return null; }
            var m = v.Slot(slot);
            if (m == null) { reason = "nothing mounted"; return null; }
            if (!AtWorkshop(ctx, v)) { reason = v.Name + " must be at the workshop"; return null; }
            var att = ctx.Data.Attachment(m.DefId);
            var job = NewJob(ctx, vehicleId, ServiceJobKind.Dismount);
            job.Slot = slot; job.AttachmentId = m.DefId;
            job.HoursRequired = (att != null ? att.DismountMinutes : 20f) / 60f;
            job.Cost = job.HoursRequired * ctx.Data.Economy.Wage("mechanic");
            return job;
        }

        public bool CancelJob(SimContext ctx, int jobId)
        {
            var w = ctx.World.Fleet.Workshop;
            foreach (var j in w.Jobs) if (j.Id == jobId && j.Status != ServiceJobStatus.Done) { j.Status = ServiceJobStatus.Cancelled; return true; }
            return false;
        }

        public bool OrderParts(SimContext ctx, string kind, int qty, out string reason)
        {
            reason = "";
            PartsKindDef pk = null;
            foreach (var p in ctx.Data.Stations.PartsKinds) if (p.Id == kind) pk = p;
            if (pk == null) { reason = "unknown part " + kind; return false; }
            double cost = pk.UnitCost * System.Math.Max(1, qty);
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Parts, cost, "Parts: " + qty + " x " + pk.DisplayName, out reason)) return false;
            var inv = ctx.World.Fleet.Parts;
            inv.Orders.Add(new PartsOrder { Id = inv.NextOrderId++, Kind = kind, Qty = System.Math.Max(1, qty), ArriveDay = ctx.Time.Day + (int)MathF.Ceiling(pk.LeadDays), Cost = cost });
            return true;
        }

        public bool UpgradeWorkshop(SimContext ctx, out string reason)
        {
            reason = "";
            var w = ctx.World.Fleet.Workshop;
            WorkshopTierDef next = null;
            foreach (var t in ctx.Data.Stations.WorkshopTiers) if (t.Tier == w.Tier + 1) next = t;
            if (next == null) { reason = "workshop is at the top tier"; return false; }
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Construction, next.UpgradeCost, "Workshop upgrade to tier " + next.Tier, out reason)) return false;
            w.Tier = next.Tier;
            ctx.Sim.Log("Workshop upgraded to tier " + w.Tier + ": " + next.DisplayName + ".");
            return true;
        }

        public bool BuyGarageBays(SimContext ctx, out string reason)
        {
            reason = "";
            var g = ctx.Data.Stations.Garage;
            double cost = g.CostPerBay * g.BaysPerUpgrade;
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Construction, cost, "Machine shed: +" + g.BaysPerUpgrade + " bays", out reason)) return false;
            ctx.World.Fleet.Workshop.GarageBays += g.BaysPerUpgrade;
            UpdateShelter(ctx);
            return true;
        }

        public bool BuildWashBay(SimContext ctx, out string reason)
        {
            reason = "";
            if (ctx.World.Fleet.Workshop.WashBay) { reason = "already built"; return false; }
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Construction, ctx.Data.Stations.WashBay.Cost, "Wash bay", out reason)) return false;
            ctx.World.Fleet.Workshop.WashBay = true;
            return true;
        }

        public float HoursToService(SimContext ctx, VehicleState v)
        {
            var def = ctx.Data.Vehicle(v.DefId);
            return def == null ? 0f : def.ServiceIntervalHours - v.Condition.HoursSinceService;
        }

        // ------------------------------------------------------------------ fuel
        private int _fuelOrderFailDay = -1;

        /// <summary>
        /// The office reorders diesel when stock plus deliveries on the way falls under fuel.autoOrderBelowFrac of the
        /// depot, filling it: a resort whose machines work every night cannot wait for the player to notice the gauge.
        /// </summary>
        private void AutoOrderFuel(SimContext ctx)
        {
            var f = ctx.World.Fleet.Fuel;
            if (!f.AutoOrder) return;
            float pending = 0f; foreach (var d in f.Pending) pending += d.Liters;
            if (f.DieselL + pending >= f.DieselCapacityL * ctx.Tuning.F("fuel.autoOrderBelowFrac")) return;
            float liters = MathF.Floor(f.DieselCapacityL - f.DieselL - pending);
            if (liters < ctx.Data.Stations.FuelDepot.DeliveryMinL) return;
            bool contract = f.ContractRemainingL >= liters;
            if (OrderFuel(ctx, liters, contract, out string why)) ctx.Sim.Log("Fuel office ordered " + liters + " L of diesel (depot at " + MathF.Round(f.DieselL) + " L).");
            else if (_fuelOrderFailDay != ctx.Time.Day) { _fuelOrderFailDay = ctx.Time.Day; ctx.Sim.Log("Fuel office could not order diesel: " + why + ".", LogLevel.Warning); }
        }

        public bool OrderFuel(SimContext ctx, float liters, bool contract, out string reason)
        {
            reason = "";
            var f = ctx.World.Fleet.Fuel;
            var dep = ctx.Data.Stations.FuelDepot;
            if (liters < dep.DeliveryMinL) { reason = "minimum delivery is " + dep.DeliveryMinL + " L"; return false; }
            float pending = 0f; foreach (var d in f.Pending) pending += d.Liters;
            if (f.DieselL + pending + liters > f.DieselCapacityL) { reason = "depot holds " + f.DieselCapacityL + " L; " + (f.DieselCapacityL - f.DieselL - pending) + " L of room"; return false; }
            float price = contract && f.ContractRemainingL >= liters ? f.ContractPricePerL : f.DieselPricePerL * (1f + dep.SpotSurchargePct / 100f);
            double cost = price * liters;
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Fuel, cost, "Diesel delivery " + liters + " L at $" + price.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "/L", out reason)) return false;
            if (contract && f.ContractRemainingL >= liters) f.ContractRemainingL -= liters;
            int arriveHour = ctx.Time.HourOfDay + (int)dep.DeliveryLeadHours;
            f.Pending.Add(new FuelDelivery { Id = f.NextDeliveryId++, Liters = liters, ArriveDay = ctx.Time.Day + arriveHour / 24, ArriveHour = arriveHour % 24, Cost = cost, Contract = contract });
            f.FuelSpendSeason += cost;
            return true;
        }

        public bool SignFuelContract(SimContext ctx, out string reason)
        {
            reason = "";
            var f = ctx.World.Fleet.Fuel;
            var dep = ctx.Data.Stations.FuelDepot;
            if (f.ContractRemainingL > 0f) { reason = "a contract is still running (" + f.ContractRemainingL + " L left)"; return false; }
            f.ContractRemainingL = dep.ContractVolumeL;
            f.ContractPricePerL = f.DieselPricePerL * (1f - dep.ContractDiscountPct / 100f);
            ctx.Sim.Log("Fuel contract signed: " + dep.ContractVolumeL + " L at $" + f.ContractPricePerL.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "/L.");
            return true;
        }

        public float RefuelAtDepot(SimContext ctx, int vehicleId, out string reason)
        {
            reason = "";
            var v = ctx.World.Vehicles.Get(vehicleId);
            if (v == null) { reason = "no such machine"; return 0f; }
            var scen = ctx.Sim.Scenario;
            if (Math.Vec2.Distance(v.Pos, scen.Landmark(scen.FuelLandmarkId).Pos) > scen.GarageRadiusM * 2f) { reason = "not at the fuel depot"; return 0f; }
            var def = ctx.Data.Vehicle(v.DefId);
            var f = ctx.World.Fleet.Fuel;
            float need = def.EnergyCapacity - v.Fuel;
            if (def.IsElectric) { ctx.TryGetSystem<EconomySystem>(out var eco); eco?.Post(ctx, LedgerCategory.Electricity, -need * ctx.Data.Economy.ElectricityPricePerKwh, v.Name + " charging"); return ctx.System<VehicleSystem>().Refuel(ctx, vehicleId, need); }
            bool gas = def.FuelType == FuelType.Gasoline;
            float stock = gas ? f.GasolineL : f.DieselL;
            float take = MathF.Min(need, stock);
            if (take <= 0f) { reason = "depot is dry: order a delivery"; return 0f; }
            if (gas) f.GasolineL -= take; else { f.DieselL -= take; f.DieselUsedSeasonL += take; }
            float got = ctx.System<VehicleSystem>().Refuel(ctx, vehicleId, take);
            if (def.HasRole(VehicleRole.Refuel))
            {
                float tank = def.TankL > 0f ? def.TankL : def.SpecOr("fuelTankL", 2000f);
                float fill = MathF.Min(tank - v.FuelCargoL, f.DieselL);
                if (fill > 0f) { v.FuelCargoL += fill; f.DieselL -= fill; }
            }
            return got;
        }

        public ServiceCall RequestServiceCall(SimContext ctx, int vehicleId, string why)
        {
            var f = ctx.World.Fleet;
            var v = ctx.World.Vehicles.Get(vehicleId);
            if (v == null) return null;
            foreach (var c in f.ServiceCalls) if (c.VehicleId == vehicleId && !c.Resolved) return c;
            var call = new ServiceCall { Id = f.NextServiceCallId++, VehicleId = vehicleId, Reason = why, CreatedTick = ctx.Time.Tick };
            f.ServiceCalls.Add(call);
            if (ctx.TryGetSystem<TaskSystem>(out var ts))
            {
                bool fuel = v.Stranded && (why.Contains("fuel") || why.Contains("battery"));
                var task = ts.Create(ctx, fuel ? TaskKind.Refuel : TaskKind.Repair, (fuel ? "Refuel " : "Field repair ") + v.Name + " at " + v.LocationLabel, v.Pos, vehicleId.ToString(), -1, fuel ? ctx.Tuning.F("fleet.fieldRefuelWorkHours") : ctx.Tuning.F("fleet.fieldRepairWorkHours"), 3f);
                task.AutoGenerated = true;
                call.TaskId = task.Id;
            }
            ctx.Events.Publish(new ServiceCallEvent { CallId = call.Id, VehicleId = vehicleId, Reason = why, Resolved = false });
            return call;
        }

        private void OnFailure(SimContext ctx, VehicleFailureEvent e)
        {
            var v = ctx.World.Vehicles.Get(e.VehicleId);
            if (v == null) return;
            // reveal a hidden defect on the failed subsystem
            foreach (var d in v.Condition.HiddenDefects) if (d.Subsystem == e.Failure.Subsystem && !d.Revealed) { d.Revealed = true; ctx.Sim.Log(v.Name + ": the failure exposed a hidden defect (" + d.Description + ").", LogLevel.Warning); }
            if (e.Failure.Immobilising)
            {
                if (AtWorkshop(ctx, v)) RequestRepair(ctx, v.Id, out _);
                else RequestServiceCall(ctx, v.Id, e.Failure.Description);
            }
            else if (AtWorkshop(ctx, v)) RequestRepair(ctx, v.Id, out _);
        }

        private void OnTaskCompleted(SimContext ctx, TaskCompletedEvent e)
        {
            if (e.Kind != TaskKind.Refuel && e.Kind != TaskKind.Repair) return;
            if (!int.TryParse(e.TargetId, out int vehicleId)) return;
            var v = ctx.World.Vehicles.Get(vehicleId);
            var f = ctx.World.Fleet;
            if (v == null) return;
            var ts = ctx.System<TaskSystem>();
            var task = ts.Get(ctx, e.TaskId);
            var truck = task != null && task.AssignedVehicleId >= 0 ? ctx.World.Vehicles.Get(task.AssignedVehicleId) : null;
            var def = ctx.Data.Vehicle(v.DefId);
            ctx.TryGetSystem<EconomySystem>(out var eco);
            if (e.Kind == TaskKind.Refuel)
            {
                float need = def.EnergyCapacity * ctx.Tuning.F("fleet.fieldRefuelFrac") - v.Fuel;
                float available = truck != null ? truck.FuelCargoL : need;
                float give = MathF.Max(0f, MathF.Min(need, available));
                if (truck != null) truck.FuelCargoL -= give;
                ctx.System<VehicleSystem>().Refuel(ctx, v.Id, give);
                ctx.Sim.Log(v.Name + " refuelled in the field with " + MathF.Round(give) + " L.");
            }
            else
            {
                double cost = 0;
                foreach (var fl in v.Condition.ActiveFailures) cost += fl.RepairCost * ctx.Tuning.F("fleet.fieldRepairMultiplier");
                bool partsOk = true;
                foreach (var fl in v.Condition.ActiveFailures) if (f.Parts.Get(fl.PartsKind) <= 0) partsOk = false;
                if (!partsOk) cost *= ctx.Tuning.F("fleet.expressPartsMultiplier");
                foreach (var fl in v.Condition.ActiveFailures) { if (f.Parts.Get(fl.PartsKind) > 0) f.Parts.Stock[fl.PartsKind]--; v.Condition.SetWear(fl.Subsystem, MathF.Max(0f, v.Condition.Wear(fl.Subsystem) - 0.25f)); }
                v.Condition.ActiveFailures.Clear();
                v.Condition.ServiceLog.Add("Day " + (ctx.Time.Day + 1) + ": field repair " + EconomySystem.Money(cost));
                eco?.Post(ctx, LedgerCategory.VehicleRepair, -cost, v.Name + " field repair");
                if (v.Ai.Mode == AiMode.Stranded) v.Ai.Mode = AiMode.Idle;
                v.Stranded = false;
                ctx.Sim.Log(v.Name + " repaired in the field for " + EconomySystem.Money(cost) + ".");
            }
            foreach (var c in f.ServiceCalls) if (c.VehicleId == vehicleId && !c.Resolved) { c.Resolved = true; ctx.Events.Publish(new ServiceCallEvent { CallId = c.Id, VehicleId = vehicleId, Reason = c.Reason, Resolved = true }); }
            f.ServiceCalls.RemoveAll(c => c.Resolved);
        }

        public bool ExpandFuelDepot(SimContext ctx, out string reason)
        {
            reason = "";
            var dep = ctx.Data.Stations.FuelDepot;
            if (ctx.TryGetSystem<EconomySystem>(out var eco) && !eco.TrySpend(ctx, LedgerCategory.Construction, dep.UpgradeCostPer10000L, "Fuel depot +10,000 L", out reason)) return false;
            ctx.World.Fleet.Fuel.DieselCapacityL += 10000f;
            return true;
        }

        public bool BuyStation(SimContext ctx, string vehicleDefId, out string reason) => BuyNew(ctx, vehicleDefId, out reason) != null;

        // ------------------------------------------------------------------ tick
        public void Tick(SimContext ctx, float dt)
        {
            var time = ctx.Time;
            var f = ctx.World.Fleet;
            if (time.IsMinuteStart) UpdateShelter(ctx);
            if (time.IsHourStart) Hourly(ctx);
            if (time.IsDayStart) Daily(ctx);
        }

        private void UpdateShelter(SimContext ctx)
        {
            var f = ctx.World.Fleet;
            var scen = ctx.Sim.Scenario;
            var garage = GaragePos(ctx);
            int bays = f.Workshop.GarageBays;
            int used = 0;
            foreach (var v in ctx.World.Vehicles.List)
            {
                var def = ctx.Data.Vehicle(v.DefId);
                if (def != null && def.IsStationary) { v.Sheltered = true; continue; }
                bool inside = Math.Vec2.Distance(v.Pos, garage) <= scen.GarageRadiusM && !v.EngineOn;
                v.Sheltered = inside && used < bays;
                if (v.Sheltered) used++;
            }
        }

        private void Hourly(SimContext ctx)
        {
            AutoOrderFuel(ctx);
            var f = ctx.World.Fleet;
            var t = ctx.Tuning;
            ctx.TryGetSystem<EconomySystem>(out var eco);
            var vs = ctx.System<VehicleSystem>();
            // wages and competence for operators on shift
            foreach (var op in f.Operators)
            {
                if (op.IsPlayer) continue;
                var v = op.AssignedVehicleId >= 0 ? ctx.World.Vehicles.Get(op.AssignedVehicleId) : null;
                bool onShift = v != null && (v.EngineOn || v.TaskId >= 0 || v.Ai.Mode != AiMode.Idle);
                op.OnShift = onShift;
                if (onShift)
                {
                    op.HoursToday += 1f; op.HoursTotal += 1f;
                    op.Competence = MathF.Min(ctx.Data.Operators.CompetenceMax, op.Competence + ctx.Data.Operators.CompetenceGainPerHour);
                    eco?.Post(ctx, LedgerCategory.Wages, -op.WagePerHour, op.Name + " wage");
                    f.WagesToday += op.WagePerHour;
                    // accident risk
                    var def = v != null ? ctx.Data.Vehicle(v.DefId) : null;
                    if (def != null && v.EngineOn)
                    {
                        bool licensed = OperatorLicensedFor(ctx, op, def);
                        float p = licensed ? ctx.Data.Operators.LicensedAccidentProbabilityPerHour : ctx.Data.Operators.UnlicensedAccidentProbabilityPerHour;
                        if (ctx.Rng.Chance(p * MathUtil.Lerp(1.5f, 0.6f, MathUtil.InverseLerp(0.6f, 0.95f, op.Competence))))
                        {
                            double cost = ctx.Data.Operators.AccidentCostMean * MathUtil.Lerp(0.5f, 1.8f, ctx.Rng.NextFloat());
                            op.Accidents++;
                            var fl = new FailureState { Kind = FailureKind.Accident, Subsystem = WearSubsystem.Drivetrain, Tick = ctx.Time.Tick, RepairCost = cost, RepairHours = 12f, PartsKind = "drivetrainKit", Immobilising = true, Description = "accident (" + (licensed ? "operator error" : "unlicensed operator") + ")" };
                            v.Condition.ActiveFailures.Add(fl);
                            v.Condition.FailuresTotal++;
                            v.EngineOn = false; v.Speed = 0f; v.Input.Clear(); v.Ai.Mode = AiMode.Stranded;
                            ctx.Events.Publish(new AccidentEvent { VehicleId = v.Id, OperatorId = op.Id, Cost = cost, Description = fl.Description });
                            ctx.Events.Publish(new VehicleFailureEvent { VehicleId = v.Id, Failure = fl });
                            ctx.Sim.Log("Accident: " + op.Name + " rolled " + v.Name + " (" + fl.Description + "). Repair " + EconomySystem.Money(cost) + ".", LogLevel.Alert);
                        }
                    }
                }
            }
            // workshop progress: bays worth of jobs per hour
            var w = f.Workshop;
            var tier = ctx.Data.Stations.Workshop(w.Tier);
            int active = 0;
            foreach (var job in w.Jobs)
            {
                if (job.Status == ServiceJobStatus.Done || job.Status == ServiceJobStatus.Cancelled) continue;
                var v = ctx.World.Vehicles.Get(job.VehicleId);
                if (v == null) { job.Status = ServiceJobStatus.Cancelled; continue; }
                if (job.Status == ServiceJobStatus.SentOut)
                {
                    if (ctx.Time.Day >= job.ReturnDay) FinishJob(ctx, job, v, eco);
                    continue;
                }
                if (active >= System.Math.Max(1, tier.Bays)) continue;
                if (!string.IsNullOrEmpty(job.PartsKind) && job.PartsQty > 0 && (job.Kind == ServiceJobKind.Repair || job.Kind == ServiceJobKind.PreventiveMaintenance || job.Kind == ServiceJobKind.Rebuild))
                {
                    if (f.Parts.Get(job.PartsKind) < job.PartsQty)
                    {
                        if (job.Status != ServiceJobStatus.WaitingParts)
                        {
                            job.Status = ServiceJobStatus.WaitingParts;
                            bool ordered = false; foreach (var o in f.Parts.Orders) if (o.Kind == job.PartsKind) ordered = true;
                            if (f.AutoOrderParts && !ordered) OrderParts(ctx, job.PartsKind, job.PartsQty, out _);
                            ctx.Sim.Log(v.Name + " is waiting for parts (" + job.PartsKind + ").", LogLevel.Warning);
                        }
                        continue;
                    }
                    f.Parts.Stock[job.PartsKind] -= job.PartsQty;
                    job.PartsQty = 0;
                }
                job.Status = ServiceJobStatus.InProgress;
                active++;
                float speed = job.Kind == ServiceJobKind.PreventiveMaintenance ? tier.PmSpeed : tier.RepairSpeed;
                job.HoursDone += speed;
                v.EngineOn = false; v.Input.Clear(); v.Speed = 0f;
                if (job.HoursDone >= job.HoursRequired) FinishJob(ctx, job, v, eco);
            }
            w.Jobs.RemoveAll(j => (j.Status == ServiceJobStatus.Done || j.Status == ServiceJobStatus.Cancelled) && j.CreatedDay < ctx.Time.Day - 3);
            // deliveries
            for (int i = f.Fuel.Pending.Count - 1; i >= 0; i--)
            {
                var d = f.Fuel.Pending[i];
                if (ctx.Time.Day > d.ArriveDay || (ctx.Time.Day == d.ArriveDay && ctx.Time.HourOfDay >= d.ArriveHour))
                {
                    if (d.Fuel == "gasoline") f.Fuel.GasolineL = MathF.Min(f.Fuel.GasolineCapacityL, f.Fuel.GasolineL + d.Liters);
                    else f.Fuel.DieselL = MathF.Min(f.Fuel.DieselCapacityL, f.Fuel.DieselL + d.Liters);
                    f.Fuel.Pending.RemoveAt(i);
                    ctx.Events.Publish(new FuelDeliveredEvent { Liters = d.Liters, Cost = d.Cost });
                    ctx.Sim.Log("Fuel delivered: " + d.Liters + " L.");
                }
            }
            // PM due warnings
            foreach (var v in ctx.World.Vehicles.List)
            {
                var def = ctx.Data.Vehicle(v.DefId);
                if (def == null || def.IsStationary) continue;
                string key = v.Id.ToString();
                if (v.Condition.HoursSinceService >= def.ServiceIntervalHours && !f.PmDueWarned.Contains(key))
                {
                    f.PmDueWarned.Add(key);
                    ctx.Sim.Log(v.Name + " is due for its " + def.ServiceIntervalHours + " h service. Overdue machines wear faster.", LogLevel.Warning);
                }
                else if (v.Condition.HoursSinceService < def.ServiceIntervalHours) f.PmDueWarned.Remove(key);
            }
        }

        private void FinishJob(SimContext ctx, ServiceJob job, VehicleState v, EconomySystem eco)
        {
            var c = v.Condition;
            var t = ctx.Tuning;
            switch (job.Kind)
            {
                case ServiceJobKind.PreventiveMaintenance:
                    c.HoursSinceService = 0f; c.ServicesDone++;
                    float rec = t.F("fleet.pmWearRecovery");
                    c.WearEngine = MathF.Max(0f, c.WearEngine - rec); c.WearHydraulics = MathF.Max(0f, c.WearHydraulics - rec); c.WearDrivetrain = MathF.Max(0f, c.WearDrivetrain - rec); c.WearTracks = MathF.Max(0f, c.WearTracks - rec * 0.5f);
                    foreach (var m in v.Mounted) m.Wear = MathF.Max(0f, m.Wear - rec);
                    c.ServiceLog.Add("Day " + (ctx.Time.Day + 1) + ": PM service");
                    break;
                case ServiceJobKind.Repair:
                    c.ActiveFailures.RemoveAll(fl => fl.Subsystem == job.Subsystem);
                    c.SetWear(job.Subsystem, MathF.Max(0f, c.Wear(job.Subsystem) - t.F("fleet.repairWearRecovery")));
                    c.ServiceLog.Add("Day " + (ctx.Time.Day + 1) + ": " + job.Subsystem + " repair");
                    if (c.ActiveFailures.Count == 0) { v.Stranded = false; if (v.Ai.Mode == AiMode.Stranded) v.Ai.Mode = AiMode.Idle; }
                    break;
                case ServiceJobKind.Rebuild:
                    c.WearEngine = 0.05f; c.WearHydraulics = 0.05f; c.WearDrivetrain = 0.05f; c.WearTracks = 0.05f; c.HoursSinceRebuild = 0f; c.HoursSinceService = 0f; c.ActiveFailures.Clear(); c.HiddenDefects.Clear();
                    v.Stranded = false;
                    c.ServiceLog.Add("Day " + (ctx.Time.Day + 1) + ": rebuild");
                    break;
                case ServiceJobKind.Mount:
                    {
                        var vs = ctx.System<VehicleSystem>();
                        var existing = v.Slot(job.Slot);
                        if (existing != null) { ctx.World.Fleet.OwnedAttachments.Add(existing.DefId); vs.Dismount(ctx, v.Id, job.Slot, out _); }
                        if (vs.Mount(ctx, v.Id, job.AttachmentId, job.Slot, out string why)) ctx.World.Fleet.OwnedAttachments.Remove(job.AttachmentId);
                        else ctx.Sim.Log("Mount failed: " + why, LogLevel.Warning);
                        break;
                    }
                case ServiceJobKind.Dismount:
                    {
                        var vs = ctx.System<VehicleSystem>();
                        var existing = v.Slot(job.Slot);
                        if (existing != null && vs.Dismount(ctx, v.Id, job.Slot, out _)) ctx.World.Fleet.OwnedAttachments.Add(existing.DefId);
                        break;
                    }
            }
            job.Status = ServiceJobStatus.Done;
            ctx.World.Fleet.Workshop.JobsDoneTotal++;
            if (job.Cost > 0) eco?.Post(ctx, job.Kind == ServiceJobKind.PreventiveMaintenance ? LedgerCategory.VehicleService : LedgerCategory.VehicleRepair, -job.Cost, v.Name + ": " + job.Kind);
            ctx.Events.Publish(new ServiceJobEvent { JobId = job.Id, Status = ServiceJobStatus.Done });
            ctx.Sim.Log(v.Name + ": " + job.Kind + " done" + (job.Cost > 0 ? " (" + EconomySystem.Money(job.Cost) + ")" : "") + ".");
        }

        private void Daily(SimContext ctx)
        {
            var f = ctx.World.Fleet;
            var t = ctx.Tuning;
            var dep = ctx.Data.Stations.FuelDepot;
            ctx.TryGetSystem<EconomySystem>(out var eco);
            // fuel price random walk within the tuning range
            var pe = t.Entry("fuel.dieselPricePerL");
            f.Fuel.DieselPricePerL = MathUtil.Clamp(f.Fuel.DieselPricePerL * (1f + ctx.Rng.NextGaussian() * dep.PriceVolatility * t.F("fleet.fuelPriceDailyVolatilityScale")), float.IsNaN(pe.Min) ? 0.8f : pe.Min, float.IsNaN(pe.Max) ? 2f : pe.Max);
            // parts arrivals
            for (int i = f.Parts.Orders.Count - 1; i >= 0; i--)
            {
                var o = f.Parts.Orders[i];
                if (ctx.Time.Day >= o.ArriveDay) { f.Parts.Stock[o.Kind] = f.Parts.Get(o.Kind) + o.Qty; f.Parts.Orders.RemoveAt(i); ctx.Sim.Log("Parts arrived: " + o.Qty + " x " + o.Kind + "."); }
            }
            // insurance per machine, leases monthly, rentals expiring, training
            foreach (var v in ctx.World.Vehicles.List)
            {
                var def = ctx.Data.Vehicle(v.DefId);
                if (def == null) continue;
                if (!v.Rented) eco?.Post(ctx, LedgerCategory.Insurance, -def.InsuranceAnnual / 365.0, v.Name + " insurance (daily)");
            }
            if (ctx.Time.Day % 30 == 0 && ctx.Time.Day > 0)
            {
                foreach (var l in f.Leases) { var v = ctx.World.Vehicles.Get(l.VehicleId); if (v != null) eco?.Post(ctx, LedgerCategory.Lease, -l.Monthly, v.Name + " lease"); }
                var tier = ctx.Data.Stations.Workshop(f.Workshop.Tier);
                if (tier.MonthlyCost > 0) eco?.Post(ctx, LedgerCategory.Misc, -tier.MonthlyCost, "Workshop tier " + tier.Tier + " monthly");
            }
            for (int i = ctx.World.Vehicles.List.Count - 1; i >= 0; i--)
            {
                var v = ctx.World.Vehicles.List[i];
                if (v.Rented && v.RentalEndDay >= 0 && ctx.Time.Day >= v.RentalEndDay)
                {
                    ctx.Sim.Log("Rental period over: " + v.Name + " returned.");
                    var op = f.Operator(v.OperatorId); if (op != null) op.AssignedVehicleId = -1;
                    ctx.System<VehicleSystem>().Remove(ctx, v.Id);
                }
            }
            foreach (var op in f.Operators)
            {
                op.HoursToday = 0f;
                if (op.TrainingLicensePending >= 0 && ctx.Time.Day >= op.TrainingDoneDay)
                {
                    var lic = (OperatorLicense)op.TrainingLicensePending;
                    if (!op.Licenses.Contains(lic)) op.Licenses.Add(lic);
                    op.TrainingLicensePending = -1;
                    op.WagePerHour = WageFor(ctx, op);
                    ctx.Sim.Log(op.Name + " is now certified: " + lic + ".");
                }
                // idle staff still cost a minimum shift
                if (!op.IsPlayer && op.HoursToday < t.F("fleet.shiftMinHoursPaid")) { double idle = op.WagePerHour * t.F("fleet.shiftMinHoursPaid") * 0.5; eco?.Post(ctx, LedgerCategory.Wages, -idle, op.Name + " retainer"); }
            }
            f.WagesToday = 0;
            if (ctx.Time.Day - f.MarketRefreshDay >= t.I("fleet.marketRefreshDays")) RefreshMarket(ctx);
            if (ctx.Time.Day - f.CandidatesRefreshDay >= t.I("fleet.candidateRefreshDays")) RefreshCandidates(ctx);
            f.FleetValue = 0; foreach (var v in ctx.World.Vehicles.List) f.FleetValue += ResaleValue(ctx, v);
        }
    }
}
