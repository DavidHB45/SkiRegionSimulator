using System;

namespace AlpineSim.Core.Data
{
    /// <summary>
    /// Presentation settings (render.json). Not balance, but still data: the Unity layer reads
    /// mesh density, LOD ranges and snow-map resolution from here instead of literals.
    /// </summary>
    [Serializable]
    public sealed class RenderData
    {
        public int TerrainChunkSizeM = 128;
        public int TerrainLod0StepM = 2;
        public int TerrainLod1StepM = 8;
        public float TerrainLod0DistanceM = 500f;
        public float SnowMapTexelsPerMeter = 1f;
        public int SnowMapMaxUpdatesPerFrame = 65536;
        public float CameraFarClipM = 6000f;
        public float FreeCameraSpeedMs = 60f;
        public float FreeCameraFastMultiplier = 4f;
        public float MouseLookSensitivity = 0.15f;
        public float GuestMarkerSizeM = 0.8f;
        public int GuestVisualBatch = 1023;
        public float CorduroyWavelengthM = 0.12f;
        public float CorduroyDepthM = 0.03f;
        public float SnowDisplacementScale = 1f;
        public float DebugOverlayAlpha = 0.75f;
        public float UiScale = 1f;
        public string Comment = "";
    }
}
