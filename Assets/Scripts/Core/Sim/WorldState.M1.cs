using AlpineSim.Core.Pistes;
using AlpineSim.Core.Snow;
using AlpineSim.Core.Weather;

namespace AlpineSim.Core.Sim
{
    public sealed partial class WorldState
    {
        /// <summary>The shared state machine (M1).</summary>
        public SnowGrid Snow;
        public PisteNetwork Pistes = new PisteNetwork();
        public WeatherState Weather = new WeatherState();
    }
}
