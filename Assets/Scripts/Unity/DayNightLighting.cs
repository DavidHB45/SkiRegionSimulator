using UnityEngine;

namespace AlpineSim.Unity
{
    /// <summary>
    /// Directional sun driven by the simulation clock. Night is dark blue with a weak moon so the
    /// vehicle lights matter; day is bright and warm. No baked lighting anywhere.
    /// </summary>
    public sealed class DayNightLighting : MonoBehaviour
    {
        private Bootstrap _boot;
        private Light _sun;
        private Light _moon;

        private void Awake()
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(transform, false);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.7f;
            _sun.color = new Color(1f, 0.96f, 0.88f);

            var moonGo = new GameObject("Moon");
            moonGo.transform.SetParent(transform, false);
            _moon = moonGo.AddComponent<Light>();
            _moon.type = LightType.Directional;
            _moon.shadows = LightShadows.None;
            _moon.color = new Color(0.55f, 0.65f, 0.9f);
            _moon.intensity = 0.12f;

            RenderSettings.sun = _sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.00035f;
        }

        public void Bind(Bootstrap boot) { _boot = boot; }

        private void LateUpdate()
        {
            if (_boot == null || _boot.Sim == null) return;
            float hour = _boot.Sim.World.Time.HourOfDayF;
            // Sun elevation: peak 45 degrees at 12:30, below horizon ~17:00-07:30 (alpine winter).
            float t = (hour - 12.5f) / 24f * Mathf.PI * 2f;
            float elevation = Mathf.Cos(t) * 45f - 6f;
            float azimuth = 180f + (hour - 12.5f) * 15f; // Unity yaw: south at noon
            _sun.transform.rotation = Quaternion.Euler(elevation, azimuth, 0f);
            float daylight = Mathf.Clamp01((elevation + 4f) / 14f);
            _sun.intensity = Mathf.Lerp(0f, 1.25f, daylight);
            float warm = Mathf.Clamp01(1f - elevation / 25f);
            _sun.color = Color.Lerp(new Color(1f, 0.97f, 0.9f), new Color(1f, 0.72f, 0.5f), warm * daylight);
            _moon.transform.rotation = Quaternion.Euler(50f, azimuth + 180f, 0f);
            _moon.intensity = Mathf.Lerp(0.14f, 0f, daylight);

            var dayAmbient = new Color(0.5f, 0.56f, 0.68f);
            var nightAmbient = new Color(0.05f, 0.06f, 0.1f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(nightAmbient, dayAmbient, daylight);
            var daySky = new Color(0.58f, 0.72f, 0.9f);
            var nightSky = new Color(0.03f, 0.04f, 0.08f);
            var sky = Color.Lerp(nightSky, daySky, daylight);
            RenderSettings.fogColor = sky;
            if (_boot.Cameras != null && _boot.Cameras.Camera != null)
            {
                _boot.Cameras.Camera.clearFlags = CameraClearFlags.SolidColor;
                _boot.Cameras.Camera.backgroundColor = sky;
            }
        }
    }
}
