using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Economy;
using AlpineSim.Core.Lifts;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Roads;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Snow;

namespace AlpineSim.Core.Guests
{
    /// <summary>
    /// Guests as cohorts (simulation.guestsPerAgent per agent). Daily demand = f(day of week,
    /// holidays, snow report, weather, PQI, ticket price, reputation, competition, act demandBase).
    /// Cohorts arrive over the morning, pick a lift, queue, ride, choose a run at the top by their
    /// archetype's difficulty window, ski it (SnowOps.SkierPass on the cells they cross with their
    /// lateral preference), buy food and rentals, and leave when their stay is over. Satisfaction
    /// weights PQI, queue time, terrain match, comfort/weather, food, parking and price by archetype;
    /// at day close the mean enters a lagging reputation. Ticks after Construction, before Vehicles.
    /// </summary>
    public sealed class GuestSystem : ISimSystem
    {
        public string Name => "Guests";

        private readonly List<RiderCohort> _arrivals = new List<RiderCohort>();
        private readonly List<LiftState> _liftTmp = new List<LiftState>();
        private readonly List<PisteState> _pisteTmp = new List<PisteState>();
        private readonly List<GuestAgent> _pending = new List<GuestAgent>();
        private int _pendingDay = -1;
        private readonly Dictionary<int, int> _lastCell = new Dictionary<int, int>();

        public void Initialize(SimContext ctx, bool newGame)
        {
            _satNeutral = ctx.Tuning.F("guests.satNeutral");
            _satBlend = ctx.Tuning.F("guests.satBlend");
            var g = ctx.World.Guests;
            if (g == null) ctx.World.Guests = g = new GuestPopulationState();
            if (newGame)
            {
                g.Reputation = ctx.Sim.Scenario.StartReputation > 0f ? ctx.Sim.Scenario.StartReputation : ctx.Data.Economy.ReputationStart;
                g.SnowReportScore = ctx.World.Pistes != null ? ctx.World.Pistes.ResortPqi : 50f;
            }
        }

        public IReadOnlyList<GuestAgent> Agents(SimContext ctx) => ctx.World.Guests.Agents;
        public float Reputation(SimContext ctx) => ctx.World.Guests.Reputation;

        public bool IsOpen(SimContext ctx)
        {
            int h = ctx.Time.HourOfDay;
            return ctx.World.Guests.ResortOpenToday && h >= ctx.Tuning.I("simulation.resortOpenHour") && h < ctx.Tuning.I("simulation.resortCloseHour");
        }

        public float LiveSatisfaction(SimContext ctx)
        {
            var agents = ctx.World.Guests.Agents;
            if (agents.Count == 0) return 0f;
            float sum = 0f; int n = 0;
            foreach (var a in agents) { sum += a.Satisfaction * a.Guests; n += a.Guests; }
            return n > 0 ? sum / n : 0f;
        }

        public void SetResortOpen(SimContext ctx, bool open) => ctx.World.Guests.ResortOpenToday = open;

        // ------------------------------------------------------------------ demand
        public int ComputeDailyDemand(SimContext ctx, int day)
        {
            var e = ctx.Data.Economy;
            var g = ctx.World.Guests;
            var act = e.Act(ctx.World.Economy.Act);
            var t = ctx.Tuning;
            float demand = act.DemandBase;
            int dow = (ctx.Time.StartDayOfWeek + day) % 7;
            bool weekend = dow >= 5;
            int seasonDay = ctx.Time.StartSeasonDay + day;
            bool holiday = ctx.Data.Guests.HolidaySeasonDays.Contains(seasonDay);
            float dayMult = 0f, share = 0f;
            foreach (var a in ctx.Data.Guests.Archetypes) { dayMult += a.Share * (holiday ? a.HolidayMultiplier : (weekend ? a.WeekendMultiplier : 1f)); share += a.Share; }
            if (share > 0f) demand *= dayMult / share;
            float priceBase = MathF.Max(1f, act.TicketPriceBase);
            demand *= MathF.Pow(MathF.Max(0.2f, ctx.World.Economy.TicketPrice / priceBase), -e.DemandPriceElasticity);
            demand *= MathF.Pow(MathUtil.Clamp(g.SnowReportScore / t.F("guests.snowReportReferencePqi"), 0.2f, 1.6f), e.DemandPqiElasticity);
            demand *= MathF.Pow(MathUtil.Clamp(g.Reputation / 50f, 0.2f, 2f), e.DemandReputationElasticity);
            var w = ctx.World.Weather.Current;
            float weather = 1f;
            if (w.WindKmh > t.F("guests.stormWindKmh") || w.SnowfallCmPerHour > t.F("guests.stormSnowCmPerHour")) weather -= e.DemandWeatherPenalty;
            if (w.TempC < t.F("guests.bitterColdC")) weather -= e.DemandWeatherPenalty * 0.6f;
            if (ctx.World.Weather.SeasonSnowfallCm > 0f && g.SnowReportScore > t.F("guests.freshSnowBonusPqi")) weather += e.DemandSnowReportBonus * 0.5f;
            demand *= MathF.Max(0.1f, weather);
            demand *= e.RegionalCompetitionFactor;
            // capacity of the mountain: guests do not come to a resort with no open lift
            int openLifts = 0;
            foreach (var l in ctx.World.Lifts.Lifts) if (l.IsBuilt) openLifts++;
            if (openLifts == 0) demand = 0f;
            return (int)MathF.Round(MathF.Max(0f, demand));
        }

        // ------------------------------------------------------------------ tick
        public void Tick(SimContext ctx, float dt)
        {
            var g = ctx.World.Guests;
            var time = ctx.Time;
            var t = ctx.Tuning;
            int open = t.I("simulation.resortOpenHour");
            int close = t.I("simulation.resortCloseHour");

            if (time.IsDayStart)
            {
                g.ResortOpenToday = true;
                g.TodayDemand = 0; g.TodayArrived = 0; g.TodayDeparted = 0; g.TodayTurnedAway = 0;
                g.TodaySatisfactionSum = 0; g.TodaySatisfactionCount = 0; g.TodayQueueMinSum = 0f; g.TodayQueueSamples = 0; g.TodayLaps = 0;
                _pending.Clear(); _pendingDay = -1;
            }
            if (time.IsHourStart && time.HourOfDay == open - 1)
            {
                // morning snow report: yesterday's grooming shows up in the demand of the day
                g.SnowReportScore = ctx.World.Pistes.ResortPqi;
            }
            if (time.IsHourStart && time.HourOfDay == open && g.ResortOpenToday)
            {
                PlanDay(ctx);
                foreach (var l in ctx.World.Lifts.Lifts) if (l.Status == LiftStatus.Closed && l.IsBuilt && !RampBlocked(ctx, l)) ctx.System<LiftSystem>().SetOpen(ctx, l.Id, true);
            }
            if (time.IsHourStart && time.HourOfDay == close)
            {
                foreach (var a in g.Agents) if (a.Phase != GuestPhase.Leaving && a.Phase != GuestPhase.Gone) Leave(ctx, a, "closing");
                foreach (var l in ctx.World.Lifts.Lifts) if (l.Status != LiftStatus.Closed && l.IsBuilt && l.Status != LiftStatus.UnderConstruction) ctx.System<LiftSystem>().SetOpen(ctx, l.Id, false);
                CloseDay(ctx);
            }

            // arrivals
            if (_pendingDay == time.Day && _pending.Count > 0 && time.IsSecondStart)
            {
                float hour = time.HourOfDayF;
                for (int i = _pending.Count - 1; i >= 0; i--)
                {
                    var a = _pending[i];
                    if (a.ArriveTick <= time.Tick) { _pending.RemoveAt(i); Arrive(ctx, a); }
                }
            }

            // agents
            var agents = g.Agents;
            var lifts = ctx.System<LiftSystem>();
            // lift arrivals
            foreach (var l in ctx.World.Lifts.Lifts)
            {
                _arrivals.Clear();
                if (lifts.DequeueArrivals(ctx, l.Id, _arrivals) == 0) continue;
                foreach (var rc in _arrivals)
                {
                    var a = Find(g, rc.AgentId);
                    if (a == null) continue;
                    a.Phase = GuestPhase.AtTop;
                    a.TicksInPhase = 0f;
                    a.Pos = l.Top;
                    a.TargetNodeId = l.TopNodeId;
                    float waitMin = a.QueueWaitMin;
                    var type = l.Type ?? ctx.Data.LiftType(l.TypeId);
                    var arch = ctx.Data.Guests.Archetype(a.ArchetypeId);
                    // ride comfort experience
                    var w = ctx.World.Weather.Current;
                    float cold = MathUtil.Clamp01((t.F("guests.comfortColdStartC") - w.TempC) / t.F("guests.comfortColdRangeC")) + MathUtil.Clamp01(w.WindKmh / t.F("guests.comfortWindRefKmh")) * 0.5f;
                    float comfort = type.ComfortScore - type.WeatherExposure * cold * (arch != null ? arch.WeatherSensitivity : 1f);
                    a.ComfortExperienced = comfort;
                    Experience(a, arch, t.F("guests.satComfortWeight") * (arch != null ? arch.ComfortWeight : 0.5f) * (comfort - 0.5f) - t.F("guests.satQueueWeight") * (arch != null ? arch.QueueWeight : 1f) * MathUtil.Clamp01(waitMin / t.F("guests.queueRefMin")));
                    g.TodayQueueMinSum += waitMin * a.Guests; g.TodayQueueSamples += a.Guests;
                    a.QueueWaitMin = 0f;
                }
            }
            int onMountain = 0;
            for (int i = agents.Count - 1; i >= 0; i--)
            {
                var a = agents[i];
                a.TicksInPhase += 1f;
                switch (a.Phase)
                {
                    case GuestPhase.AtBase: AtBase(ctx, a, lifts); break;
                    case GuestPhase.WalkingToLift:
                        {
                            var l = ctx.World.Lifts.Get(a.LiftId);
                            if (l == null) { a.Phase = GuestPhase.AtBase; break; }
                            float walk = t.F("guests.walkSpeedMs") * dt;
                            Vec2 to = l.Bottom - a.Pos;
                            if (to.Length <= walk) { a.Pos = l.Bottom; a.Phase = GuestPhase.InQueue; a.TicksInPhase = 0f; lifts.Enqueue(ctx, l.Id, a.Id, a.Guests); }
                            else a.Pos += to.Normalized * walk;
                            break;
                        }
                    case GuestPhase.InQueue:
                        {
                            a.QueueWaitMin += dt / 60f;
                            var l = ctx.World.Lifts.Get(a.LiftId);
                            var arch = ctx.Data.Guests.Archetype(a.ArchetypeId);
                            float patience = arch != null ? arch.QueuePatienceMin : 20f;
                            if (l == null || (!l.IsRunning && a.QueueWaitMin > patience * 0.5f) || a.QueueWaitMin > patience)
                            {
                                if (l != null) lifts.LeaveQueue(ctx, l.Id, a.Id);
                                Experience(a, arch, -t.F("guests.satQueueWeight") * (arch != null ? arch.QueueWeight : 1f) * 0.6f);
                                a.QueueWaitMin = 0f;
                                a.Phase = GuestPhase.AtBase;
                                a.Pos = ctx.Sim.Scenario.BaseArea.Pos;
                                a.TicksInPhase = 0f;
                                if (a.Fatigue > 0.3f || ctx.Rng.Chance(t.F("guests.leaveAfterGiveUpProb"))) Leave(ctx, a, "gave up on the queue");
                            }
                            break;
                        }
                    case GuestPhase.Riding: break;
                    case GuestPhase.AtTop: AtTop(ctx, a); break;
                    case GuestPhase.Skiing: Ski(ctx, a, dt); break;
                    case GuestPhase.Eating:
                        if (a.TicksInPhase >= t.F("guests.lunchMinutes") * SimTime.TicksPerMinute) { a.Phase = GuestPhase.AtBase; a.TicksInPhase = 0f; }
                        break;
                    case GuestPhase.Leaving:
                        if (a.TicksInPhase >= t.F("guests.leaveMinutes") * SimTime.TicksPerMinute) { a.Phase = GuestPhase.Gone; }
                        break;
                    case GuestPhase.Gone:
                        agents.RemoveAt(i);
                        _lastCell.Remove(a.Id);
                        continue;
                }
                if (a.Phase != GuestPhase.Leaving && a.Phase != GuestPhase.Gone) onMountain += a.Guests;
            }
            g.GuestsOnMountain = onMountain;
        }

        private static GuestAgent Find(GuestPopulationState g, int id)
        {
            var agents = g.Agents;
            for (int i = 0; i < agents.Count; i++) if (agents[i].Id == id) return agents[i];
            return null;
        }

        private bool RampBlocked(SimContext ctx, LiftState l)
        {
            if (ctx.TryGetSystem<RoadSystem>(out var roads)) return roads.RampBlocked(ctx, l.Id);
            return false;
        }

        private void PlanDay(SimContext ctx)
        {
            var g = ctx.World.Guests;
            var t = ctx.Tuning;
            int demand = ComputeDailyDemand(ctx, ctx.Time.Day);
            g.TodayDemand = demand;
            int per = System.Math.Max(1, t.I("simulation.guestsPerAgent"));
            var archetypes = ctx.Data.Guests.Archetypes;
            if (archetypes.Count == 0) return;
            int dow = ctx.Time.DayOfWeek;
            bool weekend = dow >= 5;
            bool holiday = ctx.Data.Guests.HolidaySeasonDays.Contains(ctx.Time.SeasonDay);
            float totalW = 0f;
            var weights = new float[archetypes.Count];
            for (int i = 0; i < archetypes.Count; i++) { var a = archetypes[i]; weights[i] = a.Share * (holiday ? a.HolidayMultiplier : (weekend ? a.WeekendMultiplier : 1f)); totalW += weights[i]; }
            int remaining = demand;
            long dayStartTick = ctx.Time.Tick - (long)ctx.Time.HourOfDay * SimTime.TicksPerHour;
            _pending.Clear();
            _pendingDay = ctx.Time.Day;
            while (remaining > 0)
            {
                int n = System.Math.Min(per, remaining);
                remaining -= n;
                float r = ctx.Rng.NextFloat() * totalW;
                int idx = 0;
                for (int i = 0; i < weights.Length; i++) { r -= weights[i]; if (r <= 0f) { idx = i; break; } idx = i; }
                var arch = archetypes[idx];
                float arriveHour = MathUtil.Clamp(ctx.Rng.NextGaussian(arch.ArrivalHourMean, arch.ArrivalHourSpread), t.I("simulation.resortOpenHour"), t.I("simulation.resortCloseHour") - 1.5f);
                var agent = new GuestAgent
                {
                    Id = g.NextId++,
                    ArchetypeId = arch.Id,
                    Guests = n,
                    Phase = GuestPhase.Arriving,
                    Lateral = MathUtil.Clamp(ctx.Rng.NextGaussian(0f, arch.LateralSpread), -0.95f, 0.95f),
                    SpeedMs = arch.SkiSpeedMs * ctx.Rng.Range(0.85f, 1.15f),
                    Satisfaction = t.F("guests.satStart"),
                    ArriveTick = dayStartTick + (long)(arriveHour * SimTime.TicksPerHour),
                    HasRental = ctx.Rng.Chance(arch.RentalProbability),
                    HadLesson = ctx.Rng.Chance(arch.LessonProbability),
                    Parked = ctx.Rng.Chance(arch.ParkingProbability),
                };
                agent.DepartTick = agent.ArriveTick + (long)(arch.StayHours * ctx.Rng.Range(0.8f, 1.2f) * SimTime.TicksPerHour);
                _pending.Add(agent);
            }
            ctx.Sim.Log("Resort open. Expected guests today: " + demand + ".");
        }

        private void Arrive(SimContext ctx, GuestAgent a)
        {
            var g = ctx.World.Guests;
            var e = ctx.World.Economy;
            var data = ctx.Data.Economy;
            var t = ctx.Tuning;
            var arch = ctx.Data.Guests.Archetype(a.ArchetypeId);
            ctx.TryGetSystem<EconomySystem>(out var eco);
            float access = 1f, parking = 1f;
            if (ctx.TryGetSystem<RoadSystem>(out var roads)) { access = roads.AccessFactor(ctx); parking = roads.ParkingFactor(ctx); }
            if (ctx.Rng.NextFloat() > access) { g.TodayTurnedAway += a.Guests; return; }
            a.Phase = GuestPhase.AtBase;
            a.Pos = ctx.Sim.Scenario.BaseArea.Pos;
            a.TicksInPhase = 0f;
            g.Agents.Add(a);
            g.TodayArrived += a.Guests;
            bool halfDay = ctx.Time.HourOfDayF >= t.F("guests.halfDayFromHour");
            float price = halfDay ? e.HalfDayPrice : e.TicketPrice;
            eco?.Post(ctx, LedgerCategory.Tickets, price * a.Guests, (halfDay ? "Half-day" : "Day") + " tickets x" + a.Guests + " (" + a.ArchetypeId + ")");
            if (a.HasRental) eco?.Post(ctx, LedgerCategory.Rentals, data.RentalPrice * a.Guests, "Rentals x" + a.Guests);
            if (a.HadLesson) eco?.Post(ctx, LedgerCategory.SkiSchool, data.LessonPrice * a.Guests, "Lessons x" + a.Guests);
            if (a.Parked) eco?.Post(ctx, LedgerCategory.Parking, data.ParkingPrice * MathF.Ceiling(a.Guests / t.F("guests.guestsPerCar")), "Parking");
            a.SpentToday += price * a.Guests;
            float priceBase = MathF.Max(1f, ctx.Data.Economy.Act(e.Act).TicketPriceBase);
            float priceTerm = -(arch != null ? arch.PriceSensitivity : 1f) * t.F("guests.satPriceWeight") * (price / priceBase - 1f);
            float parkTerm = (arch != null ? arch.ParkingWeight : 0.5f) * t.F("guests.satParkingWeight") * (parking - 1f);
            Experience(a, arch, priceTerm + parkTerm);
            ctx.Events.Publish(new GuestArrivedEvent { AgentId = a.Id, Guests = a.Guests });
        }

        private void AtBase(SimContext ctx, GuestAgent a, LiftSystem lifts)
        {
            var t = ctx.Tuning;
            var arch = ctx.Data.Guests.Archetype(a.ArchetypeId);
            if (ctx.Time.Tick >= a.DepartTick || a.Fatigue >= 1f) { Leave(ctx, a, "day over"); return; }
            if (!a.HadLesson && false) { }
            // lunch
            float hour = ctx.Time.HourOfDayF;
            if (!a.LastPisteId.StartsWith("lunch") && hour >= t.F("guests.lunchFromHour") && hour <= t.F("guests.lunchToHour") && ctx.Rng.Chance(t.F("guests.lunchProbPerVisit")))
            {
                a.LastPisteId = "lunch";
                a.Phase = GuestPhase.Eating; a.TicksInPhase = 0f;
                if (ctx.TryGetSystem<EconomySystem>(out var eco) && arch != null)
                {
                    float spend = arch.FoodSpendPerDay * a.Guests;
                    eco.Post(ctx, LedgerCategory.FoodBev, spend, "F&B x" + a.Guests);
                    eco.Post(ctx, LedgerCategory.FoodBev, -spend * (1f - ctx.Data.Economy.FoodMarginPct / 100f), "F&B cost of goods");
                    Experience(a, arch, arch.FoodWeight * t.F("guests.satFoodWeight"));
                }
                return;
            }
            // choose a lift from the base
            _liftTmp.Clear();
            lifts.LiftsFromNode(ctx, "base", _liftTmp);
            LiftState best = null; float bestScore = float.MinValue;
            foreach (var l in _liftTmp)
            {
                if (!l.IsRunning && l.Status != LiftStatus.WindHold) continue;
                float terrain = TerrainMatch(ctx, l.TopNodeId, arch);
                if (terrain < 0f) continue;
                float wait = lifts.ExpectedWaitMin(ctx, l.Id);
                float score = terrain * 2f - wait / t.F("guests.queueRefMin") + ctx.Rng.NextFloat() * 0.3f;
                if (!l.IsRunning) score -= 3f;
                if (score > bestScore) { bestScore = score; best = l; }
            }
            if (best == null)
            {
                // nothing to ride: wait a little then leave unhappy
                if (a.TicksInPhase > t.F("guests.noLiftPatienceMin") * SimTime.TicksPerMinute)
                {
                    Experience(a, arch, -0.4f);
                    Leave(ctx, a, "no lift to ride");
                }
                return;
            }
            a.LiftId = best.Id;
            a.Phase = GuestPhase.WalkingToLift;
            a.TicksInPhase = 0f;
        }

        /// <summary>-1 when no open run from the node fits the archetype at all; else 0..1 preference match.</summary>
        private float TerrainMatch(SimContext ctx, string topNodeId, GuestArchetypeDef arch)
        {
            _pisteTmp.Clear();
            ctx.World.Pistes.PistesFrom(topNodeId, _pisteTmp);
            float best = -1f;
            foreach (var p in _pisteTmp)
            {
                if (!p.Open) continue;
                if (arch != null && (p.Difficulty < arch.MinDifficulty || p.Difficulty > arch.MaxDifficulty)) { best = MathF.Max(best, 0f); continue; }
                float m = arch != null ? 1f - MathF.Abs((int)p.Difficulty - (int)arch.PreferredDifficulty) * 0.3f : 0.7f;
                best = MathF.Max(best, m);
            }
            return best;
        }

        private void AtTop(SimContext ctx, GuestAgent a)
        {
            var arch = ctx.Data.Guests.Archetype(a.ArchetypeId);
            _pisteTmp.Clear();
            ctx.World.Pistes.PistesFrom(a.TargetNodeId, _pisteTmp);
            PisteState best = null; float bestScore = float.MinValue;
            foreach (var p in _pisteTmp)
            {
                if (!p.Open) continue;
                bool inWindow = arch == null || (p.Difficulty >= arch.MinDifficulty && p.Difficulty <= arch.MaxDifficulty);
                float score = (inWindow ? 1f : -2f) + (arch != null ? -MathF.Abs((int)p.Difficulty - (int)arch.PreferredDifficulty) * 0.4f : 0f) + p.Pqi / 100f * (arch != null ? arch.PqiWeight : 1f) + ctx.Rng.NextFloat() * 0.4f;
                if (p.Id == a.LastPisteId) score -= 0.3f; // variety
                if (score > bestScore) { bestScore = score; best = p; }
            }
            if (best == null)
            {
                // download: back to base with a penalty
                Experience(a, arch, -0.3f);
                a.Phase = GuestPhase.AtBase; a.Pos = ctx.Sim.Scenario.BaseArea.Pos; a.TicksInPhase = 0f;
                return;
            }
            a.PisteId = best.Id;
            a.LastPisteId = best.Id;
            a.SegmentIndex = 0;
            a.SegmentT = 0f;
            a.Pos = best.Points[0];
            a.Phase = GuestPhase.Skiing;
            a.TicksInPhase = 0f;
            _lastCell[a.Id] = -1;
        }

        private void Ski(SimContext ctx, GuestAgent a, float dt)
        {
            var net = ctx.World.Pistes;
            var piste = net.Piste(a.PisteId);
            if (piste == null || piste.Points.Count < 2) { a.Phase = GuestPhase.AtBase; return; }
            var t = ctx.Tuning;
            var grid = ctx.World.Snow;
            int segIdx = a.SegmentIndex;
            if (segIdx >= piste.Points.Count - 1) { FinishRun(ctx, a, piste); return; }
            Vec2 p0 = piste.Points[segIdx], p1 = piste.Points[segIdx + 1];
            float segLen = MathF.Max(0.1f, Vec2.Distance(p0, p1));
            var seg = segIdx < piste.SegmentIds.Count ? net.Segment(piste.SegmentIds[segIdx]) : null;
            // speed: archetype speed modulated by surface (moguls/ice slow), grade
            float pqi = seg != null ? seg.Pqi : 50f;
            float speed = a.SpeedMs * MathUtil.Lerp(t.F("guests.speedFactorAtPqi0"), 1f, pqi / 100f);
            float grade = seg != null ? seg.GradeDeg : 15f;
            speed *= MathUtil.Clamp(grade / t.F("guests.speedRefGradeDeg"), 0.4f, 1.3f);
            a.SegmentT += speed * dt / segLen;
            Vec2 dir = (p1 - p0) / segLen;
            Vec2 left = dir.Perp;
            float half = piste.WidthM * 0.5f;
            Vec2 pos = Vec2.Lerp(p0, p1, MathUtil.Clamp01(a.SegmentT)) + left * (a.Lateral * half * 0.9f);
            a.Pos = pos;
            // write traffic when entering a new cell
            int cell = grid.CellIdAt(pos);
            if (cell >= 0 && (!_lastCell.TryGetValue(a.Id, out int last) || last != cell))
            {
                _lastCell[a.Id] = cell;
                float n = a.Guests;
                SnowOps.SkierPass(grid, cell, n * 0.5f, dir, t);
                int side1 = grid.CellIdAt(pos + left * grid.CellSize), side2 = grid.CellIdAt(pos - left * grid.CellSize);
                if (side1 >= 0) SnowOps.SkierPass(grid, side1, n * 0.25f, dir, t); else SnowOps.SkierPass(grid, cell, n * 0.25f, dir, t);
                if (side2 >= 0) SnowOps.SkierPass(grid, side2, n * 0.25f, dir, t); else SnowOps.SkierPass(grid, cell, n * 0.25f, dir, t);
                if (seg != null) seg.TrafficToday += n;
                piste.TrafficToday += n * grid.CellSize / MathF.Max(1f, piste.LengthM);
                piste.TrafficTotal += n * grid.CellSize / MathF.Max(1f, piste.LengthM);
            }
            if (a.SegmentT >= 1f)
            {
                a.SegmentT = 0f;
                a.SegmentIndex++;
                if (seg != null) { a.PqiExperienced += seg.Pqi; a.PqiSamples++; }
                if (a.SegmentIndex >= piste.Points.Count - 1) FinishRun(ctx, a, piste);
            }
        }

        private void FinishRun(SimContext ctx, GuestAgent a, PisteState piste)
        {
            var t = ctx.Tuning;
            var g = ctx.World.Guests;
            var arch = ctx.Data.Guests.Archetype(a.ArchetypeId);
            a.Laps++;
            g.TodayLaps += a.Guests;
            float pqi = a.PqiSamples > 0 ? a.PqiExperienced / a.PqiSamples : piste.Pqi;
            a.PqiExperienced = 0f; a.PqiSamples = 0;
            float pqiTerm = (arch != null ? arch.PqiWeight : 1f) * t.F("guests.satPqiWeight") * (pqi / 100f - t.F("guests.satPqiNeutral"));
            bool inWindow = arch == null || (piste.Difficulty >= arch.MinDifficulty && piste.Difficulty <= arch.MaxDifficulty);
            float terrainTerm = (arch != null ? arch.TerrainWeight : 1f) * t.F("guests.satTerrainWeight") * (inWindow ? (1f - MathF.Abs((int)piste.Difficulty - (int)(arch != null ? arch.PreferredDifficulty : PisteDifficulty.Blue)) * 0.35f) - 0.5f : -0.8f);
            Experience(a, arch, pqiTerm + terrainTerm);
            // legs tire by vertical skied, not by lap count: a bunny-hill lap costs a fraction of a summit run
            float lapVertical = MathF.Max(20f, piste.VerticalM) / t.F("guests.referenceLapVerticalM");
            a.Fatigue += t.F("guests.fatiguePerLap") * lapVertical / MathF.Max(0.3f, arch != null ? arch.LapsPerDayTarget / 8f : 1f);
            // bottom node: base or a lift bottom
            a.Pos = piste.Points[piste.Points.Count - 1];
            var node = ctx.World.Pistes.Node(piste.BottomNodeId);
            a.Phase = GuestPhase.AtBase;
            a.TicksInPhase = 0f;
            if (node != null && node.Kind == NodeKind.LiftBottom && node.LiftId >= 0)
            {
                // straight back onto the same lift unless tired or done
                var l = ctx.World.Lifts.Get(node.LiftId);
                if (l != null && l.IsRunning && ctx.Time.Tick < a.DepartTick && a.Fatigue < 1f)
                {
                    a.LiftId = l.Id;
                    a.Phase = GuestPhase.WalkingToLift;
                }
            }
        }

        private float _satNeutral = 0.6f, _satBlend = 0.3f;

        /// <summary>
        /// One experience (a ride, a run, lunch, the ticket window) scores neutral + delta; satisfaction is the
        /// moving average of those scores, so a steady 7-minute queue settles at a steady level instead of
        /// grinding satisfaction to zero over a day of laps.
        /// </summary>
        private void Experience(GuestAgent a, GuestArchetypeDef arch, float delta)
        {
            float score = MathUtil.Clamp01(_satNeutral + delta);
            a.Satisfaction = MathUtil.Clamp01(a.Satisfaction + (score - a.Satisfaction) * _satBlend);
        }

        private void Leave(SimContext ctx, GuestAgent a, string why)
        {
            if (a.Phase == GuestPhase.Leaving || a.Phase == GuestPhase.Gone) return;
            var g = ctx.World.Guests;
            if (a.Phase == GuestPhase.InQueue) ctx.System<LiftSystem>().LeaveQueue(ctx, a.LiftId, a.Id);
            a.Phase = GuestPhase.Leaving;
            a.TicksInPhase = 0f;
            g.TodayDeparted += a.Guests;
            g.TodaySatisfactionSum += a.Satisfaction * a.Guests;
            g.TodaySatisfactionCount += a.Guests;
            ctx.Events.Publish(new GuestLeftEvent { AgentId = a.Id, Satisfaction = a.Satisfaction });
        }

        private void CloseDay(SimContext ctx)
        {
            var g = ctx.World.Guests;
            var t = ctx.Tuning;
            float meanSat = g.TodaySatisfactionCount > 0 ? (float)(g.TodaySatisfactionSum / g.TodaySatisfactionCount) : 0.5f;
            if (g.TodayArrived > 0)
            {
                g.SatisfactionHistory.Add(meanSat);
                if (g.SatisfactionHistory.Count > 60) g.SatisfactionHistory.RemoveAt(0);
                float lag = MathF.Max(1f, ctx.Data.Economy.ReputationLagDays);
                float before = g.Reputation;
                g.Reputation = MathUtil.Clamp(g.Reputation + (meanSat * 100f - g.Reputation) / lag, 0f, 100f);
                ctx.Events.Publish(new ReputationChangedEvent { Reputation = g.Reputation, Delta = g.Reputation - before });
            }
            double tickets = 0, spend = 0;
            foreach (var en in ctx.World.Economy.Ledger)
            {
                if (en.Day != ctx.Time.Day || en.Amount <= 0) continue;
                if (en.Category == LedgerCategory.Tickets) tickets += en.Amount;
                if (en.Category == LedgerCategory.Tickets || en.Category == LedgerCategory.Rentals || en.Category == LedgerCategory.SkiSchool || en.Category == LedgerCategory.FoodBev || en.Category == LedgerCategory.Parking) spend += en.Amount;
            }
            var rep = new DailyGuestReport
            {
                Day = ctx.Time.Day, Demand = g.TodayDemand, Arrived = g.TodayArrived, Turned = g.TodayTurnedAway,
                AvgSatisfaction = meanSat, AvgQueueMin = g.TodayQueueSamples > 0 ? g.TodayQueueMinSum / g.TodayQueueSamples : 0f,
                AvgPqi = ctx.World.Pistes.ResortPqi, ReputationAfter = g.Reputation, TicketRevenue = tickets, Spend = spend, Laps = g.TodayLaps,
            };
            g.History.Add(rep);
            if (g.History.Count > 200) g.History.RemoveAt(0);
            ctx.Events.Publish(new GuestDayClosedEvent { Report = rep });
            ctx.Sim.Log("Lifts closed. " + g.TodayArrived + " guests, satisfaction " + MathF.Round(meanSat * 100f) + "%, avg queue " + rep.AvgQueueMin.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " min, reputation " + MathF.Round(g.Reputation) + ".");
        }
    }
}
