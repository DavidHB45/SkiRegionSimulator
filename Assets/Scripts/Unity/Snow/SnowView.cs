using UnityEngine;

namespace AlpineSim.Unity.Snow
{
    public enum SnowDebugMode { None = 0, Depth = 1, Density = 2, Roughness = 3, Pqi = 4, GroomAge = 5, Surface = 6 }

    /// <summary>
    /// Owns the snow material and the debug overlay material, swaps them onto the terrain, and keeps
    /// the snow map current through the uploader. The overlay cycles with the O key.
    /// </summary>
    public sealed class SnowView : MonoBehaviour
    {
        private Bootstrap _boot;
        private SnowMapUploader _uploader;
        private Material _snowMaterial;
        private Material _overlayMaterial;
        private SnowDebugMode _mode = SnowDebugMode.None;
        public SnowDebugMode DebugMode => _mode;
        public SnowMapUploader Uploader => _uploader;

        public void Construct(Bootstrap boot)
        {
            _boot = boot;
            var render = boot.Data.Render;
            var grid = boot.Sim.World.Snow;
            var shader = boot.snowSurfaceShader != null ? boot.snowSurfaceShader : Shader.Find("AlpineSim/SnowSurface");
            _snowMaterial = new Material(shader != null ? shader : Shader.Find("Legacy Shaders/Diffuse")) { name = "SnowSurface" };
            _snowMaterial.SetFloat("_MapSize", boot.Sim.Terrain.SizeM);
            _snowMaterial.SetFloat("_CorduroyWavelength", render.CorduroyWavelengthM);
            _snowMaterial.SetFloat("_CorduroyDepth", render.CorduroyDepthM);
            _snowMaterial.SetFloat("_DisplacementScale", render.SnowDisplacementScale);
            var overlayShader = boot.debugOverlayShader != null ? boot.debugOverlayShader : Shader.Find("AlpineSim/SnowDebugOverlay");
            if (overlayShader != null)
            {
                _overlayMaterial = new Material(overlayShader) { name = "SnowDebugOverlay" };
                _overlayMaterial.SetFloat("_Alpha", render.DebugOverlayAlpha);
                _overlayMaterial.SetFloat("_DisplacementScale", render.SnowDisplacementScale);
            }
            float groomAge = boot.Data.Tuning.F("pqi.groomHalfLifeHours") * 2f;
            _uploader = new SnowMapUploader(grid, render, boot.Sim.Terrain.SizeM, groomAge, boot.snowDeformCompute);
            _snowMaterial.SetTexture("_SnowMap", _uploader.SnowMap);
            _snowMaterial.SetTexture("_SurfaceMap", _uploader.SurfaceMap);
            if (_overlayMaterial != null)
            {
                _overlayMaterial.SetTexture("_SnowMap", _uploader.SnowMap);
                _overlayMaterial.SetTexture("_SurfaceMap", _uploader.SurfaceMap);
            }
            _uploader.Rebuild(boot.Sim.World.Time.Tick);
            boot.TerrainView.SetMaterial(_snowMaterial);
            ApplyOverlay();
        }

        public void CycleMode()
        {
            _mode = (SnowDebugMode)(((int)_mode + 1) % 7);
            ApplyOverlay();
        }

        private void ApplyOverlay()
        {
            if (_overlayMaterial == null || _boot.TerrainView == null) return;
            _overlayMaterial.SetInt("_Mode", (int)_mode);
            _boot.TerrainView.SetOverlay(_mode == SnowDebugMode.None ? null : _overlayMaterial);
        }

        private void Update()
        {
            if (_boot.Sim == null) return;
            if (_boot.Input.Game.ToggleOverlay.WasPressedThisFrame()) CycleMode();
        }

        private void LateUpdate()
        {
            if (_boot.Sim == null || _uploader == null) return;
            _uploader.Update(_boot.Sim.World.Time.Tick);
        }

        public string Status()
        {
            if (_mode == SnowDebugMode.None) return "";
            return "Snow overlay: " + _mode + (_uploader != null ? "  (" + (_uploader.UsesCompute ? "compute" : "cpu") + " upload, " + _uploader.PendingChunks + " chunks pending)" : "");
        }

        private void OnDestroy()
        {
            _uploader?.Dispose();
            _uploader = null;
        }
    }
}
