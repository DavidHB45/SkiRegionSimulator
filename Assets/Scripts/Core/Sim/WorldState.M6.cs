using AlpineSim.Core.Fleet;

namespace AlpineSim.Core.Sim
{
    public sealed partial class WorldState
    {
        /// <summary>Garage, market, operators, service, fuel logistics (M6).</summary>
        public FleetState Fleet = new FleetState();
    }
}
