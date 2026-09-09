using UnityEngine;
using UnityEngine.InputSystem;

namespace AlpineSim.Unity.Input
{
    /// <summary>
    /// Every input action, constructed in C# with the Input System package. No .inputactions asset.
    /// Milestone partial files add their maps (Vehicle map in M2, placement in M4/M5...).
    /// </summary>
    public sealed partial class InputBindings
    {
        public sealed class CameraMap
        {
            public InputActionMap Map;
            public InputAction Move;      // Vector2 (WASD / left stick)
            public InputAction Elevate;   // float (Q/E)
            public InputAction Look;      // Vector2 mouse delta / right stick
            public InputAction LookEnable;// hold right mouse
            public InputAction Fast;      // shift
            public InputAction Zoom;      // scroll
            public InputAction CycleMode; // C
        }

        public sealed class GameMap
        {
            public InputActionMap Map;
            public InputAction Pause;
            public InputAction CycleSpeed;
            public InputAction Speed1, Speed2, Speed3, Speed4;
            public InputAction QuickSave, QuickLoad;
            public InputAction ToggleOverlay;  // O (snow debug overlay cycle)
            public InputAction Panel1, Panel2, Panel3, Panel4, Panel5, Panel6, Panel7, Panel8, Panel9;
            public InputAction Escape;
            public InputAction Click;          // left mouse (world picking)
            public InputAction Interact;       // Enter (enter/exit vehicle, confirm placement)
        }

        public CameraMap Camera { get; } = new CameraMap();
        public GameMap Game { get; } = new GameMap();

        public InputBindings()
        {
            BuildCamera();
            BuildGame();
            BuildM2();
            BuildM4();
            BuildM5();
            BuildM6();
        }

        partial void BuildM2();
        partial void BuildM4();
        partial void BuildM5();
        partial void BuildM6();
        partial void EnableM2(); partial void DisableM2();
        partial void EnableM4(); partial void DisableM4();
        partial void EnableM5(); partial void DisableM5();
        partial void EnableM6(); partial void DisableM6();

        private void BuildCamera()
        {
            var m = new InputActionMap("Camera");
            Camera.Map = m;
            Camera.Move = m.AddAction("Move", InputActionType.Value);
            Camera.Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            Camera.Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            Camera.Move.AddBinding("<Gamepad>/leftStick");
            Camera.Elevate = m.AddAction("Elevate", InputActionType.Value);
            Camera.Elevate.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/q").With("Positive", "<Keyboard>/e");
            Camera.Look = m.AddAction("Look", InputActionType.Value, "<Mouse>/delta");
            Camera.Look.AddBinding("<Gamepad>/rightStick").WithProcessor("scaleVector2(x=8,y=8)");
            Camera.LookEnable = m.AddAction("LookEnable", InputActionType.Button, "<Mouse>/rightButton");
            Camera.Fast = m.AddAction("Fast", InputActionType.Button, "<Keyboard>/leftShift");
            Camera.Fast.AddBinding("<Gamepad>/leftStickPress");
            Camera.Zoom = m.AddAction("Zoom", InputActionType.Value, "<Mouse>/scroll/y");
            Camera.CycleMode = m.AddAction("CycleMode", InputActionType.Button, "<Keyboard>/c");
            Camera.CycleMode.AddBinding("<Gamepad>/rightStickPress");
        }

        private void BuildGame()
        {
            var m = new InputActionMap("Game");
            Game.Map = m;
            Game.Pause = m.AddAction("Pause", InputActionType.Button, "<Keyboard>/p");
            Game.Pause.AddBinding("<Keyboard>/space");
            Game.CycleSpeed = m.AddAction("CycleSpeed", InputActionType.Button, "<Keyboard>/tab");
            Game.Speed1 = m.AddAction("Speed1", InputActionType.Button, "<Keyboard>/1");
            Game.Speed2 = m.AddAction("Speed2", InputActionType.Button, "<Keyboard>/2");
            Game.Speed3 = m.AddAction("Speed3", InputActionType.Button, "<Keyboard>/3");
            Game.Speed4 = m.AddAction("Speed4", InputActionType.Button, "<Keyboard>/4");
            Game.QuickSave = m.AddAction("QuickSave", InputActionType.Button, "<Keyboard>/f5");
            Game.QuickLoad = m.AddAction("QuickLoad", InputActionType.Button, "<Keyboard>/f9");
            Game.ToggleOverlay = m.AddAction("ToggleOverlay", InputActionType.Button, "<Keyboard>/o");
            Game.Panel1 = m.AddAction("Panel1", InputActionType.Button, "<Keyboard>/f1");
            Game.Panel2 = m.AddAction("Panel2", InputActionType.Button, "<Keyboard>/f2");
            Game.Panel3 = m.AddAction("Panel3", InputActionType.Button, "<Keyboard>/f3");
            Game.Panel4 = m.AddAction("Panel4", InputActionType.Button, "<Keyboard>/f4");
            Game.Panel5 = m.AddAction("Panel5", InputActionType.Button, "<Keyboard>/f6");
            Game.Panel6 = m.AddAction("Panel6", InputActionType.Button, "<Keyboard>/f7");
            Game.Panel7 = m.AddAction("Panel7", InputActionType.Button, "<Keyboard>/f8");
            Game.Panel8 = m.AddAction("Panel8", InputActionType.Button, "<Keyboard>/f10");
            Game.Panel9 = m.AddAction("Panel9", InputActionType.Button, "<Keyboard>/f11");
            Game.Escape = m.AddAction("Escape", InputActionType.Button, "<Keyboard>/escape");
            Game.Click = m.AddAction("Click", InputActionType.Button, "<Mouse>/leftButton");
            Game.Interact = m.AddAction("Interact", InputActionType.Button, "<Keyboard>/enter");
            Game.Interact.AddBinding("<Gamepad>/buttonSouth");
        }

        public void Enable()
        {
            Camera.Map.Enable();
            Game.Map.Enable();
            EnableM2(); EnableM4(); EnableM5(); EnableM6();
        }

        public void Disable()
        {
            Camera.Map.Disable();
            Game.Map.Disable();
            DisableM2(); DisableM4(); DisableM5(); DisableM6();
        }

        /// <summary>Current mouse position in screen pixels (null-safe when no mouse).</summary>
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    }
}
