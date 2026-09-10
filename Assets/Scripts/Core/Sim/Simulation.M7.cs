using System.Collections.Generic;
using AlpineSim.Core.Scaffold;

namespace AlpineSim.Core.Sim
{
    public sealed partial class Simulation
    {
        partial void RegisterM7Systems(List<ISimSystem> systems) { systems.Add(new M7ScaffoldSystem()); }
    }
}
