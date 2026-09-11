using System.Collections.Generic;
using AlpineSim.Core.Economy;
using AlpineSim.Core.Guests;
using AlpineSim.Core.Lifts;

namespace AlpineSim.Core.Sim
{
    public sealed partial class Simulation
    {
        partial void RegisterM3Systems(List<ISimSystem> systems)
        {
            systems.Add(new LiftSystem());
            systems.Add(new GuestSystem());
            systems.Add(new EconomySystem());
        }
    }
}
