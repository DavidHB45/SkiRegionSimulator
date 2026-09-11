using AlpineSim.Core.Data;
using AlpineSim.Core.Random;
using AlpineSim.Core.Terrain;

namespace AlpineSim.Core.Sim
{
    /// <summary>Everything a system may touch during a tick. There is no other global state.</summary>
    public sealed class SimContext
    {
        public readonly Simulation Sim;

        public SimContext(Simulation sim) { Sim = sim; }

        public WorldState World => Sim.World;
        public GameData Data => Sim.Data;
        public TuningData Tuning => Sim.Data.Tuning;
        public XorShift128Plus Rng => Sim.World.Rng;
        public SimTime Time => Sim.World.Time;
        public SimEvents Events => Sim.Events;
        public TerrainData Terrain => Sim.Terrain;
        public float Dt => SimTime.Dt;

        public T System<T>() where T : class => Sim.GetSystem<T>();

        /// <summary>
        /// An independent PRNG stream derived from the world seed and a salt, independent of the main
        /// stream's position. Use for content that must be identical whenever it is (re)generated
        /// (weather days, forecasts, used-market listings for a given day).
        /// </summary>
        public XorShift128Plus SeededRng(int salt)
        {
            ulong x = unchecked((ulong)(long)World.Seed * 0x9E3779B97F4A7C15UL) ^ unchecked((ulong)(long)salt * 0xC2B2AE3D27D4EB4FUL);
            ulong a = XorShift128Plus.SplitMix64(ref x);
            ulong b = XorShift128Plus.SplitMix64(ref x);
            return new XorShift128Plus(a, b);
        }
        public bool TryGetSystem<T>(out T system) where T : class => Sim.TryGetSystem(out system);

        /// <summary>Append a player-visible log line (persisted in WorldState.Log).</summary>
        public void Log(string message, LogLevel level = LogLevel.Info) => Sim.Log(message, level);
    }
}
