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
    /// Work accrual contract: a vehicle counts as working on its assigned task when it is within
    /// SiteRadiusM of Site with the relevant implement on / Work pressed / cargo delivered, and the
    /// rate is tasks.json baseRatePerHour x VehicleSystem.WorkRate x operator competence.
    /// Groom tasks are special: WorkRequired is the piste area in m² and the vehicle system reports
    /// tilled m² through ReportWork. Haul tasks complete when QuantityDelivered >= QuantityRequired.
    /// Auto-generated tasks: drifted lift ramps (BlowRamp), snowed-in roads and lots (PlowRoad,
    /// ClearLot, Spread when icy), stranded machines (Refuel/Repair via the fleet system).
    /// </summary>
    public sealed class TaskSystem : ISimSystem
    {
        public string Name => "Tasks";

        public void Initialize(SimContext ctx, bool newGame) => throw new NotImplementedException("M2: TaskSystem.Initialize");
        public void Tick(SimContext ctx, float dt) => throw new NotImplementedException("M2: TaskSystem.Tick");

        public IReadOnlyList<WorkTask> All(SimContext ctx) => ctx.World.TaskBoard.Tasks;
        public WorkTask Get(SimContext ctx, int id) => ctx.World.TaskBoard.Get(id);

        /// <summary>Creates a task; roles/licence/site radius default from tasks.json for the kind.</summary>
        public WorkTask Create(SimContext ctx, TaskKind kind, string title, Vec2 site, string targetId = null, int targetIndex = -1,
            float workRequired = 1f, float priority = 1f, int projectId = -1, bool playerCreated = false) => throw new NotImplementedException("M2");
        public bool Cancel(SimContext ctx, int taskId) => throw new NotImplementedException("M2");
        /// <summary>Assigns a machine (and its operator, if any). Fails with a reason when roles or licence do not match or the machine is down.</summary>
        public bool Assign(SimContext ctx, int taskId, int vehicleId, out string reason) => throw new NotImplementedException("M2");
        public void Unassign(SimContext ctx, int taskId) => throw new NotImplementedException("M2");
        /// <summary>Whether this machine could take the task, with the blocking reason otherwise ("needs role Groom", "operator lacks licence Crane", "down for service", "out of fuel").</summary>
        public bool CanVehicleDo(SimContext ctx, WorkTask task, VehicleState vehicle, out string reason) => throw new NotImplementedException("M2");
        /// <summary>All machines with eligibility and reasons, for the fleet/task screens.</summary>
        public void Candidates(SimContext ctx, WorkTask task, List<TaskCandidate> into) => throw new NotImplementedException("M2");
        /// <summary>Adds completed work units (called by the vehicle system).</summary>
        public void ReportWork(SimContext ctx, int taskId, float units) => throw new NotImplementedException("M2");
        public void ReportDelivery(SimContext ctx, int taskId, float quantity) => throw new NotImplementedException("M2");
        public void Block(SimContext ctx, int taskId, string reason) => throw new NotImplementedException("M2");
        public void Unblock(SimContext ctx, int taskId) => throw new NotImplementedException("M2");
        /// <summary>Task the player's vehicle is currently contributing to at its position (auto-attached), or null.</summary>
        public WorkTask TaskAt(SimContext ctx, VehicleState vehicle) => throw new NotImplementedException("M2");
    }

    public struct TaskCandidate
    {
        public int VehicleId;
        public bool Eligible;
        public string Reason;
    }
}
