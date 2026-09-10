using AlpineSim.Core.Math;
using AlpineSim.Core.Terrain;
using UnityEngine;

namespace AlpineSim.Unity.Terrain
{
    /// <summary>
    /// Mouse picking against the simulation heightfield (no colliders on the terrain chunks).
    /// Marches the camera ray in fixed steps until it crosses the surface, then bisects.
    /// </summary>
    public static class TerrainPicker
    {
        public static bool Pick(Camera camera, Vector2 screenPos, TerrainData terrain, out Vec2 mapPoint, float maxDistance = 6000f)
        {
            mapPoint = Vec2.Zero;
            if (camera == null || terrain == null) return false;
            var ray = camera.ScreenPointToRay(screenPos);
            float step = 4f;
            float prevT = 0f;
            bool prevAbove = HeightDiff(ray.GetPoint(0f), terrain) > 0f;
            if (!prevAbove) return false;
            for (float t = step; t <= maxDistance; t += step)
            {
                var p = ray.GetPoint(t);
                if (p.x < 0f || p.z < 0f || p.x > terrain.SizeM || p.z > terrain.SizeM)
                {
                    // outside the map: keep marching (the ray may re-enter) but stop once far below the lowest terrain
                    if (p.y < -500f) return false;
                    prevT = t; continue;
                }
                bool above = HeightDiff(p, terrain) > 0f;
                if (!above)
                {
                    float lo = prevT, hi = t;
                    for (int i = 0; i < 12; i++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (HeightDiff(ray.GetPoint(mid), terrain) > 0f) lo = mid; else hi = mid;
                    }
                    var hit = ray.GetPoint((lo + hi) * 0.5f);
                    mapPoint = new Vec2(Mathf.Clamp(hit.x, 0f, terrain.SizeM), Mathf.Clamp(hit.z, 0f, terrain.SizeM));
                    return true;
                }
                prevT = t;
                if (t > 400f) step = 8f;
            }
            return false;
        }

        private static float HeightDiff(Vector3 p, TerrainData terrain)
        {
            float x = Mathf.Clamp(p.x, 0f, terrain.SizeM), z = Mathf.Clamp(p.z, 0f, terrain.SizeM);
            return p.y - terrain.SampleHeight(x, z);
        }
    }
}
