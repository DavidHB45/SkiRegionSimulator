using AlpineSim.Unity.Snow;
using UnityEngine;

namespace AlpineSim.Unity
{
    public sealed partial class Bootstrap
    {
        public SnowView SnowView { get; private set; }

        partial void StartM1()
        {
            var go = new GameObject("Snow");
            go.transform.SetParent(WorldRoot, false);
            SnowView = go.AddComponent<SnowView>();
            SnowView.Construct(this);
        }

        partial void TeardownM1()
        {
            SnowView = null;
        }
    }
}
