using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AlpineSim.Unity.UI
{
    /// <summary>
    /// Owns the canvas, the HUD and the registered panels. Panels are opened with F-keys; milestone
    /// files add theirs through RegisterPanel from their Bootstrap.StartMn hook.
    /// </summary>
    public sealed class UiRoot : MonoBehaviour
    {
        private Bootstrap _boot;
        public Canvas Canvas { get; private set; }
        public Hud Hud { get; private set; }
        private readonly List<UiPanel> _panels = new List<UiPanel>();
        private readonly Dictionary<int, UiPanel> _hotkeys = new Dictionary<int, UiPanel>();
        private readonly List<System.Action> _frameUpdaters = new List<System.Action>();

        public IReadOnlyList<UiPanel> Panels => _panels;

        public static UiRoot Create(Bootstrap boot)
        {
            var go = new GameObject("UI");
            go.transform.SetParent(boot.transform, false);
            var root = go.AddComponent<UiRoot>();
            root._boot = boot;
            root.Canvas = UiFactory.CreateCanvas("Canvas", boot.Data.Render.UiScale);
            root.Canvas.transform.SetParent(go.transform, false);
            UiFactory.EnsureEventSystem(go.transform);
            root.Hud = new Hud(boot, root.Canvas.transform);
            return root;
        }

        /// <summary>Registers a panel on hotkey slot 1..9 (F1..F4, F6..F8, F10, F11). Slot 0 = no hotkey.</summary>
        public T RegisterPanel<T>(T panel, int slot, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax) where T : UiPanel
        {
            panel.Build(_boot, Canvas.transform, anchorMin, anchorMax, offsetMin, offsetMax);
            _panels.Add(panel);
            if (slot > 0)
            {
                _hotkeys[slot] = panel;
                Hud.AddLegend(HotkeyName(slot) + " " + panel.Title);
            }
            return panel;
        }

        /// <summary>Standard centred window geometry.</summary>
        public T RegisterWindow<T>(T panel, int slot, float width, float height) where T : UiPanel
        {
            return RegisterPanel(panel, slot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-width / 2f, -height / 2f), new Vector2(width / 2f, height / 2f));
        }

        public void AddFrameUpdater(System.Action a) => _frameUpdaters.Add(a);

        public static string HotkeyName(int slot)
        {
            switch (slot)
            {
                case 1: return "F1"; case 2: return "F2"; case 3: return "F3"; case 4: return "F4";
                case 5: return "F6"; case 6: return "F7"; case 7: return "F8"; case 8: return "F10"; case 9: return "F11";
                default: return "";
            }
        }

        public void OnWorldBuilt()
        {
            Hud.OnWorldBuilt();
            foreach (var p in _panels) p.OnWorldRebuilt();
        }

        public void OnWorldTeardown()
        {
            Hud.OnWorldTeardown();
            foreach (var p in _panels) p.Hide();
        }

        public bool PointerOverUi => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        private void Update()
        {
            if (_boot == null || _boot.Sim == null) return;
            var g = _boot.Input.Game;
            TogglePanel(1, g.Panel1); TogglePanel(2, g.Panel2); TogglePanel(3, g.Panel3); TogglePanel(4, g.Panel4);
            TogglePanel(5, g.Panel5); TogglePanel(6, g.Panel6); TogglePanel(7, g.Panel7); TogglePanel(8, g.Panel8); TogglePanel(9, g.Panel9);
            if (g.Escape.WasPressedThisFrame()) foreach (var p in _panels) p.Hide();
            Hud.Update();
            for (int i = 0; i < _panels.Count; i++) if (_panels[i].Visible) _panels[i].Refresh();
            for (int i = 0; i < _frameUpdaters.Count; i++) _frameUpdaters[i]();
        }

        private void TogglePanel(int slot, UnityEngine.InputSystem.InputAction action)
        {
            if (action.WasPressedThisFrame() && _hotkeys.TryGetValue(slot, out var p)) p.Toggle();
        }
    }
}
