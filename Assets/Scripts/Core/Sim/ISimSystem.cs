namespace AlpineSim.Core.Sim
{
    /// <summary>
    /// A simulation system. Systems are ticked in a fixed registration order (see Simulation)
    /// at exactly 20 Hz. They read and write WorldState only; they never hold state that is
    /// not either in WorldState or rebuildable from it in Initialize.
    /// </summary>
    public interface ISimSystem
    {
        string Name { get; }

        /// <summary>
        /// Called once after the Simulation is constructed. When <paramref name="newGame"/> is true the
        /// system must populate its slice of WorldState from the scenario; otherwise it rebuilds caches
        /// from the loaded state.
        /// </summary>
        void Initialize(SimContext ctx, bool newGame);

        void Tick(SimContext ctx, float dt);
    }
}
