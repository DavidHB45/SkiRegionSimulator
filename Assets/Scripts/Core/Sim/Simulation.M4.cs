using System.Collections.Generic;
using AlpineSim.Core.Construction;

namespace AlpineSim.Core.Sim
{
    public sealed partial class Simulation
    {
        partial void RegisterM4Systems(List<ISimSystem> systems)
        {
            systems.Add(new ConstructionSystem());
        }
    }
}
