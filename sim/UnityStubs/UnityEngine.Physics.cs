// Compile-only stubs. See UnityStubs.csproj.
namespace UnityEngine
{
    public class Collider : Component
    {
        public bool enabled { get; set; }
        public bool isTrigger { get; set; }
        public float contactOffset { get; set; }
        public Bounds bounds => new Bounds();
        public bool ClosestPointOnBounds(Vector3 position, out Vector3 closest) { closest = position; return true; }
    }

    public class MeshCollider : Collider
    {
        public Mesh sharedMesh { get; set; }
        public bool convex { get; set; }
    }

    public class BoxCollider : Collider
    {
        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
    }

    public class SphereCollider : Collider
    {
        public Vector3 center { get; set; }
        public float radius { get; set; }
    }
}
