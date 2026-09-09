using UnityEngine.InputSystem;

namespace AlpineSim.Unity.Input
{
    public sealed partial class InputBindings
    {
        public sealed class VehicleMap
        {
            public InputActionMap Map;
            public InputAction Throttle;      // W/S, gamepad triggers
            public InputAction Steer;         // A/D, left stick x
            public InputAction Brake;         // Space
            public InputAction BladeLift;     // R/F
            public InputAction BladeAngle;    // Q/E
            public InputAction BladeTilt;     // Z/X
            public InputAction TillerToggle;  // T
            public InputAction ImplementToggle; // X
            public InputAction LightsToggle;  // L
            public InputAction Work;          // G (hold)
            public InputAction EngineToggle;  // I
            public InputAction Horn;          // H
        }

        public VehicleMap Vehicle { get; } = new VehicleMap();

        partial void BuildM2()
        {
            var m = new InputActionMap("Vehicle");
            Vehicle.Map = m;
            Vehicle.Throttle = m.AddAction("Throttle", InputActionType.Value);
            Vehicle.Throttle.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/s").With("Positive", "<Keyboard>/w");
            Vehicle.Throttle.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/downArrow").With("Positive", "<Keyboard>/upArrow");
            Vehicle.Throttle.AddCompositeBinding("1DAxis").With("Negative", "<Gamepad>/leftTrigger").With("Positive", "<Gamepad>/rightTrigger");
            Vehicle.Steer = m.AddAction("Steer", InputActionType.Value);
            Vehicle.Steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
            Vehicle.Steer.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
            Vehicle.Steer.AddBinding("<Gamepad>/leftStick/x");
            Vehicle.Brake = m.AddAction("Brake", InputActionType.Value, "<Keyboard>/space");
            Vehicle.Brake.AddBinding("<Gamepad>/buttonWest");
            Vehicle.BladeLift = m.AddAction("BladeLift", InputActionType.Value);
            Vehicle.BladeLift.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/f").With("Positive", "<Keyboard>/r");
            Vehicle.BladeLift.AddBinding("<Gamepad>/rightStick/y");
            Vehicle.BladeAngle = m.AddAction("BladeAngle", InputActionType.Value);
            Vehicle.BladeAngle.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/q").With("Positive", "<Keyboard>/e");
            Vehicle.BladeAngle.AddBinding("<Gamepad>/rightStick/x");
            Vehicle.BladeTilt = m.AddAction("BladeTilt", InputActionType.Value);
            Vehicle.BladeTilt.AddCompositeBinding("1DAxis").With("Negative", "<Keyboard>/z").With("Positive", "<Keyboard>/x");
            Vehicle.TillerToggle = m.AddAction("TillerToggle", InputActionType.Button, "<Keyboard>/t");
            Vehicle.TillerToggle.AddBinding("<Gamepad>/buttonNorth");
            Vehicle.ImplementToggle = m.AddAction("ImplementToggle", InputActionType.Button, "<Keyboard>/v");
            Vehicle.ImplementToggle.AddBinding("<Gamepad>/buttonEast");
            Vehicle.LightsToggle = m.AddAction("LightsToggle", InputActionType.Button, "<Keyboard>/l");
            Vehicle.Work = m.AddAction("Work", InputActionType.Button, "<Keyboard>/g");
            Vehicle.Work.AddBinding("<Gamepad>/leftShoulder");
            Vehicle.EngineToggle = m.AddAction("EngineToggle", InputActionType.Button, "<Keyboard>/i");
            Vehicle.Horn = m.AddAction("Horn", InputActionType.Button, "<Keyboard>/h");
        }

        partial void EnableM2() { Vehicle.Map.Enable(); }
        partial void DisableM2() { Vehicle.Map.Disable(); }
    }
}
