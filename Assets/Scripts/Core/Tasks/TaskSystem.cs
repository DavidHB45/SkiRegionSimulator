using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Tasks
{
    /// <summary>
    /// The job board. Tasks declare required roles/licence and a site; machines (player-driven or
    /// AI-operated) accrue work at the site; completion publishes TaskCompletedEvent which the
    /// construction, fleet and snowmaking systems consume. Ticks after Vehicles.
    ///
    /// Work accrual: an AI machine assigned to a Groom / zone / haul task accrues through the vehicle
    /// system's callbacks; a machine assigned to a site task accrues tasks.json baseRatePerHour x
    /// VehicleSystem.WorkRate x competence while within SiteRadiusM with Work engaged. The player's
    /// machine is attached automatically to the nearest open task it qualifies for at its position.
    /// </summary>
    public sealed class TaskSystem : ISimSystem
    {
        public string Name => "Tasks";

        /// <summary>Supplied by the fleet system (M6) to check operator licences; M2 treats every operator as licensed.</summary>
        public Func<SimContext, VehicleState, OperatorLicense, bool> LicenceCheck;
        public Func<SimContext, VehicleState, float> CompetenceLookup;

        public void Initialize(SimContext ctx, bool newGame)
        {
            if (ctx.World.TaskBoard == null) ctx.World.TaskBoard = new TaskBoardState();
        }

        public void Tick(SimContext ctx, float dt)
        {
            var board = ctx.World.TaskBoard;
            var vs = ctx.System<VehicleSystem>();
            for (int i = 0; i < board.Tasks.Count; i++)
            {
                var task = board.Tasks[i];
                if (!task.IsActive) continue;
                if (task.DeadlineTick >= 0 && ctx.Time.Tick > task.DeadlineTick && task.Status != TaskStatus.Done)
                {
                    if (task.AssignedVehicleId >= 0) Unassign(ctx, task.Id);
                    task.Status = TaskStatus.Cancelled;
                    task.Notes = "deadline passed";
                    ctx.Events.Publish(new TaskCancelledEvent { TaskId = task.Id });
                    continue;
                }
                if (task.AssignedVehicleId < 0) continue;
                var v = vs.Get(ctx, task.AssignedVehicleId);
                if (v == null) { Unassign(ctx, task.Id); continue; }
                // site work by an AI machine
                if (!v.PlayerControlled && v.Ai.Mode == AiMode.WorkAtSite && v.Ai.TaskId == task.Id && task.Status == TaskStatus.InProgress)
                {
                    float units = RatePerSecond(ctx, task, v) * dt;
                    if (units > 0f) ReportWork(ctx, task.Id, units);
                }
                if (task.Status == TaskStatus.Assigned && v.Ai.Mode != AiMode.Idle && !v.PlayerControlled) task.Status = TaskStatus.InProgress;
            }
            // groom coverage: any active groom task completes when the target piste is freshly groomed
            if (ctx.Time.IsMinuteStart)
            {
                for (int i = 0; i < board.Tasks.Count; i++)
                {
                    var task = board.Tasks[i];
                    if (task.Kind != TaskKind.Groom || task.Status != TaskStatus.InProgress) continue;
                    float cov = GroomCoverage(ctx, task.TargetId, task.CreatedTick);
                    if (cov > 0f) ReportGroomCoverage(ctx, task.Id, cov);
                }
            }
            // evening job board: one groom job per open, groomable piste that needs it
            if (ctx.Time.IsHourStart && ctx.Time.HourOfDay == ctx.Tuning.I("simulation.resortCloseHour") && ctx.Tuning.I("tasks.autoGroomJobs") != 0)
                PostEveningGroomJobs(ctx);
            // the night foreman: idle machines with a free operator take open jobs by priority
            if (ctx.Time.IsMinuteStart && ctx.Tuning.I("tasks.autoDispatch") != 0) AutoDispatch(ctx);
            // prune finished tasks older than a day
            if (ctx.Time.IsHourStart)
            {
                long cutoff = ctx.Time.Tick - SimTime.TicksPerDay;
                board.Tasks.RemoveAll(t => (t.Status == TaskStatus.Done || t.Status == TaskStatus.Cancelled) && t.CompletedTick >= 0 && t.CompletedTick < cutoff);
            }
        }

        private readonly List<WorkTask> _openTmp = new List<WorkTask>();
        /// <summary>Machine ids set aside for the job currently being dispatched (a list, not a set: iteration order must stay stable).</summary>
        private readonly List<int> _skipTmp = new List<int>();

        /// <summary>
        /// Foreman dispatch: every open job, highest priority first, goes to an idle AI machine that can do it.
        /// A machine without a driver gets a free, rested operator who holds the licence (the fleet system
        /// supplies that); the player's own machine and machines already on a job are left alone.
        /// </summary>
        public int AutoDispatch(SimContext ctx)
        {
            var board = ctx.World.TaskBoard;
            var vs = ctx.System<VehicleSystem>();
            ctx.TryGetSystem<Fleet.FleetSystem>(out var fleet);
            _openTmp.Clear();
            // a job one machine could not do goes back on the board after a while: another machine, a thawed road or a
            // refuelled tank may make it possible, and a blocked lot job must not stay blocked all winter
            long retry = (long)(ctx.Tuning.F("tasks.blockRetryMinutes") * SimTime.TicksPerMinute);
            for (int i = 0; i < board.Tasks.Count; i++)
            {
                var tk = board.Tasks[i];
                if (tk.Status == TaskStatus.Blocked && tk.AutoGenerated && tk.BlockedTick >= 0 && ctx.Time.Tick - tk.BlockedTick >= retry) Unblock(ctx, tk.Id);
                if (tk.Status == TaskStatus.Open && tk.AssignedVehicleId < 0) _openTmp.Add(tk);
            }
            if (_openTmp.Count == 0) return 0;
            _openTmp.Sort((a, b) => { int c = b.Priority.CompareTo(a.Priority); return c != 0 ? c : a.Id.CompareTo(b.Id); });
            int dispatched = 0;
            var vehicles = ctx.World.Vehicles.List;
            long sameMachine = (long)(ctx.Tuning.F("tasks.blockRetrySameMachineHours") * SimTime.TicksPerHour);
            float wearPenalty = ctx.Tuning.F("tasks.dispatchWearPenaltyM");
            int rescueAttempts = ctx.Tuning.I("tasks.rescueRetryAttempts");
            foreach (var task in _openTmp)
            {
                _skipTmp.Clear();
                // the hold is dropped only when it is the sole reason nobody is going: a road the one plow-capable
                // machine handed back must not sit unplowed for half the night because the fleet has no second plow
                bool ignoreHold = false;
                // at most one attempt per machine: a machine the fleet would not staff or the board would not assign
                // is set aside and the next best one tried, so one awkward machine cannot hold a job up
                for (int attempt = 0; attempt <= vehicles.Count + 1; attempt++)
                {
                    bool heldSomeoneOut = false;
                    VehicleState pick = null;
                    float bestDist = float.MaxValue;
                    for (int i = 0; i < vehicles.Count; i++)
                    {
                        var v = vehicles[i];
                        if (v.PlayerControlled || v.TaskId >= 0 || v.Ai.Mode != AiMode.Idle || !v.IsUsable || v.PlacedGunId >= 0) continue;
                        if (_skipTmp.Contains(v.Id)) continue;
                        // a machine that parked itself stuck is left for an hour: sending it straight back out only burns fuel
                        if (v.Ai.ParkedStuckTick >= 0 && ctx.Time.Tick - v.Ai.ParkedStuckTick < SimTime.TicksPerHour) continue;
                        // the machine that gave this job back does not get it again on the same shift: it would drive to
                        // the same pitch and stall there again (the old cat burned a quarter tank a night doing exactly
                        // that). A machine already stranded is the exception: somebody goes now, even the truck that
                        // turned back, because the alternative is a cat on the hill all night - but only so many times,
                        // because a stranded machine does not move and the pitch to it does not get any easier.
                        var refusal = task.Refusal(v.Id);
                        if (refusal != null && refusal.Tick >= 0)
                        {
                            if (IsRescue(task.Kind))
                            {
                                // past the cap the machine is not sent again even if nobody else can go: the stranded
                                // machine has not moved, so another identical attempt only burns the truck's own fuel
                                if (refusal.Count > rescueAttempts) continue;
                            }
                            else if (!ignoreHold && ctx.Time.Tick - refusal.Tick < sameMachine)
                            {
                                heldSomeoneOut = true;
                                continue;
                            }
                        }
                        var def = v.Def ?? ctx.Data.Vehicle(v.DefId);
                        if (def == null || def.IsStationary || def.ChassisType == ChassisType.Towed) continue;
                        // capability first, staffing last, and only for the machine finally picked: putting a driver into
                        // every candidate before knowing it could do the job shuffled the one cat driver between machines
                        // during a single scan, and the machine picked had lost them again by the time it was assigned
                        if (!CanVehicleDo(ctx, task, v, out _, v.OperatorId < 0)) continue;
                        if (v.OperatorId < 0 && (fleet == null || !fleet.CanStaffMachine(ctx, v, task.RequiredLicense))) continue;
                        // nearest wins among machines built for the job, the sound machine before the worn one;
                        // a groomer plows the lot only when nothing else can
                        float d = Vec2.Distance(v.Pos, task.Site) + (PreferredFor(task.Kind, def.Category) ? 0f : 5000f) + WorstWear(v) * wearPenalty;
                        if (d < bestDist) { bestDist = d; pick = v; }
                    }
                    if (pick == null)
                    {
                        // nobody went, and the only machines that could were the ones held out: send the best of them
                        // rather than leave the job standing. The board still waits tasks.blockRetryMinutes each round.
                        if (!ignoreHold && heldSomeoneOut) { ignoreHold = true; continue; }
                        break;
                    }
                    // staffing is the one step that can still fail on the machine the scan chose (the fleet's own checks
                    // are not the dispatcher's): the job then goes to the next best machine rather than waiting a minute
                    if (pick.OperatorId < 0 && (fleet == null || !fleet.StaffMachine(ctx, pick, task.RequiredLicense))) { _skipTmp.Add(pick.Id); continue; }
                    if (Assign(ctx, task.Id, pick.Id, out _)) { dispatched++; break; }
                    _skipTmp.Add(pick.Id);
                }
            }
            return dispatched;
        }

        /// <summary>Jobs that exist because a machine is stranded: they are never held back from any machine that can go.</summary>
        private static bool IsRescue(TaskKind kind) => kind == TaskKind.Refuel || kind == TaskKind.Repair || kind == TaskKind.Rescue;

        private static float WorstWear(VehicleState v)
        {
            var c = v.Condition;
            return MathF.Max(MathF.Max(c.WearEngine, c.WearHydraulics), MathF.Max(c.WearDrivetrain, c.WearTracks));
        }

        /// <summary>The machine classes a job is normally given to; any capable machine still qualifies when none of these is free.</summary>
        private static bool PreferredFor(TaskKind kind, VehicleCategory category)
        {
            switch (kind)
            {
                case TaskKind.Groom: return category == VehicleCategory.Groomer;
                case TaskKind.PlowRoad: case TaskKind.ClearLot: case TaskKind.Spread:
                    return category == VehicleCategory.RoadMaint || category == VehicleCategory.Truck || category == VehicleCategory.Tractor || category == VehicleCategory.Light || category == VehicleCategory.Loader;
                case TaskKind.BlowRamp: return category == VehicleCategory.Blower || category == VehicleCategory.Tractor || category == VehicleCategory.Light;
                case TaskKind.HaulSnow: case TaskKind.HaulMaterial: case TaskKind.PourConcrete: return category == VehicleCategory.Truck || category == VehicleCategory.Loader || category == VehicleCategory.Construction;
                case TaskKind.Refuel: case TaskKind.Repair: case TaskKind.Rescue: return category == VehicleCategory.Truck || category == VehicleCategory.Light;
                default: return true;
            }
        }

        /// <summary>Fraction of a piste's cells groomed since a tick (0..1). Cell lists are rebuilt on load by the piste builder.</summary>
        public float GroomCoverage(SimContext ctx, string pisteId, long sinceTick)
        {
            var grid = ctx.World.Snow;
            if (grid == null || grid.LastGroomTick == null) return 0f;
            int total = 0, fresh = 0;
            var segs = ctx.World.Pistes.Segments;
            for (int i = 0; i < segs.Count; i++)
            {
                var seg = segs[i];
                if (seg.PisteId != pisteId || seg.Cells == null) continue;
                var cells = seg.Cells;
                for (int c = 0; c < cells.Count; c++)
                {
                    int id = cells[c];
                    if (id < 0 || id >= grid.LastGroomTick.Length) continue;
                    total++;
                    if (grid.LastGroomTick[id] >= sinceTick && grid.LastGroomTick[id] > 0) fresh++;
                }
            }
            return total > 0 ? fresh / (float)total : 0f;
        }

        /// <summary>Creates the night's grooming jobs: every open groomable piste whose PQI is below the threshold and has no active job.</summary>
        public int PostEveningGroomJobs(SimContext ctx)
        {
            float below = ctx.Tuning.F("tasks.groomJobPqiBelow");
            int posted = 0;
            var pistes = ctx.World.Pistes.Pistes;
            long deadline = ctx.Time.Tick + (long)((24 - ctx.Time.HourOfDay + ctx.Tuning.I("simulation.resortOpenHour")) % 24) * SimTime.TicksPerHour;
            for (int i = 0; i < pistes.Count; i++)
            {
                var p = pistes[i];
                if (!p.Open || !p.Groomable || p.Points.Count < 2) continue;
                if (p.Pqi >= below) continue;
                if (HasActiveGroomJob(ctx, p.Id)) continue;
                var task = Create(ctx, TaskKind.Groom, "Groom " + p.Name, p.Points[p.Points.Count - 1], p.Id, -1, 1f, 1f + (below - p.Pqi) / 100f);
                task.AutoGenerated = true;
                task.DeadlineTick = deadline;
                posted++;
            }
            if (posted > 0) ctx.Sim.Log("Job board: " + posted + " grooming job" + (posted == 1 ? "" : "s") + " posted for tonight.");
            return posted;
        }

        public bool HasActiveGroomJob(SimContext ctx, string pisteId)
        {
            var tasks = ctx.World.TaskBoard.Tasks;
            for (int i = 0; i < tasks.Count; i++) if (tasks[i].Kind == TaskKind.Groom && tasks[i].TargetId == pisteId && tasks[i].IsActive) return true;
            return false;
        }

        private float RatePerSecond(SimContext ctx, WorkTask task, VehicleState v)
        {
            var kind = ctx.Data.Tasks.Kind(task.Kind);
            float baseRate = kind != null ? kind.BaseRatePerHour : 1f;
            VehicleRole role = task.RequiredRoles.Count > 0 ? task.RequiredRoles[0] : VehicleRole.Construct;
            float rate = ctx.System<VehicleSystem>().WorkRate(ctx, v, role);
            float comp = CompetenceLookup != null ? CompetenceLookup(ctx, v) : 0.8f;
            return baseRate * rate * comp / 3600f;
        }

        public IReadOnlyList<WorkTask> All(SimContext ctx) => ctx.World.TaskBoard.Tasks;
        public WorkTask Get(SimContext ctx, int id) => ctx.World.TaskBoard.Get(id);

        public WorkTask Create(SimContext ctx, TaskKind kind, string title, Vec2 site, string targetId = null, int targetIndex = -1,
            float workRequired = 1f, float priority = 1f, int projectId = -1, bool playerCreated = false)
        {
            var board = ctx.World.TaskBoard;
            var def = ctx.Data.Tasks.Kind(kind);
            var task = new WorkTask
            {
                Id = board.NextId++,
                Kind = kind,
                Title = title,
                Site = site,
                TargetId = targetId ?? "",
                TargetIndex = targetIndex,
                WorkRequired = MathF.Max(0.0001f, workRequired),
                Priority = priority,
                ProjectId = projectId,
                PlayerCreated = playerCreated,
                CreatedTick = ctx.Time.Tick,
                SiteRadiusM = def != null ? def.SiteRadiusM : 12f,
                RequiredLicense = def != null ? def.RequiredLicense : OperatorLicense.Basic,
            };
            if (def != null) task.RequiredRoles.AddRange(def.RequiredRoles);
            if (task.RequiredRoles.Count == 0) task.RequiredRoles.Add(DefaultRole(kind));
            board.Tasks.Add(task);
            ctx.Events.Publish(new TaskCreatedEvent { TaskId = task.Id });
            return task;
        }

        private static VehicleRole DefaultRole(TaskKind kind)
        {
            switch (kind)
            {
                case TaskKind.Groom: return VehicleRole.Groom;
                case TaskKind.PlowRoad: case TaskKind.ClearLot: return VehicleRole.Plow;
                case TaskKind.Spread: return VehicleRole.Spread;
                case TaskKind.BlowRamp: return VehicleRole.Blow;
                case TaskKind.HaulSnow: case TaskKind.HaulMaterial: case TaskKind.PourConcrete: return VehicleRole.Haul;
                case TaskKind.Refuel: return VehicleRole.Refuel;
                case TaskKind.Repair: return VehicleRole.Service;
                case TaskKind.Rescue: return VehicleRole.Rescue;
                case TaskKind.SetTower: case TaskKind.PlaceTerminal: case TaskKind.HangCarriers: return VehicleRole.Lift;
                case TaskKind.StringRope: case TaskKind.PullTrackRopes: return VehicleRole.Tow;
                case TaskKind.Survey: case TaskKind.Inspect: return VehicleRole.Patrol;
                case TaskKind.MakeSnow: return VehicleRole.MakeSnow;
                case TaskKind.RelocateGun: case TaskKind.Transport: return VehicleRole.Transport;
                default: return VehicleRole.Construct;
            }
        }

        public bool Cancel(SimContext ctx, int taskId)
        {
            var task = Get(ctx, taskId);
            if (task == null || !task.IsActive) return false;
            Unassign(ctx, taskId);
            task.Status = TaskStatus.Cancelled;
            task.CompletedTick = ctx.Time.Tick;
            ctx.Events.Publish(new TaskCancelledEvent { TaskId = taskId });
            return true;
        }

        /// <summary>
        /// Whether the machine, as it stands, can take the task. With assumeStaffed the operator licence is not checked
        /// (the caller will staff the machine if it is picked).
        /// </summary>
        public bool CanVehicleDo(SimContext ctx, WorkTask task, VehicleState vehicle, out string reason, bool assumeStaffed = false)
        {
            var vs = ctx.System<VehicleSystem>();
            var def = vehicle.Def ?? ctx.Data.Vehicle(vehicle.DefId);
            reason = "";
            if (def == null) { reason = "unknown machine"; return false; }
            if (def.IsStationary) { reason = "fixed installation"; return false; }
            foreach (var role in task.RequiredRoles)
            {
                if (!vs.HasRole(ctx, vehicle, role)) { reason = "needs role " + role + (RoleNeedsAttachment(role) ? " (mount the implement)" : ""); return false; }
            }
            if (vehicle.Condition.IsDown) { reason = "down for repair"; return false; }
            if (vehicle.Stranded) { reason = vehicle.StrandedReason; return false; }
            if (vehicle.Rented && vehicle.RentalEndDay >= 0 && ctx.Time.Day > vehicle.RentalEndDay) { reason = "rental expired"; return false; }
            if (!assumeStaffed && LicenceCheck != null && !LicenceCheck(ctx, vehicle, task.RequiredLicense)) { reason = "operator lacks licence " + task.RequiredLicense; return false; }
            if (task.AssignedVehicleId >= 0 && task.AssignedVehicleId != vehicle.Id) { reason = "already assigned"; return false; }
            return true;
        }

        private static bool RoleNeedsAttachment(VehicleRole r) => r == VehicleRole.Groom || r == VehicleRole.Plow || r == VehicleRole.Blow || r == VehicleRole.Spread || r == VehicleRole.Load || r == VehicleRole.Lift;

        public void Candidates(SimContext ctx, WorkTask task, List<TaskCandidate> into)
        {
            into.Clear();
            foreach (var v in ctx.World.Vehicles.List)
            {
                bool ok = CanVehicleDo(ctx, task, v, out string reason);
                into.Add(new TaskCandidate { VehicleId = v.Id, Eligible = ok, Reason = reason });
            }
        }

        public bool Assign(SimContext ctx, int taskId, int vehicleId, out string reason)
        {
            var task = Get(ctx, taskId);
            var vs = ctx.System<VehicleSystem>();
            var v = vs.Get(ctx, vehicleId);
            reason = "";
            if (task == null || !task.IsActive) { reason = "task is not open"; return false; }
            if (v == null) { reason = "no such machine"; return false; }
            if (!CanVehicleDo(ctx, task, v, out reason)) return false;
            if (v.TaskId >= 0 && v.TaskId != taskId) Unassign(ctx, v.TaskId);
            task.AssignedVehicleId = vehicleId;
            task.AssignedOperatorId = v.OperatorId;
            task.Status = TaskStatus.Assigned;
            task.BlockReason = "";
            v.TaskId = taskId;
            if (!v.PlayerControlled) Dispatch(ctx, task, v, vs);
            ctx.Events.Publish(new TaskAssignedEvent { TaskId = taskId, VehicleId = vehicleId, OperatorId = v.OperatorId });
            return true;
        }

        /// <summary>Sends the assigned machine back to its job; false when the task is closed or has no AI machine.</summary>
        public bool Resume(SimContext ctx, int taskId)
        {
            var task = Get(ctx, taskId);
            if (task == null || !task.IsActive || task.AssignedVehicleId < 0) return false;
            var vs = ctx.System<VehicleSystem>();
            var v = vs.Get(ctx, task.AssignedVehicleId);
            if (v == null || v.PlayerControlled) return false;
            Dispatch(ctx, task, v, vs);
            return true;
        }

        private void Dispatch(SimContext ctx, WorkTask task, VehicleState v, VehicleSystem vs)
        {
            switch (task.Kind)
            {
                case TaskKind.Groom: vs.AssignGroom(ctx, v.Id, task.TargetId, task.Id); break;
                case TaskKind.PlowRoad: case TaskKind.ClearLot: vs.AssignZoneWork(ctx, v.Id, task.TargetId, AiMode.PlowZone, task.Id); break;
                case TaskKind.BlowRamp: vs.AssignZoneWork(ctx, v.Id, task.TargetId, AiMode.BlowZone, task.Id); break;
                case TaskKind.Spread: vs.AssignZoneWork(ctx, v.Id, task.TargetId, AiMode.SpreadZone, task.Id); break;
                case TaskKind.HaulSnow: case TaskKind.HaulMaterial: case TaskKind.PourConcrete:
                    vs.AssignHaul(ctx, v.Id, task.LoadAt, task.Site, task.CargoKind, MathF.Max(0f, task.QuantityRequired - task.QuantityDelivered), task.Id); break;
                default: vs.AssignSiteWork(ctx, v.Id, task.Site, task.Id, task.Kind == TaskKind.Refuel || task.Kind == TaskKind.Repair || task.Kind == TaskKind.Rescue); break;
            }
            task.Status = TaskStatus.InProgress;
        }

        public void Unassign(SimContext ctx, int taskId)
        {
            var task = Get(ctx, taskId);
            if (task == null) return;
            if (task.AssignedVehicleId >= 0)
            {
                var vs = ctx.System<VehicleSystem>();
                var v = vs.Get(ctx, task.AssignedVehicleId);
                if (v != null && v.TaskId == taskId)
                {
                    v.TaskId = -1;
                    if (!v.PlayerControlled) vs.ClearAi(ctx, v.Id);
                }
            }
            task.AssignedVehicleId = -1;
            task.AssignedOperatorId = -1;
            if (task.Status == TaskStatus.Assigned || task.Status == TaskStatus.InProgress) task.Status = TaskStatus.Open;
        }

        public void ReportWork(SimContext ctx, int taskId, float units)
        {
            var task = Get(ctx, taskId);
            if (task == null || !task.IsActive || units <= 0f) return;
            task.WorkDone += units;
            if (task.Status == TaskStatus.Assigned) task.Status = TaskStatus.InProgress;
            if (task.WorkDone >= task.WorkRequired && (task.QuantityRequired <= 0f || task.QuantityDelivered >= task.QuantityRequired)) Complete(ctx, task);
        }

        public void ReportDelivery(SimContext ctx, int taskId, float quantity)
        {
            var task = Get(ctx, taskId);
            if (task == null || !task.IsActive || quantity <= 0f) return;
            task.QuantityDelivered += quantity;
            if (task.QuantityDelivered >= task.QuantityRequired)
            {
                task.WorkDone = task.WorkRequired;
                Complete(ctx, task);
            }
        }

        private void Complete(SimContext ctx, WorkTask task)
        {
            task.Status = TaskStatus.Done;
            task.CompletedTick = ctx.Time.Tick;
            ctx.World.TaskBoard.CompletedTotal++;
            int vehicleId = task.AssignedVehicleId;
            if (vehicleId >= 0)
            {
                var vs = ctx.System<VehicleSystem>();
                var v = vs.Get(ctx, vehicleId);
                if (v != null && v.TaskId == task.Id) v.TaskId = -1;
            }
            ctx.Events.Publish(new TaskCompletedEvent { TaskId = task.Id, Kind = task.Kind, TargetId = task.TargetId, ProjectId = task.ProjectId });
            ctx.Sim.Log("Task done: " + task.Title + ".");
        }

        /// <summary>Blocks the task; byVehicleId names the machine that gave it back, which is then not offered it again for tasks.blockRetrySameMachineHours.</summary>
        public void Block(SimContext ctx, int taskId, string reason, int byVehicleId = -1)
        {
            var task = Get(ctx, taskId);
            if (task == null || !task.IsActive) return;
            task.Status = TaskStatus.Blocked;
            task.BlockReason = reason;
            task.BlockedTick = ctx.Time.Tick;
            // only a machine handing the job in writes a hold, and it writes its own: a weather hold or a broken
            // machine blocking the same task must not erase the record of who could not do it, and one machine
            // handing the job in must not release another machine's hold
            if (byVehicleId >= 0)
            {
                var r = task.Refusal(byVehicleId);
                if (r == null) { r = new TaskRefusal { VehicleId = byVehicleId }; task.Refusals.Add(r); }
                r.Tick = ctx.Time.Tick;
                r.Count++;
            }
            ctx.Events.Publish(new TaskBlockedEvent { TaskId = taskId, Reason = reason });
        }

        public void Unblock(SimContext ctx, int taskId)
        {
            var task = Get(ctx, taskId);
            if (task == null || task.Status != TaskStatus.Blocked) return;
            task.BlockReason = "";
            task.Status = task.AssignedVehicleId >= 0 ? TaskStatus.InProgress : TaskStatus.Open;
            if (task.AssignedVehicleId >= 0)
            {
                var vs = ctx.System<VehicleSystem>();
                var v = vs.Get(ctx, task.AssignedVehicleId);
                if (v != null && !v.PlayerControlled) Dispatch(ctx, task, v, vs);
            }
        }

        public WorkTask TaskAt(SimContext ctx, VehicleState vehicle)
        {
            var board = ctx.World.TaskBoard;
            WorkTask best = null; float bd = float.MaxValue;
            for (int i = 0; i < board.Tasks.Count; i++)
            {
                var task = board.Tasks[i];
                if (!task.IsActive) continue;
                if (task.AssignedVehicleId >= 0 && task.AssignedVehicleId != vehicle.Id) continue;
                float d = Vec2.Distance(task.Site, vehicle.Pos);
                float radius = task.Kind == TaskKind.Groom || task.Kind == TaskKind.PlowRoad || task.Kind == TaskKind.ClearLot || task.Kind == TaskKind.BlowRamp || task.Kind == TaskKind.Spread ? 400f : task.SiteRadiusM;
                if (d > radius) continue;
                if (!CanVehicleDo(ctx, task, vehicle, out _)) continue;
                if (d < bd) { bd = d; best = task; }
            }
            return best;
        }

        /// <summary>Called by the vehicle system for the player's machine each tick: attaches and accrues site work.</summary>
        public void PlayerContribution(SimContext ctx, VehicleState v, float dt)
        {
            if (!v.Input.Work && !v.Input.Tiller && !v.Input.Implement) return;
            var task = v.TaskId >= 0 ? Get(ctx, v.TaskId) : null;
            if (task == null || !task.IsActive) task = TaskAt(ctx, v);
            if (task == null) return;
            if (task.AssignedVehicleId != v.Id)
            {
                task.AssignedVehicleId = v.Id;
                task.AssignedOperatorId = v.OperatorId;
                task.Status = TaskStatus.InProgress;
                v.TaskId = task.Id;
                ctx.Events.Publish(new TaskAssignedEvent { TaskId = task.Id, VehicleId = v.Id, OperatorId = v.OperatorId });
            }
            if (task.Status == TaskStatus.Assigned) task.Status = TaskStatus.InProgress;
            bool siteKind = task.Kind != TaskKind.Groom && task.Kind != TaskKind.PlowRoad && task.Kind != TaskKind.ClearLot && task.Kind != TaskKind.BlowRamp && task.Kind != TaskKind.Spread && task.Kind != TaskKind.HaulSnow && task.Kind != TaskKind.HaulMaterial && task.Kind != TaskKind.PourConcrete;
            if (siteKind && v.Input.Work && Vec2.Distance(task.Site, v.Pos) <= task.SiteRadiusM)
            {
                ReportWork(ctx, task.Id, RatePerSecond(ctx, task, v) * dt);
            }
        }

        /// <summary>Player grooming progress: fresh-groomed fraction of the target piste's cells (0..1) mapped onto WorkRequired.</summary>
        public void ReportGroomCoverage(SimContext ctx, int taskId, float coverageFraction)
        {
            var task = Get(ctx, taskId);
            if (task == null || !task.IsActive) return;
            task.WorkDone = MathF.Max(task.WorkDone, coverageFraction * task.WorkRequired);
            if (task.WorkDone >= task.WorkRequired * 0.97f) { task.WorkDone = task.WorkRequired; Complete(ctx, task); }
        }
    }
}
