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

        // Generated art (docs/ART_CONTRACT.md). The Unity layer reads these instead of hard-coding
        // what a model looks like at range or how worn a machine reads at a given condition.

        /// <summary>Screen-relative height below which a model drops from LOD0 to LOD1.</summary>
        public float ModelLod0ScreenHeight = 0.35f;
        /// <summary>Screen-relative height below which a model drops from LOD1 to LOD2.</summary>
        public float ModelLod1ScreenHeight = 0.12f;
        /// <summary>Screen-relative height below which a model culls entirely.</summary>
        public float ModelLod2ScreenHeight = 0.02f;
        /// <summary>Condition percentage at and above which a machine still looks factory fresh (wear 0).</summary>
        public float WearStartConditionPct = 90f;
        /// <summary>Condition percentage at and below which a machine looks worn out (wear 1).</summary>
        public float WearFullConditionPct = 25f;
        /// <summary>Seconds the wear blend takes to travel its whole range, so a repair fades rather than pops.</summary>
        public float WearBlendSeconds = 4f;
        /// <summary>Hours since the last service that read as a fully salted, filthy machine.</summary>
        public float SoilingHoursForFull = 250f;
        /// <summary>Log one line per resolved model instead of one summary line per tier.</summary>
        public bool LogModelResolutionPerAsset;
        /// <summary>Off sends every visual to the primitive tier, which is how the art pipeline is A/B tested.</summary>
        public bool UseGeneratedModels = true;
        /// <summary>Carriers above this count on one lift stay instanced from the merged proxy mesh.</summary>
        public int MaxGeneratedCarrierObjects = 24;
        public string Comment = "";
    }
}
