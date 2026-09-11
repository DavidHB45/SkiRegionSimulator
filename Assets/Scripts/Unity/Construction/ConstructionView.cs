using System.Collections.Generic;
using AlpineSim.Core.Construction;
using AlpineSim.Core.Lifts;
using AlpineSim.Unity.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity.Construction
{
    /// <summary>
    /// Shows staked lift plans (tower sites and the line) and active project corridors so the player
    /// can see where machines must drive. Rebuilt when the project list changes.
    /// </summary>
    public sealed class ConstructionView : MonoBehaviour
    {
        private Bootstrap _boot;
        private readonly List<GameObject> _markers = new List<GameObject>();
        private LiftPlanResult _plan;
        private GameObject _planGo;
        private int _projectSignature = -1;
        private float _nextSync;

        public void Construct(Bootstrap boot) { _boot = boot; }

        /// <summary>Preview a plan (null clears it).</summary>
        public void ShowPlan(LiftPlanResult plan)
        {
            _plan = plan;
            if (_planGo != null) { Destroy(_planGo); _planGo = null; }
            if (plan == null) return;
            _planGo = new GameObject("plan");
            _planGo.transform.SetParent(transform, false);
            var pm = new ProceduralMesh();
            Color32 ok = new Color32(80, 220, 120, 255), bad = new Color32(230, 70, 60, 255);
            Color32 col = plan.Ok ? ok : bad;
            var prev = _boot.SurfacePoint(plan.Bottom.X, plan.Bottom.Y) + Vector3.up * 6f;
            foreach (var t in plan.Towers)
            {
                var p = _boot.SurfacePoint(t.X, t.Y);
                pm.Cylinder(p + Vector3.up * 5f, 0.6f, 10f, 1, 6, col);
                pm.Beam(prev, p + Vector3.up * 10f, 0.3f, col);
                prev = p + Vector3.up * 10f;
            }
            pm.Beam(prev, _boot.SurfacePoint(plan.Top.X, plan.Top.Y) + Vector3.up * 6f, 0.3f, col);
            pm.Box(_boot.SurfacePoint(plan.Bottom.X, plan.Bottom.Y) + Vector3.up * 3f, new Vector3(8f, 6f, 12f), col);
            pm.Box(_boot.SurfacePoint(plan.Top.X, plan.Top.Y) + Vector3.up * 3f, new Vector3(8f, 6f, 12f), col);
            var mf = _planGo.AddComponent<MeshFilter>();
            mf.sharedMesh = pm.Build("plan");
            _planGo.AddComponent<MeshRenderer>().sharedMaterial = ProceduralMesh.VertexMaterial;
        }

        private void LateUpdate()
        {
            if (_boot.Sim == null || Time.unscaledTime < _nextSync) return;
            _nextSync = Time.unscaledTime + 1f;
            var projects = _boot.Sim.World.Construction.Projects;
            int sig = 0;
            foreach (var p in projects) sig = sig * 31 + p.Id * 7 + (int)p.Status + p.StageIndex;
            if (sig == _projectSignature) return;
            _projectSignature = sig;
            foreach (var m in _markers) Destroy(m);
            _markers.Clear();
            foreach (var p in projects)
            {
                if (p.Status == ProjectStatus.Complete || p.Status == ProjectStatus.Cancelled) continue;
                var go = new GameObject("project_" + p.Id);
                go.transform.SetParent(transform, false);
                var pm = new ProceduralMesh();
                Color32 col = p.Kind == ProjectKind.Run ? new Color32(240, 200, 80, 255) : new Color32(200, 120, 240, 255);
                if (p.Corridor.Count > 1)
                {
                    for (int i = 0; i + 1 < p.Corridor.Count; i++)
                    {
                        var a = _boot.SurfacePoint(p.Corridor[i].X, p.Corridor[i].Y) + Vector3.up * 1.5f;
                        var b = _boot.SurfacePoint(p.Corridor[i + 1].X, p.Corridor[i + 1].Y) + Vector3.up * 1.5f;
                        pm.Beam(a, b, 0.6f, col);
                    }
                }
                // site flag
                var site = _boot.SurfacePoint(p.Site.X, p.Site.Y);
                pm.Cylinder(site + Vector3.up * 4f, 0.2f, 8f, 1, 5, new Color32(240, 240, 240, 255));
                pm.Box(site + Vector3.up * 7.5f, new Vector3(3f, 1.6f, 0.1f), col);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = pm.Build("project");
                go.AddComponent<MeshRenderer>().sharedMaterial = ProceduralMesh.VertexMaterial;
                _markers.Add(go);
            }
        }
    }
}
