using System;
using System.Collections.Generic;
using AlpineSim.Core.Random;
using AlpineSim.Core.Save;

namespace AlpineSim.Core.Sim
{
    /// <summary>
    /// The complete, plain, serializable state of one game. Save = JSON of this object.
    /// Each milestone contributes its own slice as a partial-class file (WorldState.M1.cs, ...).
    /// Rules: public fields only, parameterless constructors, no references to systems or views,
    /// no derived caches unless marked [JsonIgnore].
    /// </summary>
    [Serializable]
    public sealed partial class WorldState
    {
        public int SchemaVersion = SaveSchema.CurrentVersion;
        public string ScenarioId = "";
        public int Seed;
        public XorShift128Plus Rng = new XorShift128Plus();
        public SimTime Time = new SimTime();
        public List<GameLogEntry> Log = new List<GameLogEntry>();
        public const int MaxLogEntries = 400;
    }
}
