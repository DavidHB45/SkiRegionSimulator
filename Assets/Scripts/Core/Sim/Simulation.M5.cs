using System.Collections.Generic;
using AlpineSim.Core.Snowmaking;
using AlpineSim.Core.Weather;

namespace AlpineSim.Core.Sim
{
    public sealed partial class Simulation
    {
        partial void RegisterM5Systems(List<ISimSystem> systems)
        {
            systems.Add(new WeatherSystem());
            systems.Add(new SnowmakingSystem());
        }
    }
}
