using AlpineSim.Unity.Snow;
using UnityEngine;

namespace AlpineSim.Unity
{
    public sealed partial class Bootstrap
    {
        public SnowView SnowView { get; private set; }
        private bool _m1HudBound;

        partial void StartM1()
        {
            var go = new GameObject("Snow");
            go.transform.SetParent(WorldRoot, false);
            SnowView = go.AddComponent<SnowView>();
            SnowView.Construct(this);
            if (!_m1HudBound)
            {
                _m1HudBound = true;
                Ui.Hud.AddStatusProvider(() => SnowView != null ? SnowView.Status() : "");
                Ui.Hud.AddLegend("O snow overlay (depth / density / roughness / PQI / groom age / surface)");
            }
        }

        partial void TeardownM1()
        {
            SnowView = null;
        }
    }
}
