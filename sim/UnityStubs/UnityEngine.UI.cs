// Compile-only stubs. See UnityStubs.csproj.
using System;
using System.Collections.Generic;

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 arg0);
    public delegate void UnityAction<T0, T1>(T0 arg0, T1 arg1);
    public abstract class UnityEventBase { public void RemoveAllListeners() { } public int GetPersistentEventCount() => 0; }
    public class UnityEvent : UnityEventBase { public void AddListener(UnityAction a) { } public void RemoveListener(UnityAction a) { } public void Invoke() { } }
    public class UnityEvent<T0> : UnityEventBase { public void AddListener(UnityAction<T0> a) { } public void RemoveListener(UnityAction<T0> a) { } public void Invoke(T0 v) { } }
    public class UnityEvent<T0, T1> : UnityEventBase { public void AddListener(UnityAction<T0, T1> a) { } public void Invoke(T0 a, T1 b) { } }
}

namespace UnityEngine
{
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum HorizontalWrapMode { Wrap, Overflow }
    public enum VerticalWrapMode { Truncate, Overflow }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }

    public class RectOffset
    {
        public int left, right, top, bottom;
        public RectOffset() { }
        public RectOffset(int l, int r, int t, int b) { left = l; right = r; top = t; bottom = b; }
        public int horizontal => left + right;
        public int vertical => top + bottom;
    }

    public class RectTransform : Transform
    {
        public enum Edge { Left, Right, Top, Bottom }
        public enum Axis { Horizontal, Vertical }
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector3 anchoredPosition3D { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Rect rect => new Rect();
        public void SetInsetAndSizeFromParentEdge(Edge e, float inset, float size) { }
        public void SetSizeWithCurrentAnchors(Axis a, float size) { }
        public void GetWorldCorners(Vector3[] c) { }
        public void GetLocalCorners(Vector3[] c) { }
        public void ForceUpdateRectTransforms() { }
    }

    public class Canvas : Behaviour
    {
        public RenderMode renderMode { get; set; }
        public int sortingOrder { get; set; }
        public string sortingLayerName { get; set; }
        public Camera worldCamera { get; set; }
        public bool pixelPerfect { get; set; }
        public float planeDistance { get; set; }
        public float scaleFactor { get; set; }
        public bool overrideSorting { get; set; }
        public bool isRootCanvas => true;
        public Canvas rootCanvas => this;
        public float referencePixelsPerUnit { get; set; }
        public int additionalShaderChannels { get; set; }
        public static void ForceUpdateCanvases() { }
    }

    public class CanvasGroup : Component
    {
        public float alpha { get; set; }
        public bool interactable { get; set; }
        public bool blocksRaycasts { get; set; }
        public bool ignoreParentGroups { get; set; }
    }

    public class CanvasRenderer : Component { public void SetAlpha(float a) { } public void SetColor(Color c) { } public Color GetColor() => Color.white; public bool cull { get; set; } }

    public class Sprite : Object
    {
        public static Sprite Create(Texture2D t, Rect r, Vector2 pivot) => null;
        public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu) => null;
        public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu, uint extrude, SpriteMeshType mt, Vector4 border) => null;
        public Texture2D texture => null;
        public Rect rect => new Rect();
    }
    public enum SpriteMeshType { FullRect, Tight }
}

namespace UnityEngine.EventSystems
{
    public class BaseEventData { }
    public class PointerEventData : BaseEventData
    {
        public enum InputButton { Left, Right, Middle }
        public Vector2 position { get; set; }
        public Vector2 delta { get; set; }
        public Vector2 scrollDelta { get; set; }
        public InputButton button { get; set; }
        public int clickCount { get; set; }
        public GameObject pointerEnter { get; set; }
        public GameObject pointerPress { get; set; }
        public Vector2 pressPosition { get; set; }
        public Camera pressEventCamera => null;
        public Camera enterEventCamera => null;
        public bool dragging { get; set; }
    }
    public class AxisEventData : BaseEventData { }
    public interface IEventSystemHandler { }
    public interface IPointerClickHandler : IEventSystemHandler { void OnPointerClick(PointerEventData e); }
    public interface IPointerDownHandler : IEventSystemHandler { void OnPointerDown(PointerEventData e); }
    public interface IPointerUpHandler : IEventSystemHandler { void OnPointerUp(PointerEventData e); }
    public interface IPointerEnterHandler : IEventSystemHandler { void OnPointerEnter(PointerEventData e); }
    public interface IPointerExitHandler : IEventSystemHandler { void OnPointerExit(PointerEventData e); }
    public interface IDragHandler : IEventSystemHandler { void OnDrag(PointerEventData e); }
    public interface IBeginDragHandler : IEventSystemHandler { void OnBeginDrag(PointerEventData e); }
    public interface IEndDragHandler : IEventSystemHandler { void OnEndDrag(PointerEventData e); }
    public interface IScrollHandler : IEventSystemHandler { void OnScroll(PointerEventData e); }
    public interface ISelectHandler : IEventSystemHandler { void OnSelect(BaseEventData e); }
    public interface IDeselectHandler : IEventSystemHandler { void OnDeselect(BaseEventData e); }
    public interface ISubmitHandler : IEventSystemHandler { void OnSubmit(BaseEventData e); }

    public class UIBehaviour : MonoBehaviour { }
    public class BaseInputModule : UIBehaviour { }
    public class StandaloneInputModule : BaseInputModule { }
    public class EventSystem : UIBehaviour
    {
        public static EventSystem current { get; set; }
        public GameObject currentSelectedGameObject => null;
        public bool IsPointerOverGameObject() => false;
        public bool IsPointerOverGameObject(int pointerId) => false;
        public void SetSelectedGameObject(GameObject go) { }
        public bool sendNavigationEvents { get; set; }
        public int pixelDragThreshold { get; set; }
    }
    public class EventTrigger : MonoBehaviour { }
    public static class ExecuteEvents { }
}

namespace UnityEngine.UI
{
    using UnityEngine.EventSystems;
    using UnityEngine.Events;

    public interface ILayoutElement { }
    public interface ICanvasRaycastFilter { }
    public interface IMaterialModifier { }

    public class CanvasScaler : UIBehaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public ScreenMatchMode screenMatchMode { get; set; }
        public float matchWidthOrHeight { get; set; }
        public float scaleFactor { get; set; }
        public float referencePixelsPerUnit { get; set; }
        public float dynamicPixelsPerUnit { get; set; }
    }

    public class GraphicRaycaster : UIBehaviour { public bool ignoreReversedGraphics { get; set; } public LayerMask blockingMask { get; set; } }

    public class Graphic : UIBehaviour
    {
        public Color color { get; set; }
        public bool raycastTarget { get; set; }
        public Material material { get; set; }
        public Material defaultMaterial => null;
        public RectTransform rectTransform => null;
        public CanvasRenderer canvasRenderer => null;
        public Canvas canvas => null;
        public Vector4 raycastPadding { get; set; }
        public void SetVerticesDirty() { }
        public void SetLayoutDirty() { }
        public void SetMaterialDirty() { }
        public void SetAllDirty() { }
        public void CrossFadeColor(Color c, float d, bool ignoreTimeScale, bool useAlpha) { }
        public void CrossFadeAlpha(float a, float d, bool ignoreTimeScale) { }
        public Vector2 GetPixelAdjustedPoint(Vector2 p) => p;
        public static Material defaultGraphicMaterial => null;
    }
    public class MaskableGraphic : Graphic { public bool maskable { get; set; } }

    public class Text : MaskableGraphic, ILayoutElement
    {
        public string text { get; set; }
        public Font font { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public bool alignByGeometry { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
        public bool supportRichText { get; set; }
        public float lineSpacing { get; set; }
        public bool resizeTextForBestFit { get; set; }
        public int resizeTextMinSize { get; set; }
        public int resizeTextMaxSize { get; set; }
        public float preferredWidth => 0f;
        public float preferredHeight => 0f;
        public float minWidth => 0f;
        public float minHeight => 0f;
        public float flexibleWidth => 0f;
        public float flexibleHeight => 0f;
        public int layoutPriority => 0;
        public int cachedTextGenerator_dummy => 0;
    }

    public class Image : MaskableGraphic, ILayoutElement
    {
        public enum Type { Simple, Sliced, Tiled, Filled }
        public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
        public enum OriginHorizontal { Left, Right }
        public enum OriginVertical { Bottom, Top }
        public Sprite sprite { get; set; }
        public Sprite overrideSprite { get; set; }
        public Type type { get; set; }
        public bool preserveAspect { get; set; }
        public bool fillCenter { get; set; }
        public FillMethod fillMethod { get; set; }
        public float fillAmount { get; set; }
        public bool fillClockwise { get; set; }
        public int fillOrigin { get; set; }
        public float pixelsPerUnitMultiplier { get; set; }
        public bool useSpriteMesh { get; set; }
        public float preferredWidth => 0f;
        public float preferredHeight => 0f;
        public float minWidth => 0f;
        public float minHeight => 0f;
        public float flexibleWidth => 0f;
        public float flexibleHeight => 0f;
        public int layoutPriority => 0;
        public void SetNativeSize() { }
    }

    public class RawImage : MaskableGraphic
    {
        public Texture texture { get; set; }
        public Texture mainTexture => texture;
        public Rect uvRect { get; set; }
        public void SetNativeSize() { }
    }

    public struct ColorBlock
    {
        public Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor;
        public float colorMultiplier, fadeDuration;
        public static ColorBlock defaultColorBlock => new ColorBlock { normalColor = Color.white, highlightedColor = Color.white, pressedColor = Color.gray, selectedColor = Color.white, disabledColor = Color.gray, colorMultiplier = 1f, fadeDuration = 0.1f };
    }

    public struct Navigation
    {
        public enum Mode { None = 0, Horizontal = 1, Vertical = 2, Automatic = 3, Explicit = 4 }
        public Mode mode;
        public static Navigation defaultNavigation => new Navigation { mode = Mode.Automatic };
    }

    public class Selectable : UIBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        public enum Transition { None, ColorTint, SpriteSwap, Animation }
        public bool interactable { get; set; }
        public ColorBlock colors { get; set; }
        public Graphic targetGraphic { get; set; }
        public Image image { get; set; }
        public Transition transition { get; set; }
        public Navigation navigation { get; set; }
        public bool IsInteractable() => interactable;
        public void Select() { }
        public virtual void OnPointerEnter(PointerEventData e) { }
        public virtual void OnPointerExit(PointerEventData e) { }
        public virtual void OnPointerDown(PointerEventData e) { }
        public virtual void OnPointerUp(PointerEventData e) { }
        public virtual void OnSelect(BaseEventData e) { }
        public virtual void OnDeselect(BaseEventData e) { }
    }

    public class Button : Selectable, IPointerClickHandler, ISubmitHandler
    {
        public class ButtonClickedEvent : UnityEvent { }
        public ButtonClickedEvent onClick { get; set; } = new ButtonClickedEvent();
        public virtual void OnPointerClick(PointerEventData e) { }
        public virtual void OnSubmit(BaseEventData e) { }
    }

    public class Toggle : Selectable, IPointerClickHandler, ISubmitHandler
    {
        public class ToggleEvent : UnityEvent<bool> { }
        public enum ToggleTransition { None, Fade }
        public ToggleEvent onValueChanged { get; set; } = new ToggleEvent();
        public bool isOn { get; set; }
        public Graphic graphic { get; set; }
        public ToggleTransition toggleTransition { get; set; }
        public ToggleGroup group { get; set; }
        public void SetIsOnWithoutNotify(bool v) { }
        public virtual void OnPointerClick(PointerEventData e) { }
        public virtual void OnSubmit(BaseEventData e) { }
    }
    public class ToggleGroup : UIBehaviour { public bool allowSwitchOff { get; set; } }

    public class Slider : Selectable, IDragHandler
    {
        public class SliderEvent : UnityEvent<float> { }
        public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
        public SliderEvent onValueChanged { get; set; } = new SliderEvent();
        public float value { get; set; }
        public float normalizedValue { get; set; }
        public float minValue { get; set; }
        public float maxValue { get; set; }
        public bool wholeNumbers { get; set; }
        public RectTransform fillRect { get; set; }
        public RectTransform handleRect { get; set; }
        public Direction direction { get; set; }
        public void SetValueWithoutNotify(float v) { }
        public virtual void OnDrag(PointerEventData e) { }
    }

    public class Scrollbar : Selectable
    {
        public class ScrollEvent : UnityEvent<float> { }
        public enum Direction { LeftToRight, RightToLeft, BottomToTop, TopToBottom }
        public ScrollEvent onValueChanged { get; set; } = new ScrollEvent();
        public float value { get; set; }
        public float size { get; set; }
        public RectTransform handleRect { get; set; }
        public Direction direction { get; set; }
    }

    public class InputField : Selectable
    {
        public class SubmitEvent : UnityEvent<string> { }
        public class OnChangeEvent : UnityEvent<string> { }
        public enum ContentType { Standard, Autocorrected, IntegerNumber, DecimalNumber, Alphanumeric, Name, EmailAddress, Password, Pin, Custom }
        public enum LineType { SingleLine, MultiLineSubmit, MultiLineNewline }
        public string text { get; set; }
        public Text textComponent { get; set; }
        public Graphic placeholder { get; set; }
        public ContentType contentType { get; set; }
        public LineType lineType { get; set; }
        public int characterLimit { get; set; }
        public bool readOnly { get; set; }
        public bool isFocused => false;
        public float caretWidth { get; set; }
        public Color selectionColor { get; set; }
        public SubmitEvent onEndEdit { get; set; } = new SubmitEvent();
        public SubmitEvent onSubmit { get; set; } = new SubmitEvent();
        public OnChangeEvent onValueChanged { get; set; } = new OnChangeEvent();
        public void ActivateInputField() { }
        public void DeactivateInputField() { }
        public void SetTextWithoutNotify(string s) { }
    }

    public class ScrollRect : UIBehaviour, IScrollHandler, IDragHandler, IBeginDragHandler, IEndDragHandler
    {
        public enum MovementType { Unrestricted, Elastic, Clamped }
        public enum ScrollbarVisibility { Permanent, AutoHide, AutoHideAndExpandViewport }
        public class ScrollRectEvent : UnityEvent<Vector2> { }
        public RectTransform content { get; set; }
        public RectTransform viewport { get; set; }
        public bool horizontal { get; set; }
        public bool vertical { get; set; }
        public MovementType movementType { get; set; }
        public float elasticity { get; set; }
        public bool inertia { get; set; }
        public float decelerationRate { get; set; }
        public float scrollSensitivity { get; set; }
        public Scrollbar horizontalScrollbar { get; set; }
        public Scrollbar verticalScrollbar { get; set; }
        public ScrollbarVisibility verticalScrollbarVisibility { get; set; }
        public float verticalNormalizedPosition { get; set; }
        public float horizontalNormalizedPosition { get; set; }
        public Vector2 normalizedPosition { get; set; }
        public Vector2 velocity { get; set; }
        public ScrollRectEvent onValueChanged { get; set; } = new ScrollRectEvent();
        public void StopMovement() { }
        public virtual void OnScroll(PointerEventData e) { }
        public virtual void OnDrag(PointerEventData e) { }
        public virtual void OnBeginDrag(PointerEventData e) { }
        public virtual void OnEndDrag(PointerEventData e) { }
    }

    public class Mask : UIBehaviour, ICanvasRaycastFilter, IMaterialModifier { public bool showMaskGraphic { get; set; } }
    public class RectMask2D : UIBehaviour { public Vector4 padding { get; set; } public Vector2Int softness { get; set; } }

    public class LayoutElement : UIBehaviour, ILayoutElement
    {
        public bool ignoreLayout { get; set; }
        public float minWidth { get; set; }
        public float minHeight { get; set; }
        public float preferredWidth { get; set; }
        public float preferredHeight { get; set; }
        public float flexibleWidth { get; set; }
        public float flexibleHeight { get; set; }
        public int layoutPriority { get; set; }
    }

    public abstract class LayoutGroup : UIBehaviour
    {
        public RectOffset padding { get; set; }
        public TextAnchor childAlignment { get; set; }
        public float minWidth => 0f;
        public float preferredWidth => 0f;
        public float preferredHeight => 0f;
    }
    public abstract class HorizontalOrVerticalLayoutGroup : LayoutGroup
    {
        public float spacing { get; set; }
        public bool childForceExpandWidth { get; set; }
        public bool childForceExpandHeight { get; set; }
        public bool childControlWidth { get; set; }
        public bool childControlHeight { get; set; }
        public bool childScaleWidth { get; set; }
        public bool childScaleHeight { get; set; }
        public bool reverseArrangement { get; set; }
    }
    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class GridLayoutGroup : LayoutGroup
    {
        public enum Corner { UpperLeft, UpperRight, LowerLeft, LowerRight }
        public enum Axis { Horizontal, Vertical }
        public enum Constraint { Flexible, FixedColumnCount, FixedRowCount }
        public Vector2 cellSize { get; set; }
        public Vector2 spacing { get; set; }
        public Corner startCorner { get; set; }
        public Axis startAxis { get; set; }
        public Constraint constraint { get; set; }
        public int constraintCount { get; set; }
    }

    public class ContentSizeFitter : UIBehaviour
    {
        public enum FitMode { Unconstrained, MinSize, PreferredSize }
        public FitMode horizontalFit { get; set; }
        public FitMode verticalFit { get; set; }
    }

    public class AspectRatioFitter : UIBehaviour { public float aspectRatio { get; set; } }

    public static class LayoutRebuilder
    {
        public static void ForceRebuildLayoutImmediate(RectTransform rt) { }
        public static void MarkLayoutForRebuild(RectTransform rt) { }
    }
    public static class LayoutUtility
    {
        public static float GetPreferredHeight(RectTransform rt) => 0f;
        public static float GetPreferredWidth(RectTransform rt) => 0f;
    }

    public class Shadow : UIBehaviour, IMaterialModifier { public Color effectColor { get; set; } public Vector2 effectDistance { get; set; } public bool useGraphicAlpha { get; set; } }
    public class Outline : Shadow { }

    public class Dropdown : Selectable
    {
        public class OptionData { public string text; public OptionData() { } public OptionData(string t) { text = t; } }
        public class DropdownEvent : UnityEvent<int> { }
        public List<OptionData> options { get; set; } = new List<OptionData>();
        public int value { get; set; }
        public Text captionText { get; set; }
        public Text itemText { get; set; }
        public RectTransform template { get; set; }
        public DropdownEvent onValueChanged { get; set; } = new DropdownEvent();
        public void ClearOptions() { }
        public void AddOptions(List<string> o) { }
        public void RefreshShownValue() { }
        public void SetValueWithoutNotify(int v) { }
    }
}

namespace UnityEngine
{
    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rt, Vector2 screen, Camera cam, out Vector2 local) { local = Vector2.zero; return true; }
        public static bool ScreenPointToWorldPointInRectangle(RectTransform rt, Vector2 screen, Camera cam, out Vector3 world) { world = Vector3.zero; return true; }
        public static bool RectangleContainsScreenPoint(RectTransform rt, Vector2 screen) => false;
        public static bool RectangleContainsScreenPoint(RectTransform rt, Vector2 screen, Camera cam) => false;
        public static Vector2 WorldToScreenPoint(Camera cam, Vector3 world) => Vector2.zero;
    }
}
