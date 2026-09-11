using System.Collections.Generic;
using AlpineSim.Core.Fleet;
using AlpineSim.Core.Roads;

namespace AlpineSim.Core.Sim
{
    public sealed partial class Simulation
    {
        partial void RegisterM6Systems(List<ISimSystem> systems)
        {
            systems.Add(new RoadSystem());
            systems.Add(new FleetSystem());
        }
    }
}
