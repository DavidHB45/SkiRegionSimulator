using AlpineSim.Core.Economy;
using AlpineSim.Core.Guests;
using AlpineSim.Core.Lifts;

namespace AlpineSim.Core.Sim
{
    public sealed partial class WorldState
    {
        /// <summary>Lifts, guests and the books (M3).</summary>
        public LiftsState Lifts = new LiftsState();
        public GuestPopulationState Guests = new GuestPopulationState();
        public EconomyState Economy = new EconomyState();
    }
}
