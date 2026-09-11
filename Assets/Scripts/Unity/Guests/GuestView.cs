using AlpineSim.Core.Guests;
using AlpineSim.Unity.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity.Guests
{
    /// <summary>
    /// Draws every guest cohort as an instanced marker at its sim position, coloured by phase.
    /// Cohorts riding a lift are not drawn (the carriers show the lift is loaded). One draw call per
    /// 1023 markers; nothing per guest is allocated.
    /// </summary>
    public sealed class GuestView : MonoBehaviour
    {
        private Bootstrap _boot;
        private InstancedBatch _skiing, _queue, _walking, _base;
        private float _size;

        public void Construct(Bootstrap boot)
        {
            _boot = boot;
            _size = Mathf.Max(0.3f, boot.Data.Render.GuestMarkerSizeM);
            var pm = new ProceduralMesh();
            Color32 white = new Color32(255, 255, 255, 255);
            pm.Box(new Vector3(0f, 0.9f, 0f), new Vector3(0.5f, 1.2f, 0.35f), white);
            pm.Box(new Vector3(0f, 1.65f, 0f), new Vector3(0.3f, 0.3f, 0.3f), white);
            var mesh = pm.Build("guest");
            int batch = Mathf.Clamp(boot.Data.Render.GuestVisualBatch, 1, 1023);
            _skiing = new InstancedBatch(mesh, InstancedBatch.MakeMaterial(new Color(0.15f, 0.55f, 0.95f), "guest_skiing"), batch);
            _queue = new InstancedBatch(mesh, InstancedBatch.MakeMaterial(new Color(0.95f, 0.6f, 0.15f), "guest_queue"), batch);
            _walking = new InstancedBatch(mesh, InstancedBatch.MakeMaterial(new Color(0.7f, 0.7f, 0.75f), "guest_walking"), batch);
            _base = new InstancedBatch(mesh, InstancedBatch.MakeMaterial(new Color(0.4f, 0.8f, 0.4f), "guest_base"), batch);
        }

        private void LateUpdate()
        {
            if (_boot.Sim == null) return;
            _skiing.Clear(); _queue.Clear(); _walking.Clear(); _base.Clear();
            var agents = _boot.Sim.World.Guests.Agents;
            var terrain = _boot.Sim.Terrain;
            var snow = _boot.Sim.World.Snow;
            for (int i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                InstancedBatch target;
                switch (a.Phase)
                {
                    case GuestPhase.Skiing: target = _skiing; break;
                    case GuestPhase.InQueue: target = _queue; break;
                    case GuestPhase.WalkingToLift: case GuestPhase.Leaving: case GuestPhase.Arriving: target = _walking; break;
                    case GuestPhase.AtBase: case GuestPhase.AtTop: case GuestPhase.Eating: target = _base; break;
                    default: continue;
                }
                float x = Mathf.Clamp(a.Pos.X, 0f, terrain.SizeM), y = Mathf.Clamp(a.Pos.Y, 0f, terrain.SizeM);
                float h = terrain.SampleHeight(x, y) + (snow != null ? snow.DepthAtMm(a.Pos) * 0.001f : 0f);
                float scale = _size * Mathf.Clamp(Mathf.Sqrt(Mathf.Max(1, a.Guests)), 1f, 3f);
                // spread cohorts of the same phase a little so queues read as crowds
                float jitter = (a.Id % 7 - 3) * 0.35f;
                target.Add(new Vector3(x + jitter, h, y + ((a.Id / 7) % 5 - 2) * 0.35f), 0f, scale);
            }
            _skiing.Draw(); _queue.Draw(); _walking.Draw(); _base.Draw();
        }
    }
}
