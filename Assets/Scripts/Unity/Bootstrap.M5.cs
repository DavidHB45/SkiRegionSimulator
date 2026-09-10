using AlpineSim.Unity.Snowmaking;
using AlpineSim.Unity.UI;
using UnityEngine;

namespace AlpineSim.Unity
{
    public sealed partial class Bootstrap
    {
        public SnowmakingView SnowmakingView { get; private set; }
        private WeatherPanel _weatherPanel;
        private SnowmakingPanel _snowmakingPanel;

        partial void StartM5()
        {
            var go = new GameObject("Snowmaking");
            go.transform.SetParent(WorldRoot, false);
            SnowmakingView = go.AddComponent<SnowmakingView>();
            SnowmakingView.Construct(this);
            if (_weatherPanel == null) _weatherPanel = Ui.RegisterWindow(new WeatherPanel(), 5, 1000f, 560f);
            if (_snowmakingPanel == null) _snowmakingPanel = Ui.RegisterWindow(new SnowmakingPanel(Placement), 6, 1100f, 600f);
        }

        partial void TeardownM5()
        {
            SnowmakingView = null;
        }
    }
}
