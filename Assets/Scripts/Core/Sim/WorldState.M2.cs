using AlpineSim.Core.Tasks;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Sim
{
    public sealed partial class WorldState
    {
        /// <summary>Machines and the job board (M2).</summary>
        public VehicleFleetState Vehicles = new VehicleFleetState();
        public TaskBoardState TaskBoard = new TaskBoardState();
    }
}
