using AlpineSim.Unity.UI;

namespace AlpineSim.Unity
{
    public sealed partial class Bootstrap
    {
        private FleetPanel _fleetPanel;
        private StaffPanel _staffPanel;

        partial void StartM6()
        {
            if (_fleetPanel == null) _fleetPanel = Ui.RegisterWindow(new FleetPanel(), 7, 1150f, 640f);
            if (_staffPanel == null) _staffPanel = Ui.RegisterWindow(new StaffPanel(), 8, 1000f, 560f);
        }

        partial void TeardownM6() { }
    }
}
