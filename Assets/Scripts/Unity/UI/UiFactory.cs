using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>
    /// Builders for every uGUI element the game uses. Everything is created in code; no prefabs,
    /// no TextMeshPro, no UI Toolkit. Font is the built-in LegacyRuntime.ttf.
    /// </summary>
    public static class UiFactory
    {
        private static Font _font;
        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static readonly Color PanelBg = new Color(0.07f, 0.09f, 0.12f, 0.88f);
        public static readonly Color PanelBgLight = new Color(0.14f, 0.17f, 0.22f, 0.95f);
        public static readonly Color RowBg = new Color(1f, 1f, 1f, 0.05f);
        public static readonly Color RowBgAlt = new Color(1f, 1f, 1f, 0.09f);
        public static readonly Color TextColor = new Color(0.92f, 0.94f, 0.96f);
        public static readonly Color TextDim = new Color(0.65f, 0.7f, 0.76f);
        public static readonly Color Accent = new Color(0.36f, 0.72f, 1f);
        public static readonly Color Good = new Color(0.45f, 0.85f, 0.45f);
        public static readonly Color Warn = new Color(1f, 0.8f, 0.3f);
        public static readonly Color Bad = new Color(1f, 0.4f, 0.35f);
        public static readonly Color ButtonBg = new Color(0.2f, 0.28f, 0.38f, 1f);

        public static Canvas CreateCanvas(string name, float uiScale)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f / uiScale, 900f / uiScale);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static EventSystem EnsureEventSystem(Transform parent)
        {
            if (EventSystem.current != null) return EventSystem.current;
            var go = new GameObject("EventSystem");
            go.transform.SetParent(parent, false);
            var es = go.AddComponent<EventSystem>();
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
            return es;
        }

        public static RectTransform Rect(GameObject go) => go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchored panel. anchor/size in reference pixels; anchors given as (0..1) corners.</summary>
        public static RectTransform Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color? bg = null)
        {
            var rt = CreateRect(name, parent);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = bg ?? PanelBg;
            img.raycastTarget = true;
            return rt;
        }

        public static RectTransform Fill(RectTransform rt, float pad = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
            return rt;
        }

        public static Text Label(string name, Transform parent, string text, int size = 14, TextAnchor align = TextAnchor.UpperLeft, Color? color = null)
        {
            var rt = CreateRect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.fontSize = size;
            t.text = text;
            t.color = color ?? TextColor;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>A label stretched to fill its parent with padding.</summary>
        public static Text FillLabel(string name, Transform parent, string text, int size = 14, TextAnchor align = TextAnchor.UpperLeft, float pad = 6f, Color? color = null)
        {
            var t = Label(name, parent, text, size, align, color);
            Fill((RectTransform)t.transform, pad);
            return t;
        }

        public static Button Button(string name, Transform parent, string caption, UnityEngine.Events.UnityAction onClick, int fontSize = 14, Color? bg = null)
        {
            var rt = CreateRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = bg ?? ButtonBg;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(onClick);
            var label = FillLabel("Label", rt, caption, fontSize, TextAnchor.MiddleCenter, 4f);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 26f;
            le.preferredHeight = 26f;
            return btn;
        }

        public static Toggle Toggle(string name, Transform parent, string caption, bool initial, UnityEngine.Events.UnityAction<bool> onChanged, int fontSize = 14)
        {
            var rt = CreateRect(name, parent);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 22f;
            le.preferredHeight = 22f;
            var toggle = rt.gameObject.AddComponent<Toggle>();
            var box = CreateRect("Box", rt);
            box.anchorMin = new Vector2(0f, 0.5f);
            box.anchorMax = new Vector2(0f, 0.5f);
            box.pivot = new Vector2(0f, 0.5f);
            box.anchoredPosition = new Vector2(4f, 0f);
            box.sizeDelta = new Vector2(16f, 16f);
            var boxImg = box.gameObject.AddComponent<Image>();
            boxImg.color = ButtonBg;
            var check = CreateRect("Check", box);
            Fill(check, 3f);
            var checkImg = check.gameObject.AddComponent<Image>();
            checkImg.color = Accent;
            toggle.targetGraphic = boxImg;
            toggle.graphic = checkImg;
            toggle.isOn = initial;
            if (onChanged != null) toggle.onValueChanged.AddListener(onChanged);
            var label = Label("Label", rt, caption, fontSize, TextAnchor.MiddleLeft);
            var lrt = (RectTransform)label.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(26f, 0f);
            lrt.offsetMax = Vector2.zero;
            return toggle;
        }

        public static Slider Slider(string name, Transform parent, float min, float max, float value, bool whole, UnityEngine.Events.UnityAction<float> onChanged)
        {
            var rt = CreateRect(name, parent);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 18f;
            le.preferredHeight = 18f;
            var bg = CreateRect("Background", rt);
            bg.anchorMin = new Vector2(0f, 0.35f);
            bg.anchorMax = new Vector2(1f, 0.65f);
            bg.offsetMin = Vector2.zero; bg.offsetMax = Vector2.zero;
            bg.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
            var fillArea = CreateRect("Fill Area", rt);
            fillArea.anchorMin = new Vector2(0f, 0.35f);
            fillArea.anchorMax = new Vector2(1f, 0.65f);
            fillArea.offsetMin = Vector2.zero; fillArea.offsetMax = Vector2.zero;
            var fill = CreateRect("Fill", fillArea);
            fill.gameObject.AddComponent<Image>().color = Accent;
            var handleArea = CreateRect("Handle Slide Area", rt);
            Fill(handleArea);
            var handle = CreateRect("Handle", handleArea);
            handle.sizeDelta = new Vector2(12f, 0f);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.color = TextColor;
            var slider = rt.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = whole;
            slider.value = value;
            if (onChanged != null) slider.onValueChanged.AddListener(onChanged);
            return slider;
        }

        public static InputField InputField(string name, Transform parent, string initial, UnityEngine.Events.UnityAction<string> onEndEdit, int fontSize = 14)
        {
            var rt = CreateRect(name, parent);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = 24f;
            le.preferredHeight = 24f;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.4f);
            var field = rt.gameObject.AddComponent<InputField>();
            var text = FillLabel("Text", rt, "", fontSize, TextAnchor.MiddleLeft, 4f);
            text.supportRichText = false;
            var placeholder = FillLabel("Placeholder", rt, "...", fontSize, TextAnchor.MiddleLeft, 4f, TextDim);
            field.textComponent = text;
            field.placeholder = placeholder;
            field.text = initial;
            field.targetGraphic = img;
            if (onEndEdit != null) field.onEndEdit.AddListener(onEndEdit);
            return field;
        }

        public static VerticalLayoutGroup VerticalStack(RectTransform rt, float spacing = 4f, int pad = 6, bool expandHeight = false)
        {
            var v = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = expandHeight;
            v.childAlignment = TextAnchor.UpperLeft;
            return v;
        }

        public static HorizontalLayoutGroup HorizontalRow(RectTransform rt, float spacing = 4f, int pad = 0, bool expandWidth = true)
        {
            var h = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = expandWidth;
            h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            return h;
        }

        public static RectTransform Row(string name, Transform parent, float height = 24f, float spacing = 4f)
        {
            var rt = CreateRect(name, parent);
            HorizontalRow(rt, spacing);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return rt;
        }

        /// <summary>Vertical scroll view; returns the content transform to fill.</summary>
        public static RectTransform ScrollView(string name, Transform parent, out ScrollRect scroll)
        {
            var rt = CreateRect(name, parent);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.flexibleWidth = 1f;
            le.minHeight = 60f;
            scroll = rt.gameObject.AddComponent<ScrollRect>();
            var viewport = CreateRect("Viewport", rt);
            Fill(viewport);
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.15f);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            VerticalStack(content, 2f, 4);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return content;
        }

        public static Text RowLabel(Transform row, string text, float width, TextAnchor align = TextAnchor.MiddleLeft, int fontSize = 13, Color? color = null)
        {
            var t = Label("Cell", row, text, fontSize, align, color);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.minWidth = width * 0.5f;
            le.flexibleWidth = 0f;
            return t;
        }

        public static Text FlexLabel(Transform row, string text, TextAnchor align = TextAnchor.MiddleLeft, int fontSize = 13, Color? color = null)
        {
            var t = Label("Cell", row, text, fontSize, align, color);
            var le = t.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            return t;
        }

        public static Image Spacer(Transform parent, float height)
        {
            var rt = CreateRect("Spacer", parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.12f);
            img.raycastTarget = false;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
            return img;
        }

        public static string Money(double v)
        {
            string sign = v < 0 ? "-" : "";
            v = System.Math.Abs(v);
            if (v >= 1e6) return sign + "$" + (v / 1e6).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "M";
            if (v >= 1e4) return sign + "$" + (v / 1e3).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "k";
            return sign + "$" + v.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string F(float v, int decimals = 1) => v.ToString(decimals == 0 ? "0" : ("0." + new string('0', decimals)), System.Globalization.CultureInfo.InvariantCulture);

        public static string ColorTag(Color c, string text) => "<color=#" + ColorUtility.ToHtmlStringRGBA(c) + ">" + text + "</color>";
    }
}
