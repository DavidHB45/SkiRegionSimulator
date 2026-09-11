using AlpineSim.Core.Snowmaking;

namespace AlpineSim.Core.Sim
{
    public sealed partial class WorldState
    {
        /// <summary>Snowmaking network (M5). Weather state was introduced in M1 and is driven by WeatherSystem from M5.</summary>
        public SnowmakingState Snowmaking = new SnowmakingState();
    }
}
