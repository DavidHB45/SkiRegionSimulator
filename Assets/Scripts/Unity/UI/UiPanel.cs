using UnityEngine;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>
    /// A toggleable window with a title bar, a close button and a vertical body. Panels register
    /// with UiRoot under a hotkey slot (F1..F11) and refresh themselves every frame while visible.
    /// </summary>
    public abstract class UiPanel
    {
        public string Title { get; }
        public RectTransform Root { get; private set; }
        public RectTransform Body { get; private set; }
        public bool Visible => Root != null && Root.gameObject.activeSelf;
        protected Bootstrap Boot { get; private set; }

        protected UiPanel(string title) { Title = title; }

        public void Build(Bootstrap boot, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            Boot = boot;
            Root = UiFactory.Panel(Title, parent, anchorMin, anchorMax, offsetMin, offsetMax, UiFactory.PanelBgLight);
            var stack = UiFactory.VerticalStack(Root, 4f, 6, false);
            var titleRow = UiFactory.Row("TitleRow", Root, 26f);
            UiFactory.FlexLabel(titleRow, "<b>" + Title + "</b>", TextAnchor.MiddleLeft, 16, UiFactory.Accent);
            var close = UiFactory.Button("Close", titleRow, "X", Hide, 13);
            var le = close.GetComponent<LayoutElement>();
            le.preferredWidth = 28f;
            le.flexibleWidth = 0f;
            Body = UiFactory.CreateRect("Body", Root);
            var bodyLe = Body.gameObject.AddComponent<LayoutElement>();
            bodyLe.flexibleHeight = 1f;
            bodyLe.flexibleWidth = 1f;
            UiFactory.VerticalStack(Body, 4f, 2, false);
            BuildBody(Body);
            Root.gameObject.SetActive(false);
        }

        protected abstract void BuildBody(RectTransform body);

        /// <summary>Called every frame while visible.</summary>
        public virtual void Refresh() { }

        /// <summary>Called when the world is rebuilt (new game / load) so panels re-read references.</summary>
        public virtual void OnWorldRebuilt() { }

        public void Show()
        {
            if (Root == null) return;
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
            Refresh();
        }

        public void Hide() { if (Root != null) Root.gameObject.SetActive(false); }
        public void Toggle() { if (Visible) Hide(); else Show(); }

        /// <summary>Removes all children of a container so it can be rebuilt.</summary>
        protected static void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject);
        }
    }
}
