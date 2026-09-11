using System.Collections.Generic;
using AlpineSim.Core.Snow;

namespace AlpineSim.Core.Sim
{
    public sealed partial class Simulation
    {
        partial void RegisterM1Systems(List<ISimSystem> systems)
        {
            systems.Add(new SnowSystem());
        }
    }
}
