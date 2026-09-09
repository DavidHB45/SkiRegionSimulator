using System;

namespace AlpineSim.Core.Sim
{
    /// <summary>
    /// Accumulates real time and steps the simulation at a fixed 20 Hz. Time compression changes
    /// how many ticks are consumed per unit of real time, never the tick length.
    /// Lives outside WorldState: it is a driver, not state.
    /// </summary>
    public sealed class SimulationClock
    {
        public static readonly int[] CompressionLevels = { 1, 4, 16, 60 };

        private readonly Simulation _sim;
        private double _accumulator;

        public int Compression { get; private set; } = 1;
        public bool Paused { get; set; }
        /// <summary>Upper bound on ticks per Advance call so slow frames cannot spiral. Compression then slips, deterministically.</summary>
        public int MaxTicksPerAdvance { get; set; } = 400;
        public long TotalTicksStepped { get; private set; }

        public SimulationClock(Simulation sim) { _sim = sim ?? throw new ArgumentNullException(nameof(sim)); }

        public void SetCompression(int level)
        {
            for (int i = 0; i < CompressionLevels.Length; i++)
                if (CompressionLevels[i] == level) { Compression = level; return; }
            throw new ArgumentOutOfRangeException(nameof(level), "Compression must be one of 1, 4, 16, 60");
        }

        public void CycleCompression()
        {
            int idx = Array.IndexOf(CompressionLevels, Compression);
            Compression = CompressionLevels[(idx + 1) % CompressionLevels.Length];
        }

        /// <summary>Advance by real seconds; returns the number of ticks stepped.</summary>
        public int Advance(double realDeltaSeconds)
        {
            if (Paused || realDeltaSeconds <= 0) return 0;
            const double dt = 1.0 / SimTime.TicksPerSecond;
            _accumulator += realDeltaSeconds * Compression;
            int ticks = (int)System.Math.Floor(_accumulator / dt + 1e-9);
            if (ticks > MaxTicksPerAdvance) ticks = MaxTicksPerAdvance;
            _accumulator -= ticks * dt;
            if (_accumulator > dt * MaxTicksPerAdvance) _accumulator = dt * MaxTicksPerAdvance; // drop backlog
            for (int i = 0; i < ticks; i++) _sim.Step();
            TotalTicksStepped += ticks;
            return ticks;
        }
    }
}
