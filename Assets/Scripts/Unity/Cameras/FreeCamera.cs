using UnityEngine;
using UnityEngine.EventSystems;

namespace AlpineSim.Unity.Cameras
{
    /// <summary>WASD/QE fly camera with right-mouse look. Speed from render.json; scroll wheel scales it.</summary>
    public sealed class FreeCamera : MonoBehaviour
    {
        private Bootstrap _boot;
        private float _yaw, _pitch;
        private float _speedScale = 1f;

        public void Construct(Bootstrap boot) { _boot = boot; }

        public void PlaceLookingAt(Vector3 position, Vector3 target)
        {
            transform.position = position;
            transform.LookAt(target);
            var e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            if (_boot == null || _boot.Input == null || _boot.Sim == null) return;
            var input = _boot.Input.Camera;
            var render = _boot.Data.Render;
            float dt = Time.unscaledDeltaTime;

            bool looking = input.LookEnable.IsPressed();
            Cursor.lockState = looking ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !looking;
            if (looking)
            {
                Vector2 look = input.Look.ReadValue<Vector2>() * render.MouseLookSensitivity;
                _yaw += look.x;
                _pitch = Mathf.Clamp(_pitch - look.y, -89f, 89f);
            }
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            float zoom = input.Zoom.ReadValue<float>();
            if (Mathf.Abs(zoom) > 0.01f) _speedScale = Mathf.Clamp(_speedScale * (zoom > 0 ? 1.25f : 0.8f), 0.05f, 20f);

            bool uiFocused = EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null;
            if (uiFocused) return;

            Vector2 move = input.Move.ReadValue<Vector2>();
            float elevate = input.Elevate.ReadValue<float>();
            float speed = render.FreeCameraSpeedMs * _speedScale * (input.Fast.IsPressed() ? render.FreeCameraFastMultiplier : 1f);
            Vector3 delta = (transform.forward * move.y + transform.right * move.x + Vector3.up * elevate) * speed * dt;
            Vector3 p = transform.position + delta;

            var terrain = _boot.Sim.Terrain;
            float size = terrain.SizeM;
            p.x = Mathf.Clamp(p.x, -200f, size + 200f);
            p.z = Mathf.Clamp(p.z, -200f, size + 200f);
            float ground = terrain.SampleHeight(Mathf.Clamp(p.x, 0, size), Mathf.Clamp(p.z, 0, size));
            if (p.y < ground + 2f) p.y = ground + 2f;
            if (p.y > terrain.MaxHeight + 3000f) p.y = terrain.MaxHeight + 3000f;
            transform.position = p;
        }
    }
}
