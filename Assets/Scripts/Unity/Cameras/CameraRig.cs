using UnityEngine;

namespace AlpineSim.Unity.Cameras
{
    public enum CameraMode { Free, Chase, Cockpit }

    /// <summary>
    /// Owns the single game camera and switches between controllers. M0 has the free camera;
    /// M2 adds chase and cockpit controllers via the partial hooks.
    /// </summary>
    public sealed partial class CameraRig : MonoBehaviour
    {
        public Camera Camera { get; private set; }
        public FreeCamera Free { get; private set; }
        public CameraMode Mode { get; private set; } = CameraMode.Free;
        private Bootstrap _boot;

        public void Construct(Bootstrap boot)
        {
            _boot = boot;
            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(transform, false);
            Camera = camGo.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.Skybox;
            Camera.backgroundColor = new Color(0.55f, 0.68f, 0.85f);
            Camera.fieldOfView = 60f;
            Camera.nearClipPlane = 0.3f;
            Camera.farClipPlane = boot.Data.Render.CameraFarClipM;
            camGo.AddComponent<AudioListener>();

            Free = camGo.AddComponent<FreeCamera>();
            Free.Construct(boot);
            ConstructM2();
        }

        partial void ConstructM2();
        partial void OnWorldBuiltM2();
        partial void ApplyModeM2(CameraMode mode);
        partial void CanUseModeM2(CameraMode mode, ref bool ok);

        public void OnWorldBuilt()
        {
            var scenario = _boot.Sim.Scenario;
            var basePos = _boot.SurfacePoint(scenario.BaseArea.X, scenario.BaseArea.Y);
            Free.PlaceLookingAt(basePos + new Vector3(-120f, 120f, -260f), basePos + new Vector3(0f, 200f, 700f));
            OnWorldBuiltM2();
            SetMode(CameraMode.Free);
        }

        public bool CanUse(CameraMode mode)
        {
            if (mode == CameraMode.Free) return true;
            bool ok = false;
            CanUseModeM2(mode, ref ok);
            return ok;
        }

        public void SetMode(CameraMode mode)
        {
            if (!CanUse(mode)) mode = CameraMode.Free;
            Mode = mode;
            Free.enabled = mode == CameraMode.Free;
            ApplyModeM2(mode);
        }

        public void CycleMode()
        {
            var next = (CameraMode)(((int)Mode + 1) % 3);
            if (!CanUse(next)) next = (CameraMode)(((int)next + 1) % 3);
            if (!CanUse(next)) next = CameraMode.Free;
            SetMode(next);
        }

        private void Update()
        {
            if (_boot == null || _boot.Input == null) return;
            if (_boot.Input.Camera.CycleMode.WasPressedThisFrame()) CycleMode();
        }
    }
}
