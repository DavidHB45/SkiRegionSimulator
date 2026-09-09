using AlpineSim.Unity.UI;
using AlpineSim.Unity.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity
{
    public sealed partial class Bootstrap
    {
        public VehicleViewManager Vehicles { get; private set; }
        private TaskPanel _taskPanel;

        partial void StartM2()
        {
            var go = new GameObject("Vehicles");
            go.transform.SetParent(WorldRoot, false);
            Vehicles = go.AddComponent<VehicleViewManager>();
            Vehicles.Construct(this);
            Cameras.BindVehicles(Vehicles);
            if (_taskPanel == null) _taskPanel = Ui.RegisterWindow(new TaskPanel(), 2, 900f, 520f);
        }

        partial void TeardownM2()
        {
            Cameras.BindVehicles(null);
            Vehicles = null;
        }
    }
}
