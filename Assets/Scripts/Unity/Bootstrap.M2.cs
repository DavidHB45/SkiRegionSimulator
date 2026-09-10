using AlpineSim.Unity.UI;
using AlpineSim.Unity.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity
{
    public sealed partial class Bootstrap
    {
        public VehicleViewManager Vehicles { get; private set; }
        private TaskPanel _taskPanel;
        private bool _m2HudBound;

        partial void StartM2()
        {
            var go = new GameObject("Vehicles");
            go.transform.SetParent(WorldRoot, false);
            Vehicles = go.AddComponent<VehicleViewManager>();
            Vehicles.Construct(this);
            Cameras.BindVehicles(Vehicles);
            if (_taskPanel == null) _taskPanel = Ui.RegisterWindow(new TaskPanel(), 2, 900f, 520f);
            if (!_m2HudBound)
            {
                _m2HudBound = true;
                Ui.Hud.AddStatusProvider(() => Vehicles != null ? Vehicles.VehicleStatus() : "");
                Ui.Hud.AddLegend("Enter enter/leave machine  W/S A/D drive  Space brake  R/F blade  Q/E angle  T tiller  V implement  G work  L lights  I engine");
            }
        }

        partial void TeardownM2()
        {
            Cameras.BindVehicles(null);
            Vehicles = null;
        }
    }
}
