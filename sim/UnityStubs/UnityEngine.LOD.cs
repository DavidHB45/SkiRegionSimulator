// Compile-only stubs. See UnityStubs.csproj.
namespace UnityEngine
{
    public enum LODFadeMode { None = 0, CrossFade = 1, SpeedTree = 2 }

    public struct LOD
    {
        public float screenRelativeTransitionHeight;
        public float fadeTransitionWidth;
        public Renderer[] renderers;
        public LOD(float screenRelativeTransitionHeight, Renderer[] renderers)
        {
            this.screenRelativeTransitionHeight = screenRelativeTransitionHeight;
            fadeTransitionWidth = 0f;
            this.renderers = renderers;
        }
    }

    public class LODGroup : Component
    {
        public Vector3 localReferencePoint { get; set; }
        public float size { get; set; }
        public int lodCount => 0;
        public LODFadeMode fadeMode { get; set; }
        public bool animateCrossFading { get; set; }
        public bool enabled { get; set; }
        public static float crossFadeAnimationDuration { get; set; }
        public void RecalculateBounds() { }
        public LOD[] GetLODs() => new LOD[0];
        public void SetLODs(LOD[] lods) { }
        public void ForceLOD(int index) { }
    }
}
