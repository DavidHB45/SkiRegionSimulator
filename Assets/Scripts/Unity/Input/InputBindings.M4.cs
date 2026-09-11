using UnityEngine.InputSystem;

namespace AlpineSim.Unity.Input
{
    public sealed partial class InputBindings
    {
        /// <summary>Placement and staking (lifts, runs, guns, hydrants). Left click places, right click / Escape cancels, Backspace undoes a point.</summary>
        public sealed class PlacementMap
        {
            public InputActionMap Map;
            public InputAction Place;      // left mouse
            public InputAction Cancel;     // right mouse
            public InputAction Undo;       // backspace
            public InputAction Finish;     // enter
            public InputAction Rotate;     // scroll while placing a gun
        }

        public PlacementMap Placement { get; } = new PlacementMap();

        partial void BuildM4()
        {
            var m = new InputActionMap("Placement");
            Placement.Map = m;
            Placement.Place = m.AddAction("Place", InputActionType.Button, "<Mouse>/leftButton");
            Placement.Cancel = m.AddAction("Cancel", InputActionType.Button, "<Mouse>/rightButton");
            Placement.Undo = m.AddAction("Undo", InputActionType.Button, "<Keyboard>/backspace");
            Placement.Finish = m.AddAction("Finish", InputActionType.Button, "<Keyboard>/enter");
            Placement.Rotate = m.AddAction("Rotate", InputActionType.Value, "<Mouse>/scroll/y");
        }

        partial void EnableM4() { Placement.Map.Enable(); }
        partial void DisableM4() { Placement.Map.Disable(); }
    }
}
