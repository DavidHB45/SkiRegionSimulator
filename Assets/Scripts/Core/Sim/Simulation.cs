using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Save;
using AlpineSim.Core.Terrain;

namespace AlpineSim.Core.Sim
{
    /// <summary>
    /// Composition root of the simulation. Owns the WorldState, the derived terrain, the event bus
    /// and the ordered list of systems. Milestone partial files (Simulation.M1.cs ...) register
    /// their systems through the partial Register hooks so each milestone commit stays buildable.
    ///
    /// Tick order is the registration order and is documented in docs/ARCHITECTURE.md.
    /// </summary>
    public sealed partial class Simulation
    {
        public GameData Data { get; }
        public WorldState World { get; }
        public TerrainData Terrain { get; }
        public SimEvents Events { get; }
        public SimContext Ctx { get; }
        public ScenarioData Scenario { get; }

        private readonly List<ISimSystem> _systems = new List<ISimSystem>();
        private readonly Dictionary<Type, ISimSystem> _byType = new Dictionary<Type, ISimSystem>();
        public IReadOnlyList<ISimSystem> Systems => _systems;

        private static readonly Dictionary<string, TerrainData> TerrainCache = new Dictionary<string, TerrainData>();

        private Simulation(GameData data, WorldState world, bool newGame)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            World = world ?? throw new ArgumentNullException(nameof(world));
            Scenario = data.GetScenario(world.ScenarioId);
            Terrain = GetOrBuildTerrain(Scenario, world.Seed);
            Events = new SimEvents();
            Ctx = new SimContext(this);
            RegisterSystems();
            foreach (var s in _systems) s.Initialize(Ctx, newGame);
        }

        /// <summary>Starts a fresh game from a scenario. Same seed + same inputs = same state forever.</summary>
        public static Simulation CreateNew(GameData data, int seed, string scenarioId = null)
        {
            var scenario = data.GetScenario(scenarioId);
            var world = new WorldState
            {
                ScenarioId = scenario.Id,
                Seed = seed,
                Rng = new Random.XorShift128Plus(seed),
            };
            world.Time.StartHour = scenario.StartHour;
            world.Time.StartDayOfWeek = scenario.StartDayOfWeek;
            world.Time.StartSeasonDay = scenario.StartSeasonDay;
            return new Simulation(data, world, true);
        }

        /// <summary>Resumes from a loaded (already migrated) WorldState.</summary>
        public static Simulation FromState(GameData data, WorldState world) => new Simulation(data, world, false);

        private static TerrainData GetOrBuildTerrain(ScenarioData scenario, int seed)
        {
            string key = scenario.Id + "#" + seed;
            lock (TerrainCache)
            {
                if (TerrainCache.TryGetValue(key, out var t)) return t;
                t = TerrainGenerator.Build(scenario, seed);
                if (TerrainCache.Count > 8) TerrainCache.Clear();
                TerrainCache[key] = t;
                return t;
            }
        }

        // ------------------------------------------------------------------ system registry
        private void RegisterSystems()
        {
            RegisterM1Systems(_systems);
            RegisterM2Systems(_systems);
            RegisterM3Systems(_systems);
            RegisterM4Systems(_systems);
            RegisterM5Systems(_systems);
            RegisterM6Systems(_systems);
            RegisterM7Systems(_systems);
            RegisterM8Systems(_systems);
            OrderSystems(_systems);
            _byType.Clear();
            foreach (var s in _systems) _byType[s.GetType()] = s;
        }

        // Implemented in Simulation.Mn.cs. Absent implementations compile to nothing.
        partial void RegisterM1Systems(List<ISimSystem> systems);
        partial void RegisterM2Systems(List<ISimSystem> systems);
        partial void RegisterM3Systems(List<ISimSystem> systems);
        partial void RegisterM4Systems(List<ISimSystem> systems);
        partial void RegisterM5Systems(List<ISimSystem> systems);
        partial void RegisterM6Systems(List<ISimSystem> systems);
        partial void RegisterM7Systems(List<ISimSystem> systems);
        partial void RegisterM8Systems(List<ISimSystem> systems);

        /// <summary>
        /// Canonical tick order, independent of which milestones are compiled in. Systems not in the
        /// table keep their registration order after the known ones.
        /// </summary>
        public static readonly string[] TickOrder =
        {
            "Weather", "Snowmaking", "Snow", "Roads", "Lifts", "Construction", "Guests",
            "Vehicles", "Tasks", "Fleet", "Economy", "Campaign", "Scaffold"
        };

        private static void OrderSystems(List<ISimSystem> systems)
        {
            int Rank(ISimSystem s)
            {
                int i = Array.IndexOf(TickOrder, s.Name);
                return i < 0 ? TickOrder.Length : i;
            }
            // stable insertion sort by rank
            for (int i = 1; i < systems.Count; i++)
            {
                var cur = systems[i];
                int j = i - 1;
                while (j >= 0 && Rank(systems[j]) > Rank(cur)) { systems[j + 1] = systems[j]; j--; }
                systems[j + 1] = cur;
            }
        }

        public T GetSystem<T>() where T : class
        {
            if (TryGetSystem<T>(out var s)) return s;
            throw new InvalidOperationException("System " + typeof(T).Name + " is not registered in this build");
        }

        public bool TryGetSystem<T>(out T system) where T : class
        {
            if (_byType.TryGetValue(typeof(T), out var s)) { system = (T)s; return true; }
            foreach (var sys in _systems) if (sys is T t) { system = t; return true; }
            system = null;
            return false;
        }

        // ------------------------------------------------------------------ stepping
        /// <summary>Advance exactly one 20 Hz tick.</summary>
        public void Step()
        {
            var time = World.Time;
            time.Tick++;
            float dt = SimTime.Dt;
            for (int i = 0; i < _systems.Count; i++) _systems[i].Tick(Ctx, dt);
            if (time.IsHourStart)
            {
                Events.Publish(new HourChangedEvent { AbsoluteHour = time.AbsoluteHour, Day = time.Day, HourOfDay = time.HourOfDay });
                if (time.IsDayStart) Events.Publish(new DayChangedEvent { Day = time.Day });
            }
        }

        public void StepTicks(long ticks) { for (long i = 0; i < ticks; i++) Step(); }
        public void StepSeconds(double seconds) => StepTicks((long)System.Math.Round(seconds * SimTime.TicksPerSecond));
        public void StepMinutes(double minutes) => StepTicks((long)System.Math.Round(minutes * SimTime.TicksPerMinute));
        public void StepHours(double hours) => StepTicks((long)System.Math.Round(hours * SimTime.TicksPerHour));
        public void StepDays(double days) => StepTicks((long)System.Math.Round(days * SimTime.TicksPerDay));

        /// <summary>Steps until the calendar reaches the given hour-of-day (next occurrence).</summary>
        public void StepUntilHour(int hourOfDay)
        {
            long target = World.Time.AbsoluteHour + 1;
            while ((target % 24) != hourOfDay) target++;
            long targetTick = target * SimTime.TicksPerHour - (long)World.Time.StartHour * SimTime.TicksPerHour;
            StepTicks(targetTick - World.Time.Tick);
        }

        public ulong ComputeStateHash() => StateHasher.Hash(World);

        // ------------------------------------------------------------------ logging
        public void Log(string message, LogLevel level = LogLevel.Info)
        {
            var e = new GameLogEntry { Tick = World.Time.Tick, Level = level, Message = message };
            World.Log.Add(e);
            if (World.Log.Count > WorldState.MaxLogEntries) World.Log.RemoveRange(0, World.Log.Count - WorldState.MaxLogEntries);
            Events.Publish(new LogEvent { Entry = e });
        }
    }
}
