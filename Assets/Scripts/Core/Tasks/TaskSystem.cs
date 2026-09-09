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
            // prune finished tasks older than a day
            if (ctx.Time.IsHourStart)
            {
                long cutoff = ctx.Time.Tick - SimTime.TicksPerDay;
                board.Tasks.RemoveAll(t => (t.Status == TaskStatus.Done || t.Status == TaskStatus.Cancelled) && t.CompletedTick >= 0 && t.CompletedTick < cutoff);
            }
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

        public bool CanVehicleDo(SimContext ctx, WorkTask task, VehicleState vehicle, out string reason)
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
            if (LicenceCheck != null && !LicenceCheck(ctx, vehicle, task.RequiredLicense)) { reason = "operator lacks licence " + task.RequiredLicense; return false; }
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
                default: vs.AssignSiteWork(ctx, v.Id, task.Site, task.Id); break;
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

        public void Block(SimContext ctx, int taskId, string reason)
        {
            var task = Get(ctx, taskId);
            if (task == null || !task.IsActive) return;
            task.Status = TaskStatus.Blocked;
            task.BlockReason = reason;
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
