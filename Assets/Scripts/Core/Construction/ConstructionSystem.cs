using System;
using System.Collections.Generic;
using AlpineSim.Core.Lifts;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Construction
{
    /// <summary>
    /// Lift and run building as a driven job chain (construction.json stages). Staking evaluates
    /// the line against the type's max length / vertical / span / grade and the terrain's
    /// NoFoundation cells (span = longest gap between placeable tower sites), returning reasons
    /// that name the constraint. Committing creates the lift (UnderConstruction: a visible,
    /// blocking object earning nothing), the project, and the current stage's tasks on the job
    /// board. Stages advance on TaskCompletedEvent; stages with weather windows wait; helicopter
    /// mode replaces crane tower setting with paid flight hours; the final Inspection stage opens
    /// the lift (Closed, ready to open). Run projects clear and grade a corridor, then add the piste
    /// to the network and the snow grid. Ticks after Lifts, before Guests.
    /// </summary>
    public sealed class ConstructionSystem : ISimSystem
    {
        public string Name => "Construction";

        public void Initialize(SimContext ctx, bool newGame) => throw new NotImplementedException("M4: ConstructionSystem.Initialize");
        public void Tick(SimContext ctx, float dt) => throw new NotImplementedException("M4: ConstructionSystem.Tick");

        public IReadOnlyList<ConstructionProject> Projects(SimContext ctx) => ctx.World.Construction.Projects;

        /// <summary>Terrain gating and costing for a staked line. Never mutates state.</summary>
        public LiftPlanResult Stake(SimContext ctx, string typeId, Vec2 bottom, Vec2 top, List<string> options = null) => throw new NotImplementedException("M4");
        /// <summary>Starts building; pays the first stage; fails with a reason when the plan is invalid, the act does not allow the tier, or cash is short.</summary>
        public ConstructionProject Commit(SimContext ctx, LiftPlanResult plan, string name, List<string> options, bool helicopterMode, out string reason) => throw new NotImplementedException("M4");
        /// <summary>Stakes a new run corridor (returns reasons when the grade or width is unusable).</summary>
        public bool StakeRun(SimContext ctx, string id, string name, PisteDifficulty difficulty, float widthM, List<Vec2> points, string topNodeId, string bottomNodeId, out double cost, out List<string> reasons) => throw new NotImplementedException("M4");
        public ConstructionProject CommitRun(SimContext ctx, string id, string name, PisteDifficulty difficulty, float widthM, List<Vec2> points, string topNodeId, string bottomNodeId, out string reason) => throw new NotImplementedException("M4");
        public bool Cancel(SimContext ctx, int projectId, out string reason) => throw new NotImplementedException("M4");
        public void SetHelicopterMode(SimContext ctx, int projectId, bool on) => throw new NotImplementedException("M4");
        /// <summary>Tower sites along a line for a type, shifted off NoFoundation cells; used by staking and by the lift view.</summary>
        public List<Vec2> PlaceTowers(SimContext ctx, LiftTypeDef type, Vec2 bottom, Vec2 top, out float maxSpanM) => throw new NotImplementedException("M4");
    }
}
