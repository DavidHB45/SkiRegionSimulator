using AlpineSim.Unity.Construction;
using AlpineSim.Unity.UI;
using UnityEngine;

namespace AlpineSim.Unity
{
    public sealed partial class Bootstrap
    {
        public PlacementController Placement { get; private set; }
        public ConstructionView ConstructionView { get; private set; }
        private ConstructionPanel _constructionPanel;

        partial void StartM4()
        {
            var go = new GameObject("Construction");
            go.transform.SetParent(WorldRoot, false);
            ConstructionView = go.AddComponent<ConstructionView>();
            ConstructionView.Construct(this);
            if (Placement == null)
            {
                var pgo = new GameObject("Placement");
                pgo.transform.SetParent(transform, false);
                Placement = pgo.AddComponent<PlacementController>();
                Placement.Construct(this);
            }
            else Placement.Cancel();
            if (_constructionPanel == null)
            {
                _constructionPanel = Ui.RegisterWindow(new ConstructionPanel(Placement), 9, 1100f, 620f);
                Ui.Hud.AddLegend("Staking: left click place  right click cancel  Backspace undo  Enter finish  scroll aim");
            }
        }

        partial void TeardownM4()
        {
            if (Placement != null) Placement.Cancel();
            ConstructionView = null;
        }
    }
}
