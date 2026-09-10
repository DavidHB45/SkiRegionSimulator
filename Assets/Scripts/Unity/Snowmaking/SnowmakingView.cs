using System.Collections.Generic;
using AlpineSim.Core.Snowmaking;
using AlpineSim.Unity.Guests;
using AlpineSim.Unity.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity.Snowmaking
{
    /// <summary>
    /// Hydrant posts along the runs and a plume cone for every running gun, scaled by its output and
    /// aimed along its heading. Guns themselves are machines drawn by the vehicle views.
    /// </summary>
    public sealed class SnowmakingView : MonoBehaviour
    {
        private Bootstrap _boot;
        private InstancedBatch _hydrants;
        private InstancedBatch _plumes;
        private int _hydrantCount = -1;

        public void Construct(Bootstrap boot)
        {
            _boot = boot;
            var pm = new ProceduralMesh();
            Color32 red = new Color32(220, 50, 40, 255);
            pm.Cylinder(new Vector3(0f, 0.6f, 0f), 0.18f, 1.2f, 1, 6, red);
            pm.Box(new Vector3(0f, 1.25f, 0f), new Vector3(0.5f, 0.3f, 0.5f), red);
            _hydrants = new InstancedBatch(pm.Build("hydrant"), InstancedBatch.MakeMaterial(Color.white, "hydrant"));
            var plume = new ProceduralMesh();
            Color32 white = new Color32(235, 240, 250, 255);
            // a tapered cone made of stacked boxes: length 1 along +Z, widening
            for (int i = 0; i < 6; i++)
            {
                float z = (i + 0.5f) / 6f;
                float w = 0.08f + z * 0.5f;
                plume.Box(new Vector3(0f, 0.15f + z * 0.25f, z), new Vector3(w, w * 0.6f, 1f / 6f), white);
            }
            _plumes = new InstancedBatch(plume.Build("plume"), InstancedBatch.MakeMaterial(new Color(0.95f, 0.97f, 1f), "plume"));
        }

        private void LateUpdate()
        {
            if (_boot.Sim == null) return;
            var s = _boot.Sim.World.Snowmaking;
            _hydrants.Clear();
            foreach (var h in s.Hydrants)
            {
                if (!h.Built) continue;
                _hydrants.Add(_boot.SurfacePoint(h.Pos.X, h.Pos.Y), 0f, 1f);
            }
            _hydrants.Draw();
            _plumes.Clear();
            var w = _boot.Sim.World.Weather.Current;
            foreach (var g in s.Guns)
            {
                if (!g.Running || g.OutputM3PerHour <= 0.01f) continue;
                var def = _boot.Data.Vehicle(g.DefId);
                float reach = def != null ? def.SpecOr("gunReachM", 30f) : 30f;
                float len = Mathf.Clamp(reach * Mathf.Clamp01(0.4f + g.OutputM3PerHour / 60f), 8f, 70f);
                // drift the plume downwind a little (compass wind direction is where it blows FROM)
                float yaw = g.HeadingDeg;
                var pos = _boot.SurfacePoint(g.Pos.X, g.Pos.Y) + Vector3.up * 2.5f;
                _plumes.Add(pos, Quaternion.Euler(-12f, yaw, 0f), new Vector3(len * 0.35f, len * 0.3f, len));
            }
            _plumes.Draw();
        }
    }
}
