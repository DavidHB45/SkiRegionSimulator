using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Economy;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Snow;
using AlpineSim.Core.Terrain;
using AlpineSim.Core.Weather;

namespace AlpineSim.Core.Lifts
{
    /// <summary>
    /// Lift operations: queue simulation with maze/singles line, loading at rated pph (capacity x
    /// options x condition), ride time = length / ride speed (+ load time), unloading to the top node,
    /// wind / lightning / cold holds per type, MTBF breakdowns with repair downtime, periodic
    /// inspections, hourly opex/power/staff posting to the economy, lift nodes and ramp zones in the
    /// piste network. Ticks after Roads, before Construction.
    ///
    /// Hold ordering as wind rises: surface &lt; fixed-grip &lt; detachable &lt; monocable gondola &lt; funitel &lt; 3S/tram.
    /// </summary>
    public sealed class LiftSystem : ISimSystem
    {
        public string Name => "Lifts";

        private readonly Dictionary<int, List<RiderCohort>> _arrivals = new Dictionary<int, List<RiderCohort>>();
        private float _baseElevation;

        public void Initialize(SimContext ctx, bool newGame)
        {
            var world = ctx.World;
            if (world.Lifts == null) world.Lifts = new LiftsState();
            _baseElevation = ctx.Terrain.SampleHeight(ctx.Sim.Scenario.BaseArea.Pos);
            if (newGame)
            {
                var scen = ctx.Sim.Scenario;
                foreach (var l in scen.Lifts)
                {
                    if (l.ExistsFromAct > scen.StartingAct) continue;
                    if (ctx.Data.LiftType(l.TypeId) == null) { ctx.Sim.Log("Scenario lift " + l.Id + " has unknown type " + l.TypeId, LogLevel.Warning); continue; }
                    var lift = Build(ctx, l.TypeId, new Vec2(l.BottomX, l.BottomY), new Vec2(l.TopX, l.TopY), l.Name, l.Options, l.Id, true);
                    lift.Condition = MathUtil.Clamp01(l.ConditionPct / 100f);
                    lift.DaysSinceInspection = 0;
                    lift.DaysSinceAnnualInspection = l.AgeDays % 365;
                    lift.Status = LiftStatus.Closed;
                }
            }
            foreach (var l in world.Lifts.Lifts) l.Type = ctx.Data.LiftType(l.TypeId);
            if (ctx.TryGetSystem<EconomySystem>(out var eco)) eco.LiftValuation = BookValue;
        }

        private double BookValue(SimContext ctx)
        {
            double v = 0;
            foreach (var l in ctx.World.Lifts.Lifts) if (l.IsBuilt) v += l.BookValue;
            return v;
        }

        // ------------------------------------------------------------------ building
        public IReadOnlyList<LiftState> All(SimContext ctx) => ctx.World.Lifts.Lifts;
        public LiftState Get(SimContext ctx, int id) => ctx.World.Lifts.Get(id);

        public LiftState Build(SimContext ctx, string typeId, Vec2 bottom, Vec2 top, string name, List<string> options, string scenarioId, bool prebuilt)
        {
            var type = ctx.Data.RequireLiftType(typeId);
            var state = ctx.World.Lifts;
            var terrain = ctx.Terrain;
            var lift = new LiftState
            {
                Id = state.NextId++,
                TypeId = typeId,
                Type = type,
                Name = string.IsNullOrEmpty(name) ? type.DisplayName : name,
                Bottom = bottom,
                Top = top,
                BuildDay = ctx.Time.Day,
                PrebuiltByScenario = prebuilt,
                Status = prebuilt ? LiftStatus.Closed : LiftStatus.UnderConstruction,
            };
            if (options != null) lift.Options.AddRange(options);
            lift.LengthM = Vec2.Distance(bottom, top);
            lift.VerticalM = terrain.SampleHeight(top) - terrain.SampleHeight(bottom);
            lift.Towers = PlaceTowers(ctx, type, bottom, top, out float maxSpan, out float maxGrade);
            lift.MaxSpanM = maxSpan;
            lift.MaxGradeDeg = maxGrade;
            float spacing = type.CarrierSpacingM > 0f ? type.CarrierSpacingM : MathF.Max(8f, type.LineSpeedMs * 3600f * type.SeatsOrCabinCapacity / MathF.Max(1f, type.CapacityPph));
            lift.Carriers = type.RopeConfiguration == RopeConfig.Reversible ? 2 : System.Math.Max(2, (int)MathF.Ceiling(2f * lift.LengthM / spacing));
            lift.CapexTotal = Capex(ctx, type, lift.LengthM, lift.Towers.Count, lift.Carriers, lift.Options);
            lift.BookValue = lift.CapexTotal;
            // nodes: the scenario names nodes "lift:<scenarioId>:top"; runtime lifts use "lift:<id>:..."
            string key = string.IsNullOrEmpty(scenarioId) ? lift.Id.ToString() : scenarioId;
            var net = ctx.World.Pistes;
            var scen = ctx.Sim.Scenario;
            float baseR = scen.BaseAreaRadiusM + 160f;
            lift.BottomNodeId = Vec2.Distance(bottom, scen.BaseArea.Pos) <= baseR ? "base" : "lift:" + key + ":bottom";
            lift.TopNodeId = "lift:" + key + ":top";
            if (lift.BottomNodeId != "base") net.EnsureNode(lift.BottomNodeId, NodeKind.LiftBottom, bottom, lift.Id);
            else net.EnsureNode("lift:" + key + ":bottom", NodeKind.LiftBottom, bottom, lift.Id);
            net.EnsureNode(lift.TopNodeId, NodeKind.LiftTop, top, lift.Id);
            // ramp zones (allocate snow so ramps can drift in and be blown out)
            EnsureRampZone(ctx, "ramp:" + key + ":bottom", bottom, type.IsSurface ? 10f : 20f);
            EnsureRampZone(ctx, "ramp:" + key + ":top", top, type.IsSurface ? 8f : 18f);
            state.Lifts.Add(lift);
            _arrivals[lift.Id] = new List<RiderCohort>();
            ctx.Events.Publish(new LiftBuiltEvent { LiftId = lift.Id });
            return lift;
        }

        private static void EnsureRampZone(SimContext ctx, string id, Vec2 pos, float radius)
        {
            var net = ctx.World.Pistes;
            if (net.Zone(id) != null) return;
            var z = new SurfaceZone { Id = id, Kind = ZoneKind.LiftRamp, RadiusM = radius };
            z.Points.Add(pos);
            net.Zones.Add(z);
            var grid = ctx.World.Snow;
            PisteNetworkBuilder.StampDisc(grid, pos, radius, cell => { if (grid.Surface[cell] == (byte)SurfaceType.OffPiste) grid.Surface[cell] = (byte)SurfaceType.LiftRamp; });
            PisteNetworkBuilder.RebuildDerived(ctx);
        }

        /// <summary>Tower sites along the line at the type's spacing, nudged off NoFoundation cells; reports the longest span and the steepest grade.</summary>
        public static List<Vec2> PlaceTowers(SimContext ctx, LiftTypeDef type, Vec2 bottom, Vec2 top, out float maxSpanM, out float maxGradeDeg)
        {
            var terrain = ctx.Terrain;
            var towers = new List<Vec2>();
            float length = Vec2.Distance(bottom, top);
            Vec2 dir = (top - bottom).Normalized;
            float spacing = MathF.Max(type.MinTowerSpacingM, 1000f / MathF.Max(0.5f, type.TowersPerKm));
            int count = System.Math.Max(1, (int)MathF.Round(length / spacing) - 1);
            float step = length / (count + 1);
            maxGradeDeg = 0f;
            if (type.IsSurface)
            {
                // surface lifts tow skiers on the ground: the grade that matters is the ground grade, smoothed over a 30 m window
                for (float s = 0f; s <= length; s += 10f)
                {
                    float g = 0f; int n = 0;
                    for (float o = -15f; o <= 15f; o += 5f) { float u = MathUtil.Clamp(s + o, 0f, length); g += terrain.GradeAlongDeg(bottom.X + dir.X * u, bottom.Y + dir.Y * u, dir); n++; }
                    maxGradeDeg = MathF.Max(maxGradeDeg, MathF.Abs(g / n));
                }
            }
            float last = 0f;
            maxSpanM = 0f;
            for (int i = 1; i <= count; i++)
            {
                float s = i * step;
                float placed = -1f;
                for (float d = 0f; d <= step * 0.5f; d += 5f)
                {
                    float a = s - d, b = s + d;
                    if (a > last + type.MinTowerSpacingM * 0.5f && a < length && !terrain.HasFlag(bottom.X + dir.X * a, bottom.Y + dir.Y * a, TerrainFlags.NoFoundation)) { placed = a; break; }
                    if (b < length && !terrain.HasFlag(bottom.X + dir.X * b, bottom.Y + dir.Y * b, TerrainFlags.NoFoundation)) { placed = b; break; }
                }
                if (placed < 0f) continue;
                towers.Add(bottom + dir * placed);
                maxSpanM = MathF.Max(maxSpanM, placed - last);
                last = placed;
            }
            maxSpanM = MathF.Max(maxSpanM, length - last);
            if (towers.Count == 0) maxSpanM = length;
            if (!type.IsSurface)
            {
                // aerial lifts: the rope grade between consecutive supports (terminals and towers)
                Vec2 prev = bottom; float prevH = terrain.SampleHeight(bottom);
                for (int i = 0; i <= towers.Count; i++)
                {
                    Vec2 next = i < towers.Count ? towers[i] : top;
                    float h = terrain.SampleHeight(next);
                    float d = Vec2.Distance(prev, next);
                    if (d > 1f) maxGradeDeg = MathF.Max(maxGradeDeg, MathF.Abs(MathF.Atan((h - prevH) / d) * MathUtil.Rad2Deg));
                    prev = next; prevH = h;
                }
            }
            return towers;
        }

        public static double Capex(SimContext ctx, LiftTypeDef type, float lengthM, int towers, int carriers, List<string> options)
        {
            double capex = type.CapexPerKm * lengthM / 1000.0 + type.CapexPerTower * towers + type.CapexTerminalDrive + type.CapexTerminalReturn + type.CapexPerCarrier * carriers + type.CabinBarnCapex;
            float pct = 0f;
            if (options != null) foreach (var o in options) { var od = ctx.Data.LiftOption(o); if (od != null) pct += od.CapexPct; }
            return capex * (1.0 + pct / 100.0);
        }

        public bool Remove(SimContext ctx, int id)
        {
            var l = Get(ctx, id);
            if (l == null) return false;
            ctx.World.Lifts.Lifts.Remove(l);
            _arrivals.Remove(id);
            return true;
        }

        /// <summary>Player or automatic open/close. A lift the player closed stays closed until reopened; the resort's hours open and close the rest.</summary>
        public void SetOpen(SimContext ctx, int id, bool open, bool byPlayer = true)
        {
            var l = Get(ctx, id);
            if (l == null || !l.IsBuilt) return;
            if (byPlayer) l.PlayerClosed = !open;
            if (open && l.Status == LiftStatus.Closed) SetStatus(ctx, l, LiftStatus.Open, "opened");
            else if (!open && (l.Status == LiftStatus.Open || l.Status == LiftStatus.WindHold || l.Status == LiftStatus.ColdHold || l.Status == LiftStatus.LightningHold))
            {
                // unload riders at the top, empty the queue
                foreach (var r in l.Riders) _arrivals[l.Id].Add(r);
                l.Riders.Clear();
                SetStatus(ctx, l, LiftStatus.Closed, "closed");
            }
        }

        public void SetSinglesLine(SimContext ctx, int id, bool on) { var l = Get(ctx, id); if (l != null) l.SinglesLine = on; }

        private void SetStatus(SimContext ctx, LiftState l, LiftStatus status, string reason)
        {
            if (l.Status == status) return;
            l.Status = status;
            l.StatusReason = reason;
            ctx.Events.Publish(new LiftStatusChangedEvent { LiftId = l.Id, Status = status, Reason = reason });
        }

        // ------------------------------------------------------------------ guest interface
        public void Enqueue(SimContext ctx, int liftId, int agentId, int guests)
        {
            var l = Get(ctx, liftId);
            if (l == null) return;
            l.Queue.Add(new QueueEntry { AgentId = agentId, Guests = guests, JoinTick = ctx.Time.Tick });
            l.QueueGuests += guests;
        }

        public bool LeaveQueue(SimContext ctx, int liftId, int agentId)
        {
            var l = Get(ctx, liftId);
            if (l == null) return false;
            for (int i = 0; i < l.Queue.Count; i++)
                if (l.Queue[i].AgentId == agentId) { l.QueueGuests -= l.Queue[i].Guests; l.Queue.RemoveAt(i); return true; }
            return false;
        }

        public int DequeueArrivals(SimContext ctx, int liftId, List<RiderCohort> into)
        {
            if (!_arrivals.TryGetValue(liftId, out var list)) { list = new List<RiderCohort>(); _arrivals[liftId] = list; }
            int n = list.Count;
            into.AddRange(list);
            list.Clear();
            return n;
        }

        public int QueueGuests(SimContext ctx, int liftId) { var l = Get(ctx, liftId); return l != null ? l.QueueGuests : 0; }

        public float ExpectedWaitMin(SimContext ctx, int liftId)
        {
            var l = Get(ctx, liftId);
            if (l == null) return 0f;
            float pph = EffectiveCapacityPph(ctx, l);
            if (!l.IsRunning) return 999f;
            return pph > 1f ? l.QueueGuests / (pph / 60f) : 999f;
        }

        public float RideTimeS(SimContext ctx, LiftState lift)
        {
            var type = lift.Type ?? ctx.Data.LiftType(lift.TypeId);
            return lift.LengthM / MathF.Max(0.3f, type.RideSpeedMs) + type.LoadTimeS;
        }

        public float EffectiveCapacityPph(SimContext ctx, LiftState lift)
        {
            var type = lift.Type ?? ctx.Data.LiftType(lift.TypeId);
            float pct = 0f;
            foreach (var o in lift.Options) { var od = ctx.Data.LiftOption(o); if (od != null) pct += od.CapacityPct; }
            var t = ctx.Tuning;
            float cap = type.CapacityPph * (1f + pct / 100f);
            cap *= lift.SinglesLine ? t.F("lifts.singlesLineCapacityFactor") : 1f;
            cap *= lift.Maze ? 1f : t.F("lifts.noMazeCapacityFactor");
            cap *= MathUtil.Lerp(t.F("lifts.capacityAtZeroCondition"), 1f, lift.Condition);
            return cap;
        }

        public float WindHoldKmh(SimContext ctx, LiftState lift)
        {
            var type = lift.Type ?? ctx.Data.LiftType(lift.TypeId);
            float bonus = 0f;
            foreach (var o in lift.Options) { var od = ctx.Data.LiftOption(o); if (od != null) bonus += od.WindHoldBonusKmh; }
            return type.WindHoldKmh + bonus;
        }

        /// <summary>Wind at the top terminal: base wind plus exposure with elevation.</summary>
        public float WindAtTopKmh(SimContext ctx, LiftState lift, float baseWindKmh)
        {
            float topElev = ctx.Terrain.SampleHeight(lift.Top);
            return baseWindKmh * (1f + ctx.Tuning.F("lifts.windExposurePer100m") * MathF.Max(0f, topElev - _baseElevation) / 100f);
        }

        public bool WouldHold(SimContext ctx, LiftState lift, float windKmh, bool lightning, float tempC, out string reason)
        {
            var type = lift.Type ?? ctx.Data.LiftType(lift.TypeId);
            reason = "";
            if (WindAtTopKmh(ctx, lift, windKmh) >= WindHoldKmh(ctx, lift)) { reason = "wind hold"; return true; }
            if (lightning && type.LightningHold) { reason = "lightning hold"; return true; }
            float topTemp = WetBulb.TempAtElevation(tempC, _baseElevation, ctx.Terrain.SampleHeight(lift.Top), ctx.World.Weather.LapseRateCPer100m);
            if (topTemp <= type.ColdWeatherLimitC) { reason = "cold hold"; return true; }
            return false;
        }

        public void LiftsFromNode(SimContext ctx, string nodeId, List<LiftState> into)
        {
            foreach (var l in ctx.World.Lifts.Lifts) if (l.BottomNodeId == nodeId && l.IsBuilt) into.Add(l);
        }

        // ------------------------------------------------------------------ tick
        public void Tick(SimContext ctx, float dt)
        {
            var lifts = ctx.World.Lifts.Lifts;
            var time = ctx.Time;
            var w = ctx.World.Weather.Current;
            var t = ctx.Tuning;
            bool minute = time.IsMinuteStart;
            bool hour = time.IsHourStart;
            ctx.TryGetSystem<EconomySystem>(out var eco);
            // resort hours: lifts open when the resort opens and close after the last ride, unless the player closed them
            bool opening = hour && time.HourOfDay == t.I("simulation.resortOpenHour");
            bool closing = hour && time.HourOfDay == t.I("simulation.resortCloseHour");
            for (int i = 0; i < lifts.Count; i++)
            {
                var l = lifts[i];
                var type = l.Type ?? (l.Type = ctx.Data.LiftType(l.TypeId));
                if (type == null || !l.IsBuilt) continue;
                if (!_arrivals.ContainsKey(l.Id)) _arrivals[l.Id] = new List<RiderCohort>();
                if (opening && l.Status == LiftStatus.Closed && !l.PlayerClosed) SetOpen(ctx, l.Id, true, false);
                if (closing && !l.NightLighting && (l.Status == LiftStatus.Open || l.Status == LiftStatus.WindHold || l.Status == LiftStatus.ColdHold || l.Status == LiftStatus.LightningHold)) SetOpen(ctx, l.Id, false, false);

                // breakdown / inspection timers
                if (l.Status == LiftStatus.Breakdown || l.Status == LiftStatus.Inspection)
                {
                    if (time.Tick >= l.BreakdownUntilTick) SetStatus(ctx, l, LiftStatus.Open, l.Status == LiftStatus.Breakdown ? "repaired" : "inspection passed");
                    else continue;
                }

                // holds
                if (minute && (l.Status == LiftStatus.Open || l.Status == LiftStatus.WindHold || l.Status == LiftStatus.LightningHold || l.Status == LiftStatus.ColdHold))
                {
                    bool hold = WouldHold(ctx, l, w.WindKmh, w.Lightning, w.TempC, out string reason);
                    if (hold)
                    {
                        var st = reason == "wind hold" ? LiftStatus.WindHold : (reason == "lightning hold" ? LiftStatus.LightningHold : LiftStatus.ColdHold);
                        if (l.Status != st) { SetStatus(ctx, l, st, reason); ctx.Sim.Log(l.Name + " on " + reason + ".", LogLevel.Warning); }
                    }
                    else if (l.Status != LiftStatus.Open) SetStatus(ctx, l, LiftStatus.Open, "hold lifted");
                }

                if (l.Status == LiftStatus.Open)
                {
                    // loading
                    float pph = EffectiveCapacityPph(ctx, l);
                    l.LoadAccumulator += pph / 3600f * dt;
                    float carrier = MathF.Max(1f, type.SeatsOrCabinCapacity);
                    if (l.LoadAccumulator > carrier * 3f) l.LoadAccumulator = carrier * 3f;
                    float ride = RideTimeS(ctx, l);
                    while (l.Queue.Count > 0 && l.LoadAccumulator >= MathF.Min(l.Queue[0].Guests, carrier))
                    {
                        var q = l.Queue[0];
                        l.Queue.RemoveAt(0);
                        l.QueueGuests -= q.Guests;
                        l.LoadAccumulator -= q.Guests;
                        l.Riders.Add(new RiderCohort { AgentId = q.AgentId, Guests = q.Guests, BoardTick = time.Tick, ArriveTick = time.Tick + (long)(ride * SimTime.TicksPerSecond) });
                        l.TotalLoaded += q.Guests;
                        l.LoadedToday += q.Guests;
                        float waitMin = (time.Tick - q.JoinTick) / (float)SimTime.TicksPerMinute;
                        l.QueueSampleSum += waitMin * q.Guests;
                        l.QueueSamples += q.Guests;
                    }
                    if (l.Queue.Count == 0 && l.LoadAccumulator > carrier) l.LoadAccumulator = carrier;
                    // unloading
                    for (int r = l.Riders.Count - 1; r >= 0; r--)
                    {
                        var rc = l.Riders[r];
                        if (time.Tick >= rc.ArriveTick)
                        {
                            l.Riders.RemoveAt(r);
                            l.TotalUnloaded += rc.Guests;
                            _arrivals[l.Id].Add(rc);
                            ctx.Events.Publish(new LiftUnloadEvent { LiftId = l.Id, AgentId = rc.AgentId, Guests = rc.Guests });
                        }
                    }
                    l.HoursOperated += dt / 3600f;
                    l.HoursOperatedToday += dt / 3600f;
                    l.HoursSinceInspection += dt / 3600f;
                    // breakdown roll (per hour of operation)
                    if (minute)
                    {
                        float rate = 1f / MathF.Max(50f, type.MtbfHours) * MathUtil.Lerp(t.F("lifts.breakdownRateAtZeroCondition"), 1f, l.Condition) / 60f;
                        if (ctx.Rng.Chance(rate))
                        {
                            float hours = t.F("lifts.breakdownRepairHoursMean") * MathUtil.Lerp(0.5f, 1.5f, ctx.Rng.NextFloat());
                            l.BreakdownUntilTick = time.Tick + (long)(hours * SimTime.TicksPerHour);
                            l.Breakdowns++;
                            double cost = l.CapexTotal * t.F("lifts.breakdownCostFracOfCapex");
                            foreach (var rc in l.Riders) _arrivals[l.Id].Add(rc); // rope evac / restart: riders unload
                            l.Riders.Clear();
                            SetStatus(ctx, l, LiftStatus.Breakdown, "breakdown");
                            eco?.Post(ctx, LedgerCategory.LiftMaintenance, -cost, l.Name + " breakdown repair");
                            ctx.Events.Publish(new LiftBreakdownEvent { LiftId = l.Id, DowntimeHours = hours, Cost = cost });
                            ctx.Sim.Log(l.Name + " has broken down; " + hours.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " h to repair.", LogLevel.Alert);
                        }
                    }
                }

                // riders on a held lift still arrive (slowly) so nobody is stuck forever: handled by SetOpen/breakdown; queue patience is the guest system's

                if (hour && eco != null)
                {
                    bool operated = l.HoursOperatedToday > 0f && (l.Status == LiftStatus.Open || l.Status == LiftStatus.WindHold || l.Status == LiftStatus.LightningHold || l.Status == LiftStatus.ColdHold);
                    float kwh = operated ? type.PowerDrawKw * MathUtil.Lerp(0.35f, 1f, MathUtil.Clamp01(l.LoadedToday > 0 ? 0.7f : 0.2f)) : type.StandbyPowerKw;
                    l.PowerKwhToday += kwh;
                    eco.Post(ctx, LedgerCategory.Electricity, -kwh * ctx.Data.Economy.ElectricityPricePerKwh, l.Name + " power");
                    if (operated)
                    {
                        eco.Post(ctx, LedgerCategory.LiftOpex, -type.OpexPerOperatingHour * (1f + OptionOpexPct(ctx, l) / 100f), l.Name + " operating hour");
                        float wage = ctx.Data.Economy.Wage("liftOperator");
                        eco.Post(ctx, LedgerCategory.Wages, -wage * type.StaffRequired.Total, l.Name + " lift crew");
                    }
                }
                if (time.IsDayStart)
                {
                    // daily maintenance accrual, inspection clock, condition drift
                    if (eco != null) eco.Post(ctx, LedgerCategory.LiftMaintenance, -l.CapexTotal * type.AnnualMaintenancePct / 100.0 / 365.0, l.Name + " maintenance (daily)");
                    l.DaysSinceInspection++;
                    l.DaysSinceAnnualInspection++;
                    l.Condition = MathUtil.Clamp01(l.Condition - t.F("lifts.conditionLossPerOperatingHour") * l.HoursOperatedToday + t.F("lifts.conditionRecoveryPerDay"));
                    l.BookValue = System.Math.Max(l.CapexTotal * 0.1, l.BookValue - l.CapexTotal / (t.F("lifts.depreciationYears") * 365.0));
                    if (l.DaysSinceInspection >= type.InspectionIntervalDays && l.IsBuilt)
                    {
                        l.DaysSinceInspection = 0;
                        l.BreakdownUntilTick = time.Tick + (long)(t.F("lifts.inspectionHours") * SimTime.TicksPerHour);
                        l.Condition = MathUtil.Clamp01(l.Condition + t.F("lifts.inspectionConditionGain"));
                        foreach (var rc in l.Riders) _arrivals[l.Id].Add(rc);
                        l.Riders.Clear();
                        SetStatus(ctx, l, LiftStatus.Inspection, "periodic inspection");
                        eco?.Post(ctx, LedgerCategory.Inspection, -l.CapexTotal * t.F("lifts.inspectionCostFracOfCapex"), l.Name + " periodic inspection");
                    }
                    l.AvgQueueMinToday = l.QueueSamples > 0 ? l.QueueSampleSum / l.QueueSamples : 0f;
                    l.QueueSampleSum = 0f; l.QueueSamples = 0; l.LoadedToday = 0; l.HoursOperatedToday = 0f; l.PowerKwhToday = 0f;
                }
            }
        }

        private static float OptionOpexPct(SimContext ctx, LiftState l)
        {
            float pct = 0f;
            foreach (var o in l.Options) { var od = ctx.Data.LiftOption(o); if (od != null) pct += od.OpexPct; }
            return pct;
        }
    }
}
