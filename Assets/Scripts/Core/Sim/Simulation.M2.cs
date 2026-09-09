using System.Collections.Generic;
using AlpineSim.Core.Tasks;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Sim
{
    public sealed partial class Simulation
    {
        partial void RegisterM2Systems(List<ISimSystem> systems)
        {
            systems.Add(new VehicleSystem());
            systems.Add(new TaskSystem());
        }
    }
}
