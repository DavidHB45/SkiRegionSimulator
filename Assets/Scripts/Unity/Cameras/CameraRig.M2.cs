using AlpineSim.Unity.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity.Cameras
{
    public sealed partial class CameraRig
    {
        private VehicleViewManager _vehicles;
        private float _chaseYaw;
        private float _chasePitch = 18f;
        private float _chaseDistance = 14f;
        private Vector3 _chaseVel;

        public void BindVehicles(VehicleViewManager vehicles) { _vehicles = vehicles; }

        partial void ConstructM2() { }
        partial void OnWorldBuiltM2() { _vehicles = null; }

        partial void CanUseModeM2(CameraMode mode, ref bool ok)
        {
            ok = _vehicles != null && _vehicles.PlayerView != null;
        }

        partial void ApplyModeM2(CameraMode mode)
        {
            if (mode == CameraMode.Chase && _vehicles != null && _vehicles.PlayerView != null)
                _chaseYaw = _vehicles.PlayerView.transform.eulerAngles.y;
        }

        private void LateUpdate()
        {
            if (Mode == CameraMode.Free || _vehicles == null) return;
            var view = _vehicles.PlayerView;
            if (view == null) { SetMode(CameraMode.Free); return; }
            var input = _boot.Input.Camera;
            var render = _boot.Data.Render;
            if (Mode == CameraMode.Cockpit)
            {
                Camera.transform.position = view.CabAnchor.position;
                bool looking = input.LookEnable.IsPressed();
                if (looking)
                {
                    Vector2 look = input.Look.ReadValue<Vector2>() * render.MouseLookSensitivity;
                    _chaseYaw += look.x;
                    _chasePitch = Mathf.Clamp(_chasePitch - look.y, -40f, 40f);
                }
                else { _chaseYaw = Mathf.LerpAngle(_chaseYaw, 0f, Time.unscaledDeltaTime * 3f); _chasePitch = Mathf.Lerp(_chasePitch, 4f, Time.unscaledDeltaTime * 3f); }
                Camera.transform.rotation = view.transform.rotation * Quaternion.Euler(_chasePitch, _chaseYaw, 0f);
                Cursor.lockState = looking ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !looking;
                return;
            }
            // chase
            float zoom = input.Zoom.ReadValue<float>();
            if (Mathf.Abs(zoom) > 0.01f) _chaseDistance = Mathf.Clamp(_chaseDistance * (zoom > 0 ? 0.85f : 1.18f), 5f, 60f);
            bool orbit = input.LookEnable.IsPressed();
            if (orbit)
            {
                Vector2 look = input.Look.ReadValue<Vector2>() * render.MouseLookSensitivity;
                _chaseYaw += look.x;
                _chasePitch = Mathf.Clamp(_chasePitch - look.y, -10f, 80f);
            }
            else
            {
                _chaseYaw = Mathf.LerpAngle(_chaseYaw, view.transform.eulerAngles.y, Time.unscaledDeltaTime * 2.5f);
            }
            Cursor.lockState = orbit ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !orbit;
            var rot = Quaternion.Euler(_chasePitch, _chaseYaw, 0f);
            Vector3 target = view.ChaseAnchor.position;
            Vector3 desired = target - rot * Vector3.forward * _chaseDistance;
            // keep the camera above the snow
            var terrain = _boot.Sim.Terrain;
            float ground = terrain.SampleHeight(Mathf.Clamp(desired.x, 0f, terrain.SizeM), Mathf.Clamp(desired.z, 0f, terrain.SizeM)) + 1.5f;
            if (desired.y < ground) desired.y = ground;
            Camera.transform.position = Vector3.SmoothDamp(Camera.transform.position, desired, ref _chaseVel, 0.12f, Mathf.Infinity, Time.unscaledDeltaTime);
            Camera.transform.LookAt(target + Vector3.up * 0.5f);
        }
    }
}
