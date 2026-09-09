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
        public bool TryGetSystem<T>(out T system) where T : class => Sim.TryGetSystem(out system);

        /// <summary>Append a player-visible log line (persisted in WorldState.Log).</summary>
        public void Log(string message, LogLevel level = LogLevel.Info) => Sim.Log(message, level);
    }
}
