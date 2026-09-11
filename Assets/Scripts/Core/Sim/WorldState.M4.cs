using AlpineSim.Core.Construction;

namespace AlpineSim.Core.Sim
{
    public sealed partial class WorldState
    {
        /// <summary>Lift and run construction projects (M4).</summary>
        public ConstructionState Construction = new ConstructionState();
    }
}
