// Compile-only stubs for com.unity.inputsystem 1.11. See UnityStubs.csproj.
using System;
using System.Collections.Generic;

namespace UnityEngine.InputSystem
{
    public enum InputActionType { Value, Button, PassThrough }
    public enum InputActionPhase { Disabled, Waiting, Started, Performed, Canceled }

    public class InputActionAsset : ScriptableObject
    {
        public InputActionMap FindActionMap(string name) => null;
        public InputActionMap FindActionMap(string name, bool throwIfNotFound) => null;
        public InputAction FindAction(string name) => null;
        public InputAction FindAction(string name, bool throwIfNotFound) => null;
        public void Enable() { }
        public void Disable() { }
        public IReadOnlyList<InputActionMap> actionMaps => new List<InputActionMap>();
    }

    public class InputActionMap : IDisposable
    {
        public InputActionMap() { }
        public InputActionMap(string name) { this.name = name; }
        public string name { get; }
        public bool enabled { get; private set; }
        public IReadOnlyList<InputAction> actions => new List<InputAction>();
        public InputAction FindAction(string name) => null;
        public InputAction FindAction(string name, bool throwIfNotFound) => null;
        public void Enable() { enabled = true; }
        public void Disable() { enabled = false; }
        public void Dispose() { }
        public event Action<InputAction.CallbackContext> actionTriggered;
    }

    public class InputAction : IDisposable
    {
        public struct CallbackContext
        {
            public InputAction action => null;
            public InputActionPhase phase => InputActionPhase.Performed;
            public bool performed => true;
            public bool started => false;
            public bool canceled => false;
            public double time => 0;
            public double startTime => 0;
            public double duration => 0;
            public float GetTimestamp() => 0f;
            public TValue ReadValue<TValue>() where TValue : struct => default(TValue);
            public bool ReadValueAsButton() => false;
            public object ReadValueAsObject() => null;
            public UnityEngine.InputSystem.Controls.InputControl control => null;
        }
        public InputAction() { }
        public InputAction(string name = null, InputActionType type = InputActionType.Value, string binding = null, string interactions = null, string processors = null, string expectedControlType = null) { this.name = name; this.type = type; }
        public string name { get; }
        public InputActionType type { get; }
        public bool enabled { get; private set; }
        public bool triggered => false;
        public InputActionPhase phase => InputActionPhase.Waiting;
        public InputActionMap actionMap => null;
        public UnityEngine.InputSystem.Controls.InputControl activeControl => null;
        public event Action<CallbackContext> started;
        public event Action<CallbackContext> performed;
        public event Action<CallbackContext> canceled;
        public void Enable() { enabled = true; }
        public void Disable() { enabled = false; }
        public void Dispose() { }
        public TValue ReadValue<TValue>() where TValue : struct => default(TValue);
        public object ReadValueAsObject() => null;
        public bool IsPressed() => false;
        public bool IsInProgress() => false;
        public bool WasPressedThisFrame() => false;
        public bool WasReleasedThisFrame() => false;
        public bool WasPerformedThisFrame() => false;
        public bool WasCompletedThisFrame() => false;
        public InputAction Clone() => this;
        public string GetBindingDisplayString() => "";
        public string GetBindingDisplayString(int bindingIndex) => "";
        public IReadOnlyList<InputBinding> bindings => new List<InputBinding>();
        public IReadOnlyList<UnityEngine.InputSystem.Controls.InputControl> controls => new List<UnityEngine.InputSystem.Controls.InputControl>();
    }

    public struct InputBinding
    {
        public string path, interactions, processors, groups, name, action;
        public bool isComposite, isPartOfComposite;
    }

    public static class InputActionSetupExtensions
    {
        public struct BindingSyntax
        {
            public BindingSyntax WithProcessor(string processor) => this;
            public BindingSyntax WithProcessors(string processors) => this;
            public BindingSyntax WithInteraction(string interaction) => this;
            public BindingSyntax WithInteractions(string interactions) => this;
            public BindingSyntax WithGroup(string group) => this;
            public BindingSyntax WithGroups(string groups) => this;
            public BindingSyntax WithName(string name) => this;
            public BindingSyntax WithPath(string path) => this;
            public BindingSyntax Triggering(InputAction action) => this;
            public int bindingIndex => 0;
        }
        public struct CompositeSyntax
        {
            public CompositeSyntax With(string name, string binding, string groups = null, string processors = null) => this;
        }
        public static InputActionMap AddActionMap(this InputActionAsset asset, string name) => new InputActionMap(name);
        public static InputAction AddAction(this InputActionMap map, string name, InputActionType type = InputActionType.Value, string binding = null, string interactions = null, string processors = null, string groups = null, string expectedControlLayout = null) => new InputAction(name, type, binding, interactions, processors);
        public static BindingSyntax AddBinding(this InputAction action, string path, string interactions = null, string processors = null, string groups = null) => new BindingSyntax();
        public static BindingSyntax AddBinding(this InputActionMap map, string path, string interactions = null, string groups = null, string action = null) => new BindingSyntax();
        public static CompositeSyntax AddCompositeBinding(this InputAction action, string composite, string interactions = null, string processors = null) => new CompositeSyntax();
        public static void RemoveAction(this InputAction action) { }
        public static BindingSyntax ChangeBinding(this InputAction action, int index) => new BindingSyntax();
    }

    public static class InputSystem
    {
        public static InputSettings settings { get; set; }
        public static IReadOnlyList<InputDevice> devices => new List<InputDevice>();
        public static event Action<InputDevice, InputDeviceChange> onDeviceChange;
        public static void Update() { }
        public static void EnableDevice(InputDevice d) { }
        public static void DisableDevice(InputDevice d) { }
        public static InputDevice GetDevice(string layout) => null;
        public static TDevice GetDevice<TDevice>() where TDevice : InputDevice => null;
    }
    public enum InputDeviceChange { Added, Removed, Disconnected, Reconnected, Enabled, Disabled }
    public class InputSettings : ScriptableObject { public UpdateMode updateMode { get; set; } public enum UpdateMode { ProcessEventsInDynamicUpdate = 1, ProcessEventsInFixedUpdate, ProcessEventsManually } }

    public class InputDevice : UnityEngine.InputSystem.Controls.InputControl
    {
        public int deviceId => 0;
        public bool added => true;
        public bool enabled => true;
        public string description_dummy => "";
        public double lastUpdateTime => 0;
    }

    public class Mouse : Pointer
    {
        public static Mouse current => null;
        public UnityEngine.InputSystem.Controls.ButtonControl leftButton => null;
        public UnityEngine.InputSystem.Controls.ButtonControl rightButton => null;
        public UnityEngine.InputSystem.Controls.ButtonControl middleButton => null;
        public UnityEngine.InputSystem.Controls.ButtonControl forwardButton => null;
        public UnityEngine.InputSystem.Controls.ButtonControl backButton => null;
        public UnityEngine.InputSystem.Controls.Vector2Control scroll => null;
        public UnityEngine.InputSystem.Controls.IntegerControl clickCount => null;
        public void WarpCursorPosition(Vector2 p) { }
    }

    public class Pointer : InputDevice
    {
        public UnityEngine.InputSystem.Controls.Vector2Control position => null;
        public UnityEngine.InputSystem.Controls.Vector2Control delta => null;
        public UnityEngine.InputSystem.Controls.ButtonControl press => null;
        public UnityEngine.InputSystem.Controls.AxisControl pressure => null;
        public static Pointer current => null;
    }

    public class Keyboard : InputDevice
    {
        public static Keyboard current => null;
        public UnityEngine.InputSystem.Controls.AnyKeyControl anyKey => null;
        public UnityEngine.InputSystem.Controls.KeyControl this[Key key] => null;
        public UnityEngine.InputSystem.Controls.KeyControl escapeKey => null;
        public UnityEngine.InputSystem.Controls.KeyControl enterKey => null;
        public UnityEngine.InputSystem.Controls.KeyControl spaceKey => null;
        public UnityEngine.InputSystem.Controls.KeyControl leftShiftKey => null;
        public UnityEngine.InputSystem.Controls.KeyControl leftCtrlKey => null;
        public UnityEngine.InputSystem.Controls.KeyControl tabKey => null;
        public UnityEngine.InputSystem.Controls.KeyControl backspaceKey => null;
        public UnityEngine.InputSystem.Controls.KeyControl deleteKey => null;
        public event Action<char> onTextInput;
    }
    public enum Key { None, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash, LeftBracket, RightBracket, Minus, Equals, A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z, Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0, LeftShift, RightShift, LeftAlt, RightAlt, LeftCtrl, RightCtrl, Escape, LeftArrow, RightArrow, UpArrow, DownArrow, Backspace, PageDown, PageUp, Home, End, Insert, Delete, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12 }

    public class Gamepad : InputDevice
    {
        public static Gamepad current => null;
        public UnityEngine.InputSystem.Controls.StickControl leftStick => null;
        public UnityEngine.InputSystem.Controls.StickControl rightStick => null;
        public UnityEngine.InputSystem.Controls.ButtonControl buttonSouth => null;
        public UnityEngine.InputSystem.Controls.ButtonControl buttonEast => null;
        public UnityEngine.InputSystem.Controls.ButtonControl leftTrigger => null;
        public UnityEngine.InputSystem.Controls.ButtonControl rightTrigger => null;
    }
}

namespace UnityEngine.InputSystem.Controls
{
    public class InputControl
    {
        public string name => "";
        public string path => "";
        public string displayName => "";
        public InputDevice device => null;
        public InputControl parent => null;
        public bool IsActuated(float threshold = 0) => false;
        public float EvaluateMagnitude() => 0f;
    }
    public class InputControl<TValue> : InputControl where TValue : struct
    {
        public TValue ReadValue() => default(TValue);
        public TValue ReadValueFromPreviousFrame() => default(TValue);
        public TValue ReadDefaultValue() => default(TValue);
    }
    public class AxisControl : InputControl<float> { }
    public class ButtonControl : AxisControl
    {
        public bool isPressed => false;
        public bool wasPressedThisFrame => false;
        public bool wasReleasedThisFrame => false;
        public float pressPoint { get; set; }
    }
    public class KeyControl : ButtonControl { public Key keyCode => Key.None; }
    public class AnyKeyControl : ButtonControl { }
    public class Vector2Control : InputControl<Vector2> { public AxisControl x => null; public AxisControl y => null; }
    public class StickControl : Vector2Control { public ButtonControl up => null; public ButtonControl down => null; public ButtonControl left => null; public ButtonControl right => null; }
    public class IntegerControl : InputControl<int> { }
}

namespace UnityEngine.InputSystem.UI
{
    public class InputSystemUIInputModule : UnityEngine.EventSystems.BaseInputModule
    {
        public InputActionAsset actionsAsset { get; set; }
        public void AssignDefaultActions() { }
        public void UnassignActions() { }
        public float moveRepeatDelay { get; set; }
        public float moveRepeatRate { get; set; }
        public bool deselectOnBackgroundClick { get; set; }
        public UnityEngine.InputSystem.UI.InputSystemUIInputModule.CursorLockBehavior cursorLockBehavior { get; set; }
        public enum CursorLockBehavior { OutsideScreen, ScreenCenter }
    }
}
