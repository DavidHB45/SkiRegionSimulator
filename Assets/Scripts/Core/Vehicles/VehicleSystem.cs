using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Tasks;

namespace AlpineSim.Core.Vehicles
{
    /// <summary>
    /// Every machine on the mountain: driving physics per chassis type (DrivingModels), snow contact
    /// through mounted attachments and running gear (SnowContact), fuel, warm-up / cold start,
    /// subsystem wear from load, failures, and the AI operator (VehicleAi). Ticks after Guests,
    /// before Tasks.
    ///
    /// Out of fuel: the machine stops where it is, Stranded = true, VehicleStrandedEvent fires and the
    /// assigned task is blocked until refuelled (the fleet system dispatches the service truck).
    /// </summary>
    public sealed class VehicleSystem : ISimSystem
    {
        public string Name => "Vehicles";

        private readonly SnowContact _contact = new SnowContact();
        private readonly VehicleAi _ai = new VehicleAi();
        private readonly RouteGraph _routes = new RouteGraph();
        private int _routeVersion = -1;
        private readonly List<Vec2> _routeTmp = new List<Vec2>();

        /// <summary>Called by the fleet system (M6) to supply operator competence/limits; defaults otherwise.</summary>
        public Func<SimContext, VehicleState, OperatorProfile> OperatorLookup;
        /// <summary>Hook for the fleet system: refuel at the depot when an AI machine arrives.</summary>
        public Action<SimContext, VehicleState> DepotArrival;

        public struct OperatorProfile { public float Competence; public float MaxSlopeDeg; public bool Licensed; public int OperatorId; }

        public void Initialize(SimContext ctx, bool newGame)
        {
            var world = ctx.World;
            if (world.Vehicles == null) world.Vehicles = new VehicleFleetState();
            if (newGame)
            {
                var scen = ctx.Sim.Scenario;
                foreach (var s in scen.StartingFleet)
                {
                    if (s.ExistsFromAct > scen.StartingAct) continue;
                    if (ctx.Data.Vehicle(s.DefId) == null) { ctx.Sim.Log("Scenario fleet references unknown machine " + s.DefId, LogLevel.Warning); continue; }
                    var v = Spawn(ctx, s.DefId, new Vec2(s.X, s.Y), s.HeadingDeg * MathUtil.Deg2Rad, s.Name, s.Hours, s.ConditionPct, s.FuelFrac);
                    v.Sheltered = s.Sheltered;
                }
            }
            foreach (var v in world.Vehicles.List) v.Def = ctx.Data.Vehicle(v.DefId);
            RebuildRoutes(ctx);
        }

        private void RebuildRoutes(SimContext ctx)
        {
            var net = ctx.World.Pistes;
            int version = net.Pistes.Count * 1000 + net.Zones.Count;
            if (version == _routeVersion) return;
            _routes.Build(net, ctx.Sim.Scenario.BaseArea.Pos, 25f);
            _routeVersion = version;
        }

        // ------------------------------------------------------------------ registry
        public IReadOnlyList<VehicleState> All(SimContext ctx) => ctx.World.Vehicles.List;
        public VehicleState Get(SimContext ctx, int id) => ctx.World.Vehicles.Get(id);
        public VehicleState PlayerVehicle(SimContext ctx) => ctx.World.Vehicles.Get(ctx.World.Vehicles.PlayerVehicleId);

        public VehicleState Spawn(SimContext ctx, string defId, Vec2 pos, float headingRad, string name = null, float hours = 0f, float conditionPct = 100f, float fuelFrac = 0.8f)
        {
            var def = ctx.Data.RequireVehicle(defId);
            var fleet = ctx.World.Vehicles;
            var v = new VehicleState
            {
                Id = fleet.NextId++,
                DefId = defId,
                Name = string.IsNullOrEmpty(name) ? def.DisplayName : name,
                Pos = pos,
                Heading = headingRad,
                Def = def,
                HoursMeter = hours,
                Fuel = def.EnergyCapacity * MathUtil.Clamp01(fuelFrac),
                AcquiredDay = ctx.Time.Day,
                PurchasePaid = def.PurchasePrice,
                EngineTempC = ctx.World.Weather.Current.TempC,
                HydraulicTempC = ctx.World.Weather.Current.TempC,
            };
            float wear = MathUtil.Clamp01(1f - conditionPct / 100f);
            v.Condition.WearEngine = wear * 0.9f;
            v.Condition.WearHydraulics = wear;
            v.Condition.WearDrivetrain = wear * 0.8f;
            v.Condition.WearTracks = wear * 1.1f > 1f ? 1f : wear * 1.1f;
            v.Condition.WearAttachment = wear * 0.7f;
            v.Condition.HoursSinceService = hours % MathF.Max(1f, def.ServiceIntervalHours);
            foreach (var slot in def.AttachmentSlots)
            {
                if (string.IsNullOrEmpty(slot.DefaultAttachmentId)) continue;
                if (Mount(ctx, v, slot.DefaultAttachmentId, slot.Position, out _)) { }
            }
            fleet.List.Add(v);
            UpdateLocationLabel(ctx, v);
            ctx.Events.Publish(new VehicleSpawnedEvent { VehicleId = v.Id });
            return v;
        }

        public bool Remove(SimContext ctx, int id)
        {
            var fleet = ctx.World.Vehicles;
            var v = fleet.Get(id);
            if (v == null) return false;
            if (fleet.PlayerVehicleId == id) Exit(ctx);
            fleet.List.Remove(v);
            ctx.Events.Publish(new VehicleRemovedEvent { VehicleId = id });
            return true;
        }

        // ------------------------------------------------------------------ player
        public void SetInput(SimContext ctx, int id, VehicleInput input)
        {
            var v = Get(ctx, id);
            if (v == null || !v.PlayerControlled) return;
            v.Input.Throttle = input.Throttle; v.Input.Steer = input.Steer; v.Input.Brake = input.Brake;
            v.Input.BladeLift = input.BladeLift; v.Input.BladeAngle = input.BladeAngle; v.Input.BladeTilt = input.BladeTilt;
            v.Input.Tiller = input.Tiller; v.Input.Implement = input.Implement; v.Input.Lights = input.Lights; v.Input.Work = input.Work;
            v.Input.WinchTension = input.WinchTension;
        }

        public bool TryEnter(SimContext ctx, int id, out string reason)
        {
            var v = Get(ctx, id);
            reason = "";
            if (v == null) { reason = "no such machine"; return false; }
            var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
            if (def.IsStationary) { reason = def.DisplayName + " is a fixed installation"; return false; }
            if (def.ChassisType == ChassisType.Towed) { reason = def.DisplayName + " must be towed"; return false; }
            if (v.Condition.IsDown) { reason = v.Name + " is down for repair"; return false; }
            Exit(ctx);
            v.PlayerControlled = true;
            v.Ai.Mode = AiMode.Idle;
            v.Input.Clear();
            ctx.World.Vehicles.PlayerVehicleId = id;
            if (!v.EngineOn) StartEngine(ctx, id, out reason);
            ctx.Events.Publish(new PlayerVehicleChangedEvent { VehicleId = id });
            return true;
        }

        public void Exit(SimContext ctx)
        {
            var fleet = ctx.World.Vehicles;
            var v = fleet.Get(fleet.PlayerVehicleId);
            if (v != null)
            {
                v.PlayerControlled = false;
                v.Input.Clear();
                v.Input.Brake = 1f;
            }
            fleet.PlayerVehicleId = -1;
            ctx.Events.Publish(new PlayerVehicleChangedEvent { VehicleId = -1 });
        }

        public bool StartEngine(SimContext ctx, int id, out string reason)
        {
            var v = Get(ctx, id);
            reason = "";
            if (v == null) { reason = "no such machine"; return false; }
            if (v.EngineOn) return true;
            var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
            var t = ctx.Tuning;
            if (v.Condition.IsDown) { reason = "machine is down: " + (v.Condition.ActiveFailures.Count > 0 ? v.Condition.ActiveFailures[0].Description : "failure"); return false; }
            if (def.FuelType != FuelType.None && v.Fuel <= 0.01f) { reason = "out of " + (def.IsElectric ? "charge" : "fuel"); v.Stranded = true; v.StrandedReason = reason; return false; }
            float ambient = ctx.World.Weather.Current.TempC;
            float start = v.Sheltered ? ambient + t.F("vehicles.shelteredTempBonusC") : ambient;
            if (start < t.F("vehicles.coldStartBelowC") && v.EngineTempC < t.F("vehicles.coldStartBelowC"))
            {
                float failP = t.Curve("vehicles.coldStartFailProbByTier", def.Tier) * (v.Sheltered ? 0.3f : 1f) * (1f + v.Condition.WearEngine);
                if (ctx.Rng.Chance(MathUtil.Clamp01(failP)))
                {
                    reason = "cold start failed at " + MathF.Round(start) + " C; try again";
                    v.EngineTempC += 3f; // each attempt warms things a little
                    return false;
                }
                v.WarmupFrac = 0f;
            }
            else if (v.WarmupFrac <= 0f) v.WarmupFrac = MathUtil.Clamp01((v.EngineTempC + 10f) / 40f);
            v.EngineOn = true;
            v.Rpm = t.F("vehicles.idleRpm");
            return true;
        }

        public void StopEngine(SimContext ctx, int id)
        {
            var v = Get(ctx, id);
            if (v == null) return;
            v.EngineOn = false;
            v.Rpm = 0f;
            v.Input.Clear();
        }

        // ------------------------------------------------------------------ attachments / fuel / roles
        public bool Mount(SimContext ctx, int id, string attachmentId, SlotPosition slot, out string reason)
        {
            var v = Get(ctx, id);
            if (v == null) { reason = "no such machine"; return false; }
            return Mount(ctx, v, attachmentId, slot, out reason);
        }

        public bool Mount(SimContext ctx, VehicleState v, string attachmentId, SlotPosition slot, out string reason)
        {
            reason = "";
            var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
            var att = ctx.Data.Attachment(attachmentId);
            if (att == null) { reason = "unknown attachment " + attachmentId; return false; }
            var s = def.Slot(slot);
            if (s == null) { reason = def.DisplayName + " has no " + slot + " slot"; return false; }
            if (!att.FitsSlot(slot)) { reason = att.DisplayName + " does not fit a " + slot + " slot"; return false; }
            if (att.MassKg > s.MaxMassKg) { reason = att.DisplayName + " is too heavy for the " + slot + " slot (" + att.MassKg + " > " + s.MaxMassKg + " kg)"; return false; }
            if (att.HydraulicFlowLpm > 0f && (s.RequiresHydraulics || att.HydraulicFlowLpm > def.HydraulicFlowLpm)) { if (att.HydraulicFlowLpm > def.HydraulicFlowLpm) { reason = att.DisplayName + " needs " + att.HydraulicFlowLpm + " L/min hydraulics; machine has " + def.HydraulicFlowLpm; return false; } }
            if (att.RequiresPto && !def.Pto) { reason = att.DisplayName + " needs a PTO"; return false; }
            if (att.MinHostPowerKw > def.EnginePowerKw) { reason = att.DisplayName + " needs " + att.MinHostPowerKw + " kW; machine has " + def.EnginePowerKw; return false; }
            if (att.ForCategories.Count > 0 && !att.ForCategories.Contains(def.Category)) { reason = att.DisplayName + " is not built for a " + def.Category; return false; }
            var existing = v.Slot(slot);
            if (existing != null) v.Mounted.Remove(existing);
            v.Mounted.Add(new MountedAttachment { DefId = attachmentId, Slot = slot, Lift = 1f });
            ctx.Events.Publish(new AttachmentChangedEvent { VehicleId = v.Id, Slot = slot, AttachmentId = attachmentId, Mounted = true });
            return true;
        }

        public bool Dismount(SimContext ctx, int id, SlotPosition slot, out string reason)
        {
            var v = Get(ctx, id);
            reason = "";
            if (v == null) { reason = "no such machine"; return false; }
            var m = v.Slot(slot);
            if (m == null) { reason = "nothing mounted on " + slot; return false; }
            if (m.LoadKg > 0f)
            {
                // drop the carried snow where it is
                var grid = ctx.World.Snow;
                int cell = grid.EnsureCellAt(v.Pos + Vec2.FromAngle(v.Heading) * 3f);
                if (cell >= 0) Snow.SnowOps.DepositPacked(grid, cell, m.LoadKg / grid.CellAreaM2, m.LoadDensity);
                m.LoadKg = 0f;
            }
            v.Mounted.Remove(m);
            ctx.Events.Publish(new AttachmentChangedEvent { VehicleId = v.Id, Slot = slot, AttachmentId = m.DefId, Mounted = false });
            return true;
        }

        public float Refuel(SimContext ctx, int id, float amount)
        {
            var v = Get(ctx, id);
            if (v == null || amount <= 0f) return 0f;
            var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
            float room = def.EnergyCapacity - v.Fuel;
            float accepted = MathF.Max(0f, MathF.Min(room, amount));
            v.Fuel += accepted;
            if (v.Stranded && v.Fuel > def.EnergyCapacity * 0.02f)
            {
                v.Stranded = false;
                v.StrandedReason = "";
                if (v.Ai.Mode == AiMode.Stranded) v.Ai.Mode = AiMode.Idle;
                UnblockTask(ctx, v);
            }
            ctx.Events.Publish(new VehicleRefueledEvent { VehicleId = id, Liters = accepted });
            return accepted;
        }

        public bool HasRole(SimContext ctx, VehicleState v, VehicleRole role)
        {
            var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
            if (def != null && def.HasRole(role)) return true;
            for (int i = 0; i < v.Mounted.Count; i++)
            {
                var a = ctx.Data.Attachment(v.Mounted[i].DefId);
                if (a != null && a.GrantsRoles.Contains(role)) return true;
            }
            return false;
        }

        public float WorkRate(SimContext ctx, VehicleState v, VehicleRole role)
        {
            var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
            var t = ctx.Tuning;
            float rate = def.SpecOr("workRate", 1f);
            for (int i = 0; i < v.Mounted.Count; i++)
            {
                var a = ctx.Data.Attachment(v.Mounted[i].DefId);
                if (a != null && a.GrantsRoles.Contains(role) && a.Effects.WorkRate > 0f) rate *= a.Effects.WorkRate * (1f - v.Mounted[i].Wear * 0.4f);
            }
            float power = 1f - v.Condition.WearEngine * t.F("vehicles.enginePowerLossAtFullWear");
            float warm = MathUtil.Lerp(0.5f, 1f, v.WarmupFrac);
            return rate * power * warm;
        }

        public float WorkingWidthM(SimContext ctx, VehicleState v, VehicleRole role)
        {
            var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
            float best = 0f;
            for (int i = 0; i < v.Mounted.Count; i++)
            {
                var a = ctx.Data.Attachment(v.Mounted[i].DefId);
                if (a != null && a.GrantsRoles.Contains(role)) best = MathF.Max(best, a.WorkingWidthM);
            }
            if (best <= 0f)
            {
                if (role == VehicleRole.Blow && def.HasSpec("intakeWidthM")) best = def.Spec("intakeWidthM");
                else if (role == VehicleRole.Plow && def.HasSpec("plowWidthM")) best = def.Spec("plowWidthM");
                else best = def.Visual != null ? def.Visual.BodyW : 2.5f;
            }
            return best;
        }

        // ------------------------------------------------------------------ AI assignment
        public void AssignRoute(SimContext ctx, int id, List<Vec2> waypoints, AiMode modeAtEnd = AiMode.Idle)
        {
            var v = Get(ctx, id); if (v == null) return;
            v.PlayerControlled = v.PlayerControlled && false;
            v.Ai.Route = new List<Vec2>(waypoints); v.Ai.RouteIndex = 0; v.Ai.Mode = AiMode.FollowRoute; v.Ai.Phase = "driving";
        }

        public void AssignGroom(SimContext ctx, int id, string pisteId, int taskId = -1)
        {
            var v = Get(ctx, id); if (v == null) return;
            v.Ai = new VehicleAiState { Mode = AiMode.GroomPiste, TargetId = pisteId, TaskId = taskId, Phase = "" };
            v.TaskId = taskId;
        }

        public void AssignZoneWork(SimContext ctx, int id, string zoneId, AiMode mode, int taskId = -1)
        {
            var v = Get(ctx, id); if (v == null) return;
            v.Ai = new VehicleAiState { Mode = mode, TargetId = zoneId, TaskId = taskId, Phase = "" };
            v.TaskId = taskId;
        }

        public void AssignHaul(SimContext ctx, int id, Vec2 loadAt, Vec2 deliverTo, string cargoKind, float quantity, int taskId = -1)
        {
            var v = Get(ctx, id); if (v == null) return;
            v.Ai = new VehicleAiState { Mode = AiMode.HaulLoop, LoadAt = loadAt, DeliverTo = deliverTo, TaskId = taskId, Phase = "", TargetId = cargoKind };
            v.Ai.WorkTimer = 0f;
            v.TaskId = taskId;
            _haulRemaining[id] = quantity;
        }

        public void AssignSiteWork(SimContext ctx, int id, Vec2 site, int taskId)
        {
            var v = Get(ctx, id); if (v == null) return;
            v.Ai = new VehicleAiState { Mode = AiMode.GoToSite, TaskId = taskId, Phase = "to site" };
            v.Ai.Route = FindRoute(ctx, v.Pos, site);
            v.Ai.RouteIndex = 0;
            v.TaskId = taskId;
        }

        public void ReturnToBase(SimContext ctx, int id)
        {
            var v = Get(ctx, id); if (v == null) return;
            v.Ai.Mode = AiMode.ReturnToBase;
            v.Ai.Route = FindRoute(ctx, v.Pos, ctx.Sim.Scenario.Landmark(ctx.Sim.Scenario.GarageLandmarkId).Pos);
            v.Ai.RouteIndex = 0;
            v.Ai.Phase = "returning";
        }

        public void ClearAi(SimContext ctx, int id)
        {
            var v = Get(ctx, id); if (v == null) return;
            v.Ai = new VehicleAiState();
            v.Input.Clear();
            v.TaskId = -1;
        }

        private readonly Dictionary<int, float> _haulRemaining = new Dictionary<int, float>();
        public float HaulRemaining(SimContext ctx, VehicleState v) => _haulRemaining.TryGetValue(v.Id, out float r) ? r : 0f;

        internal void OnHaulDelivered(SimContext ctx, VehicleState v, float kg)
        {
            if (_haulRemaining.TryGetValue(v.Id, out float r)) _haulRemaining[v.Id] = MathF.Max(0f, r - kg);
            if (v.TaskId >= 0 && ctx.TryGetSystem<TaskSystem>(out var ts)) ts.ReportDelivery(ctx, v.TaskId, kg);
        }

        internal void SelfLoad(SimContext ctx, VehicleState v, float dt)
        {
            // a self-loading hauler scoops from the ground under its front
            var grid = ctx.World.Snow;
            float capacity = SnowContact.TruckCapacityKg(ctx, v);
            float want = capacity * dt / 30f;
            var fwd = Vec2.FromAngle(v.Heading);
            for (int i = -2; i <= 2 && want > 0f; i++)
            {
                int id = grid.CellIdAt(v.Pos + fwd * 3f + fwd.Perp * (i * 0.5f));
                if (id < 0) continue;
                float dens = grid.ColumnDensity(id);
                float got = Snow.SnowOps.Remove(grid, id, MathF.Min(want / grid.CellAreaM2, grid.MassKgPerM2(id) * 0.3f)) * grid.CellAreaM2;
                v.CargoDensity = v.CargoKg + got > 0f ? (v.CargoDensity * v.CargoKg + dens * got) / (v.CargoKg + got) : dens;
                v.CargoKg += got; v.CargoKind = "snow"; want -= got;
            }
        }

        internal void OnGroomFinished(SimContext ctx, VehicleState v, PisteState piste)
        {
            if (v.TaskId >= 0 && ctx.TryGetSystem<TaskSystem>(out var ts))
            {
                var task = ts.Get(ctx, v.TaskId);
                if (task != null) ts.ReportWork(ctx, v.TaskId, MathF.Max(0f, task.WorkRequired - task.WorkDone));
            }
            ctx.Sim.Log(v.Name + " finished grooming " + piste.Name + ".");
        }

        internal void OnZoneFinished(SimContext ctx, VehicleState v, SurfaceZone zone)
        {
            if (v.TaskId >= 0 && ctx.TryGetSystem<TaskSystem>(out var ts))
            {
                var task = ts.Get(ctx, v.TaskId);
                if (task != null) ts.ReportWork(ctx, v.TaskId, MathF.Max(0f, task.WorkRequired - task.WorkDone));
            }
            ctx.Sim.Log(v.Name + " cleared " + zone.Id + ".");
        }

        internal void OnAiIdle(SimContext ctx, VehicleState v)
        {
            v.Input.Clear();
            v.Input.Brake = 1f;
            if (v.TaskId >= 0 && ctx.TryGetSystem<TaskSystem>(out var ts))
            {
                var task = ts.Get(ctx, v.TaskId);
                if (task != null && task.Status != TaskStatus.Done) { /* leave assignment; fleet decides */ }
            }
        }

        internal void ArrivedAtDepot(SimContext ctx, VehicleState v)
        {
            if (DepotArrival != null) DepotArrival(ctx, v);
            else
            {
                // M2: the depot is bottomless; M6 charges the tank from depot stock
                var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
                Refuel(ctx, v.Id, def.EnergyCapacity);
                ctx.Sim.Log(v.Name + " refuelled at the depot.");
            }
        }

        // ------------------------------------------------------------------ queries
        public float SurfaceHeight(SimContext ctx, Vec2 pos) => ctx.Terrain.SampleHeight(pos) + ctx.World.Snow.DepthAtMm(pos) * 0.001f;

        public List<Vec2> FindRoute(SimContext ctx, Vec2 from, Vec2 to)
        {
            RebuildRoutes(ctx);
            var t = ctx.Tuning;
            return _routes.Find(from, to, t.F("vehicles.routeSnapRadiusM"), t.F("vehicles.routeStraightMaxM"));
        }

        public VehicleState Nearest(SimContext ctx, Vec2 pos, float radiusM)
        {
            VehicleState best = null; float bd = radiusM * radiusM;
            foreach (var v in ctx.World.Vehicles.List)
            {
                var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
                if (def == null || def.IsStationary || def.ChassisType == ChassisType.Towed) continue;
                float d = Vec2.SqrDistance(v.Pos, pos);
                if (d < bd) { bd = d; best = v; }
            }
            return best;
        }

        // ------------------------------------------------------------------ tick
        public void Tick(SimContext ctx, float dt)
        {
            var list = ctx.World.Vehicles.List;
            var t = ctx.Tuning;
            int divisor = System.Math.Max(1, t.I("simulation.aiVehicleTickDivisor"));
            for (int i = 0; i < list.Count; i++)
            {
                var v = list[i];
                var def = v.Def ?? (v.Def = ctx.Data.Vehicle(v.DefId));
                if (def == null || def.IsStationary) continue;
                bool ai = !v.PlayerControlled && v.Ai.Mode != AiMode.Idle;
                if (ai && divisor > 1 && (ctx.Time.Tick + v.Id) % divisor != 0) continue;
                float step = ai ? dt * divisor : dt;
                StepVehicle(ctx, v, def, step);
            }
            if (ctx.Time.IsDayStart)
            {
                for (int i = 0; i < list.Count; i++) { var v = list[i]; v.TilledM2Today = 0f; v.PlowedM2Today = 0f; v.BlownKgToday = 0f; v.MadeSnowM3Today = 0f; }
            }
            if (ctx.Time.IsMinuteStart) for (int i = 0; i < list.Count; i++) UpdateLocationLabel(ctx, list[i]);
        }

        private void StepVehicle(SimContext ctx, VehicleState v, VehicleDef def, float dt)
        {
            var t = ctx.Tuning;
            float ambient = ctx.World.Weather.Current.TempC;

            // ---- operator input
            OperatorProfile op = OperatorLookup != null ? OperatorLookup(ctx, v) : new OperatorProfile { Competence = 0.8f, MaxSlopeDeg = 30f, Licensed = true, OperatorId = -1 };
            if (!v.PlayerControlled)
            {
                if (v.Ai.Mode != AiMode.Idle) _ai.Step(ctx, this, v, def, op.Competence, op.MaxSlopeDeg, dt);
                else if (v.EngineOn && !v.Stranded) { v.Input.Clear(); v.Input.Brake = 1f; }
            }

            // ---- towed machines follow their tower: handled by the tower's position; skip physics
            if (def.ChassisType == ChassisType.Towed) { v.Speed = 0f; return; }

            // ---- engine thermal / warm-up
            if (v.EngineOn)
            {
                v.EngineTempC = MathUtil.MoveTowards(v.EngineTempC, 85f, 0.15f * dt * (1f + v.LoadFrac));
                float warmMin = t.F("vehicles.warmupMinutes") * (v.Sheltered ? 1f : t.F("vehicles.unshelteredWarmupMultiplier"));
                v.WarmupFrac = MathUtil.Clamp01(v.WarmupFrac + dt / (warmMin * 60f));
                v.HydraulicTempC = MathUtil.MoveTowards(v.HydraulicTempC, 50f, 0.08f * dt);
            }
            else
            {
                v.EngineTempC = MathUtil.MoveTowards(v.EngineTempC, ambient + (v.Sheltered ? t.F("vehicles.shelteredTempBonusC") : 0f), 0.01f * dt);
                v.HydraulicTempC = MathUtil.MoveTowards(v.HydraulicTempC, ambient, 0.01f * dt);
                v.WarmupFrac = MathUtil.Clamp01(v.WarmupFrac - dt / 1800f);
                v.Rpm = 0f;
                v.LoadFrac = 0f;
                v.FuelBurnLph = 0f;
                if (MathF.Abs(v.Speed) > 0.01f) { StepPhysics(ctx, v, def, dt); }
                return;
            }

            // ---- implements (hydraulic response scales with warm-up and hydraulic wear)
            float hyd = MathUtil.Lerp(0.35f, 1f, v.WarmupFrac) * (1f - v.Condition.WearHydraulics * t.F("vehicles.hydraulicResponseLossAtFullWear"));
            for (int i = 0; i < v.Mounted.Count; i++)
            {
                var m = v.Mounted[i];
                if (m.Slot == SlotPosition.Front || m.Slot == SlotPosition.Mid)
                {
                    m.Lift = MathUtil.Clamp01(m.Lift + v.Input.BladeLift * t.F("vehicles.bladeLiftRatePerS") * hyd * dt);
                    float maxA = t.F("vehicles.bladeMaxAngleRad");
                    m.Angle = MathUtil.Clamp(m.Angle + v.Input.BladeAngle * t.F("vehicles.bladeAngleRateRadS") * hyd * dt, -maxA, maxA);
                    m.Tilt = MathUtil.Clamp(m.Tilt + v.Input.BladeTilt * 0.3f * hyd * dt, -0.3f, 0.3f);
                }
                else if (m.Slot == SlotPosition.Rear)
                {
                    float target = v.Input.Tiller || v.Input.Implement ? 0f : 1f;
                    m.Lift = MathUtil.MoveTowards(m.Lift, target, t.F("vehicles.bladeLiftRatePerS") * hyd * dt);
                }
            }

            StepPhysics(ctx, v, def, dt);

            // ---- fuel, hours, wear, failures
            float burn = BurnRate(def, v, t);
            v.FuelBurnLph = burn;
            if (def.FuelType != FuelType.None)
            {
                v.Fuel -= burn * dt / 3600f;
                if (v.Fuel <= 0f)
                {
                    v.Fuel = 0f;
                    v.EngineOn = false;
                    v.Stranded = true;
                    v.StrandedReason = def.IsElectric ? "battery flat" : "out of fuel";
                    v.Input.Clear();
                    v.Ai.Mode = AiMode.Stranded;
                    BlockTask(ctx, v, v.StrandedReason);
                    ctx.Events.Publish(new VehicleStrandedEvent { VehicleId = v.Id, Reason = v.StrandedReason });
                    ctx.Sim.Log(v.Name + " is " + v.StrandedReason + " at " + v.LocationLabel + ". Send the service truck.", LogLevel.Alert);
                }
            }
            float hours = dt / 3600f;
            v.HoursMeter += hours;
            v.Condition.HoursSinceService += hours;
            v.Condition.HoursSinceRebuild += hours;
            v.OdometerKm += MathF.Abs(v.Speed) * dt / 1000f;
            ApplyWear(ctx, v, def, hours, t);
            RollFailures(ctx, v, def, hours, t);
            v.LightsOn = v.Input.Lights;
            v.Rpm = MathUtil.Lerp(t.F("vehicles.idleRpm"), t.F("vehicles.ratedRpm"), MathUtil.Clamp01(v.LoadFrac + MathF.Abs(v.Input.Throttle) * 0.3f));

            // ---- report grooming work to the task board
            if (v.TaskId >= 0 && v.TilledM2Today > 0f) { /* groom tasks complete through OnGroomFinished or player sweep */ }
            if (v.PlayerControlled && ctx.TryGetSystem<TaskSystem>(out var ts)) ts.PlayerContribution(ctx, v, dt);
        }

        private void StepPhysics(SimContext ctx, VehicleState v, VehicleDef def, float dt)
        {
            var t = ctx.Tuning;
            var terrain = ctx.Terrain;
            Vec2 prev = v.Pos;
            Vec2 fwd = Vec2.FromAngle(v.Heading);
            var dc = new DriveContext { Dt = dt };
            // contact result from the previous position is close enough for the physics inputs
            int under = ctx.World.Snow.CellIdAt(v.Pos);
            float density = under >= 0 ? ctx.World.Snow.ColumnDensity(under) : ctx.World.Snow.BackgroundDensity;
            float loose = (under >= 0 ? ctx.World.Snow.LooseMm[under] : ctx.World.Snow.BackgroundLooseMm) * 0.001f;
            float mass = def.MassKg + v.CargoKg + v.SaltKg + v.BrineL + v.WaterL;
            float bladeLoad = 0f;
            for (int i = 0; i < v.Mounted.Count; i++) { var a = ctx.Data.Attachment(v.Mounted[i].DefId); if (a != null) mass += a.MassKg; bladeLoad += v.Mounted[i].LoadKg; }
            dc.TotalMassKg = mass;
            dc.PowerAvailW = def.EnginePowerKw * 1000f * (1f - v.Condition.WearEngine * t.F("vehicles.enginePowerLossAtFullWear")) * MathUtil.Lerp(0.6f, 1f, v.WarmupFrac);
            if (!v.EngineOn) dc.PowerAvailW = 0f;
            dc.TractionCoeff = t.Curve("vehicles.tractionByDensity", density) * (1f - v.Condition.WearTracks * t.F("vehicles.trackGripLossAtFullWear"));
            dc.ShearKpa = t.Curve("vehicles.shearStrengthByDensity", density);
            dc.GroundPressureKpa = def.GroundPressureKpa * (mass / MathF.Max(1f, def.MassKg));
            dc.LooseDepthM = loose;
            dc.GradeAlongRad = terrain.GradeAlongDeg(v.Pos.X, v.Pos.Y, fwd) * MathUtil.Deg2Rad;
            dc.SideSlopeRad = terrain.GradeAlongDeg(v.Pos.X, v.Pos.Y, new Vec2(fwd.Y, -fwd.X)) * -MathUtil.Deg2Rad;
            dc.BladeLoadKg = bladeLoad;
            dc.Chains = def.TireSpec != null && def.TireSpec.Chains;
            dc.ImplementDragN = _lastDrag.TryGetValue(v.Id, out var ld) ? ld : 0f;
            dc.ImplementPowerW = _lastPower.TryGetValue(v.Id, out var lp) ? lp : 0f;

            DrivingModels.Step(ctx, v, def, ref dc);

            // keep inside the map
            float size = terrain.SizeM;
            if (v.Pos.X < 2f || v.Pos.Y < 2f || v.Pos.X > size - 2f || v.Pos.Y > size - 2f)
            {
                v.Pos = new Vec2(MathUtil.Clamp(v.Pos.X, 2f, size - 2f), MathUtil.Clamp(v.Pos.Y, 2f, size - 2f));
                v.Speed = 0f;
            }
            // gorge cells are impassable
            if (terrain.HasFlag(v.Pos.X, v.Pos.Y, Terrain.TerrainFlags.Gorge)) { v.Pos = prev; v.Speed = 0f; }

            var res = _contact.Apply(ctx, v, def, prev, dt);
            _lastDrag[v.Id] = res.ImplementDragN;
            _lastPower[v.Id] = res.ImplementPowerW;
            // visual pitch/roll from terrain
            v.PitchDeg = -dc.GradeAlongRad * MathUtil.Rad2Deg;
            v.RollDeg = dc.SideSlopeRad * MathUtil.Rad2Deg;
        }

        private readonly Dictionary<int, float> _lastDrag = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _lastPower = new Dictionary<int, float>();

        private static float BurnRate(VehicleDef def, VehicleState v, TuningData t)
        {
            var b = def.BurnLPerHour;
            float load = MathUtil.Clamp01(v.LoadFrac);
            bool working = v.Input.Tiller || v.Input.Implement;
            float rate;
            if (load < 0.1f && MathF.Abs(v.Speed) < 0.1f) rate = b.Idle;
            else if (working) rate = MathUtil.Lerp(b.Working, b.HighLoad, MathUtil.Clamp01((load - 0.5f) * 2f));
            else rate = MathUtil.Lerp(b.Idle, MathUtil.Lerp(b.Transit, b.HighLoad, MathUtil.Clamp01((load - 0.5f) * 2f)), MathUtil.Clamp01(load * 2f));
            rate *= 1f + v.Condition.WearEngine * t.F("vehicles.burnIncreaseAtFullWear");
            if (def.IsElectric) rate = def.EnginePowerKw * MathF.Max(0.03f, load); // kWh per hour
            return rate;
        }

        private static void ApplyWear(SimContext ctx, VehicleState v, VehicleDef def, float hours, TuningData t)
        {
            float basePerHour = 1f / t.F("vehicles.wearHoursToFailureAtFullLoad");
            float load = MathF.Max(t.F("vehicles.wearIdleFraction"), v.LoadFrac);
            float shelter = v.Sheltered ? 1f : ctx.Data.Stations.Garage.UnshelteredWearMultiplier;
            var c = v.Condition;
            var w = def.WearRates;
            float cold = v.WarmupFrac < 0.5f ? 1.5f : 1f;
            c.WearEngine = MathUtil.Clamp01(c.WearEngine + basePerHour * load * w.Engine * hours * shelter * cold);
            bool implement = v.Input.Tiller || v.Input.Implement || MathF.Abs(v.Input.BladeLift) > 0.1f;
            c.WearHydraulics = MathUtil.Clamp01(c.WearHydraulics + basePerHour * (implement ? 1f : 0.2f) * w.Hydraulics * hours * shelter * cold);
            c.WearDrivetrain = MathUtil.Clamp01(c.WearDrivetrain + basePerHour * load * w.Drivetrain * hours * shelter);
            float trackWear = basePerHour * (0.3f + MathF.Abs(v.Speed) / 5f) * w.TracksOrTires * hours + t.F("vehicles.trackWearPerSlipHour") * v.SlipFrac * hours;
            c.WearTracks = MathUtil.Clamp01(c.WearTracks + trackWear);
            for (int i = 0; i < v.Mounted.Count; i++)
            {
                var m = v.Mounted[i];
                if (!m.Engaged && m.Lift > 0.5f) continue;
                var a = ctx.Data.Attachment(m.DefId);
                float rate = a != null ? a.WearRate : 1f;
                m.Wear = MathUtil.Clamp01(m.Wear + t.F("vehicles.attachmentWearPerWorkHour") * rate * hours * w.Attachment);
                m.Hours += hours;
            }
            c.WearAttachment = 0f;
            for (int i = 0; i < v.Mounted.Count; i++) c.WearAttachment = MathF.Max(c.WearAttachment, v.Mounted[i].Wear);
        }

        private void RollFailures(SimContext ctx, VehicleState v, VehicleDef def, float hours, TuningData t)
        {
            if (v.Condition.IsDown) return;
            float mtbf = MathF.Max(20f, def.MtbfHours);
            float maxWear = MathF.Max(MathF.Max(v.Condition.WearEngine, v.Condition.WearHydraulics), MathF.Max(v.Condition.WearDrivetrain, v.Condition.WearTracks));
            float rate = t.Curve("vehicles.failureRateByWear", maxWear) / mtbf * MathF.Max(0.3f, v.LoadFrac + 0.3f);
            float p = MathUtil.Clamp01(rate * hours);
            if (!ctx.Rng.Chance(p)) return;
            // pick the subsystem weighted by wear
            float e = v.Condition.WearEngine + 0.05f, h = v.Condition.WearHydraulics + 0.05f, d = v.Condition.WearDrivetrain + 0.05f, tr = v.Condition.WearTracks + 0.05f;
            float r = ctx.Rng.NextFloat() * (e + h + d + tr);
            WearSubsystem sub; FailureKind kind; string parts; bool immob;
            if (r < e) { sub = WearSubsystem.Engine; kind = FailureKind.EngineFailure; parts = "engineKit"; immob = true; }
            else if (r < e + h) { sub = WearSubsystem.Hydraulics; kind = FailureKind.HydraulicFailure; parts = "hydraulicPump"; immob = false; }
            else if (r < e + h + d) { sub = WearSubsystem.Drivetrain; kind = FailureKind.DrivetrainFailure; parts = "drivetrainKit"; immob = true; }
            else { sub = WearSubsystem.TracksOrTires; kind = FailureKind.TrackFailure; parts = "trackPads"; immob = true; }
            float wear = v.Condition.Wear(sub);
            var f = new FailureState
            {
                Kind = kind, Subsystem = sub, Tick = ctx.Time.Tick, Immobilising = immob, PartsKind = parts,
                RepairCost = def.PurchasePrice * t.Curve("vehicles.repairCostFracByWear", wear),
                RepairHours = t.F("vehicles.repairHoursBase") * (0.5f + wear * 1.5f),
                Description = kind.ToString() + " (" + sub + " wear " + MathF.Round(wear * 100f) + "%)",
            };
            v.Condition.ActiveFailures.Add(f);
            v.Condition.FailuresTotal++;
            if (immob)
            {
                v.EngineOn = false; v.Input.Clear(); v.Speed = 0f;
                v.Ai.Mode = AiMode.Stranded;
                BlockTask(ctx, v, f.Description);
            }
            ctx.Events.Publish(new VehicleFailureEvent { VehicleId = v.Id, Failure = f });
            ctx.Sim.Log(v.Name + ": " + f.Description + " at " + v.LocationLabel + ".", LogLevel.Alert);
        }

        private void BlockTask(SimContext ctx, VehicleState v, string reason)
        {
            if (v.TaskId >= 0 && ctx.TryGetSystem<TaskSystem>(out var ts)) ts.Block(ctx, v.TaskId, v.Name + " " + reason);
        }

        private void UnblockTask(SimContext ctx, VehicleState v)
        {
            if (v.TaskId >= 0 && ctx.TryGetSystem<TaskSystem>(out var ts)) ts.Unblock(ctx, v.TaskId);
        }

        private void UpdateLocationLabel(SimContext ctx, VehicleState v)
        {
            var net = ctx.World.Pistes;
            string best = "off-piste"; float bd = float.MaxValue;
            foreach (var l in ctx.Sim.Scenario.Landmarks)
            {
                float d = Vec2.Distance(v.Pos, l.Pos);
                if (d < 60f && d < bd) { bd = d; best = l.Id; }
            }
            if (bd == float.MaxValue)
            {
                int cell = ctx.World.Snow.CellIdAt(v.Pos);
                if (cell >= 0)
                {
                    int seg = ctx.World.Snow.Segment[cell];
                    if (seg >= 0) { var s = net.Segment(seg); var p = s != null ? net.Piste(s.PisteId) : null; if (p != null) best = p.Name; }
                    else
                    {
                        var st = ctx.World.Snow.SurfaceOf(cell);
                        if (st == Snow.SurfaceType.Road) best = "access road";
                        else if (st == Snow.SurfaceType.Lot) best = "parking";
                        else if (st == Snow.SurfaceType.BaseArea) best = "base area";
                        else if (st == Snow.SurfaceType.LiftRamp) best = "lift ramp";
                    }
                }
            }
            v.LocationLabel = best;
        }
    }
}
