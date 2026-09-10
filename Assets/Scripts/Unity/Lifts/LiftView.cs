using System.Collections.Generic;
using AlpineSim.Core.Lifts;
using AlpineSim.Core.Math;
using AlpineSim.Unity.Guests;
using AlpineSim.Unity.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity.Lifts
{
    /// <summary>
    /// One lift's visuals: towers, terminals and a sagging haul rope built once from the sim's tower
    /// positions, plus carriers drawn instanced and animated from sim time while the lift runs.
    /// Reads LiftState only.
    /// </summary>
    public sealed class LiftView : MonoBehaviour
    {
        public int LiftId { get; private set; }
        public LiftState State { get; private set; }
        public LiftTypeDef Type { get; private set; }

        private Bootstrap _boot;
        private MeshRenderer _staticRenderer;
        private MeshFilter _staticFilter;
        private Material _statusMaterial;
        private MeshRenderer _statusRenderer;
        private InstancedBatch _carriers;
        private readonly List<Vector3> _line = new List<Vector3>();   // sampled rope polyline (up-line), world space
        private readonly List<float> _lineDist = new List<float>();
        private float _lineLength;
        private float _towerHeight;
        private LiftStatus _lastStatus = LiftStatus.Planned;
        private int _lastTowerCount = -1;
        private float _phase;

        public void Construct(Bootstrap boot, LiftState state, LiftTypeDef type)
        {
            _boot = boot;
            State = state;
            Type = type;
            LiftId = state.Id;
            name = "lift_" + state.Id + "_" + state.TypeId;
            var go = new GameObject("structure");
            go.transform.SetParent(transform, false);
            _staticFilter = go.AddComponent<MeshFilter>();
            _staticRenderer = go.AddComponent<MeshRenderer>();
            _staticRenderer.sharedMaterial = ProceduralMesh.VertexMaterial;
            _staticRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            var st = new GameObject("status");
            st.transform.SetParent(transform, false);
            var sf = st.AddComponent<MeshFilter>();
            _statusRenderer = st.AddComponent<MeshRenderer>();
            _statusMaterial = InstancedBatch.MakeMaterial(Color.gray, "liftStatus");
            _statusMaterial.enableInstancing = false;
            _statusRenderer.sharedMaterial = _statusMaterial;
            var beacon = new ProceduralMesh();
            beacon.Box(Vector3.zero, new Vector3(1.2f, 1.2f, 1.2f), Color.white);
            sf.sharedMesh = beacon.Build("beacon");

            _carriers = new InstancedBatch(BuildCarrierMesh(type), InstancedBatch.MakeMaterial(CarrierColor(type), "carrier_" + type.Id));
            Rebuild();
        }

        private static Color CarrierColor(LiftTypeDef type)
        {
            switch (type.Family)
            {
                case LiftFamily.Surface: return new Color(0.9f, 0.6f, 0.2f);
                case LiftFamily.Gondola: return new Color(0.85f, 0.15f, 0.15f);
                case LiftFamily.Aerial: return new Color(0.2f, 0.3f, 0.8f);
                case LiftFamily.Rail: return new Color(0.7f, 0.1f, 0.3f);
                case LiftFamily.Hybrid: return new Color(0.8f, 0.4f, 0.1f);
                default: return new Color(0.2f, 0.25f, 0.3f);
            }
        }

        private static float TowerHeightFor(LiftTypeDef type)
        {
            switch (type.Family)
            {
                case LiftFamily.Surface: return 5f;
                case LiftFamily.Chair: return type.Grip == GripType.Detachable ? 12f : 9f;
                case LiftFamily.Gondola: return 15f;
                case LiftFamily.Hybrid: return 14f;
                case LiftFamily.Aerial: return type.RopeConfiguration == RopeConfig.Tri ? 34f : 26f;
                default: return 3f;
            }
        }

        private static Mesh BuildCarrierMesh(LiftTypeDef type)
        {
            var pm = new ProceduralMesh();
            Color32 body = new Color32(255, 255, 255, 255);
            switch (type.Family)
            {
                case LiftFamily.Surface:
                    pm.Beam(Vector3.zero, new Vector3(0f, -3f, 0f), 0.08f, body);
                    pm.Box(new Vector3(0f, -3f, 0f), new Vector3(1.4f, 0.12f, 0.12f), body);
                    break;
                case LiftFamily.Gondola:
                case LiftFamily.Hybrid:
                case LiftFamily.Aerial:
                {
                    float w = Mathf.Clamp(0.9f + type.SeatsOrCabinCapacity * 0.08f, 1.6f, 6f);
                    pm.Beam(Vector3.zero, new Vector3(0f, -2.2f, 0f), 0.12f, body);
                    pm.Box(new Vector3(0f, -2.2f - w * 0.45f, 0f), new Vector3(w, w * 0.9f, w * 0.8f), body);
                    break;
                }
                case LiftFamily.Rail:
                    pm.Box(new Vector3(0f, 1.2f, 0f), new Vector3(2.6f, 2.4f, 8f), body);
                    break;
                default:
                {
                    float w = 0.55f * Mathf.Max(1, type.SeatsOrCabinCapacity) + 0.3f;
                    pm.Beam(Vector3.zero, new Vector3(0f, -2.4f, 0f), 0.08f, body);
                    pm.Box(new Vector3(0f, -2.4f, 0f), new Vector3(w, 0.15f, 0.7f), body);
                    pm.Box(new Vector3(0f, -2.0f, -0.35f), new Vector3(w, 0.8f, 0.1f), body);
                    break;
                }
            }
            return pm.Build("carrier_" + type.Id);
        }

        /// <summary>Rebuild the static structure from the sim's tower list (called when towers appear during construction).</summary>
        public void Rebuild()
        {
            _lastTowerCount = State.Towers.Count;
            _towerHeight = TowerHeightFor(Type);
            var pm = new ProceduralMesh();
            Color32 steel = new Color32(120, 128, 136, 255);
            Color32 dark = new Color32(60, 64, 70, 255);
            Color32 terminal = new Color32(150, 150, 160, 255);
            bool rail = Type.Family == LiftFamily.Rail;

            var points = new List<Vector3>();
            points.Add(SurfaceAt(State.Bottom) + Vector3.up * (rail ? 0.3f : _towerHeight * 0.6f));
            foreach (var t in State.Towers)
            {
                var basePos = SurfaceAt(t);
                var topPos = basePos + Vector3.up * _towerHeight;
                if (rail) { pm.Box(basePos + Vector3.up * 0.15f, new Vector3(3f, 0.3f, 3f), dark); points.Add(basePos + Vector3.up * 0.3f); continue; }
                pm.Cylinder(basePos + Vector3.up * _towerHeight * 0.5f, Type.Family == LiftFamily.Aerial ? 1.2f : 0.45f, _towerHeight, 1, 8, steel);
                pm.Box(basePos + Vector3.up * 0.4f, new Vector3(2.4f, 0.8f, 2.4f), dark);
                // cross arm with sheave assemblies on both sides
                var dir = Bootstrap.ToUnity(State.Direction, 0f);
                var side = Vector3.Cross(Vector3.up, dir).normalized;
                float arm = Type.Family == LiftFamily.Surface ? 1.2f : 3.2f;
                pm.Box(topPos, new Vector3(0.3f, 0.3f, 0.3f), steel);
                pm.Beam(topPos - side * arm, topPos + side * arm, 0.25f, steel);
                pm.Box(topPos - side * arm, new Vector3(0.6f, 0.4f, 1.6f), Quaternion.LookRotation(dir), dark);
                pm.Box(topPos + side * arm, new Vector3(0.6f, 0.4f, 1.6f), Quaternion.LookRotation(dir), dark);
                points.Add(topPos);
            }
            points.Add(SurfaceAt(State.Top) + Vector3.up * (rail ? 0.3f : _towerHeight * 0.6f));

            // terminals
            var d = Bootstrap.ToUnity(State.Direction, 0f);
            var rot = Quaternion.LookRotation(d);
            float termL = Type.Family == LiftFamily.Surface ? 4f : (Type.Grip == GripType.Detachable || Type.IsEnclosed ? 22f : 10f);
            float termW = Type.Family == LiftFamily.Surface ? 2f : 8f;
            float termH = Type.Family == LiftFamily.Surface ? 3f : _towerHeight * 0.7f;
            pm.Box(SurfaceAt(State.Bottom) + Vector3.up * termH * 0.5f, new Vector3(termW, termH, termL), rot, terminal);
            pm.Box(SurfaceAt(State.Top) + Vector3.up * termH * 0.5f, new Vector3(termW, termH, termL), rot, terminal);

            // haul rope: parabolic sag between consecutive support points, both directions
            _line.Clear(); _lineDist.Clear(); _lineLength = 0f;
            var sideOffset = Vector3.Cross(Vector3.up, d).normalized * (Type.Family == LiftFamily.Surface ? 1.2f : 3.2f);
            for (int i = 0; i + 1 < points.Count; i++)
            {
                var a = points[i]; var b = points[i + 1];
                float span = Vector3.Distance(a, b);
                float sag = rail ? 0f : Mathf.Min(span * span * 0.00025f, 8f);
                int steps = Mathf.Clamp(Mathf.CeilToInt(span / 25f), 1, 24);
                for (int s = 0; s < steps; s++)
                {
                    float t0 = s / (float)steps, t1 = (s + 1) / (float)steps;
                    var p0 = Vector3.Lerp(a, b, t0) - Vector3.up * (sag * 4f * t0 * (1f - t0));
                    var p1 = Vector3.Lerp(a, b, t1) - Vector3.up * (sag * 4f * t1 * (1f - t1));
                    pm.Beam(p0 + sideOffset, p1 + sideOffset, rail ? 0.25f : 0.09f, dark);
                    pm.Beam(p0 - sideOffset, p1 - sideOffset, rail ? 0.25f : 0.09f, dark);
                    if (_line.Count == 0) { _line.Add(p0); _lineDist.Add(0f); }
                    _lineLength += Vector3.Distance(p0, p1);
                    _line.Add(p1); _lineDist.Add(_lineLength);
                }
            }
            if (_staticFilter.sharedMesh != null) Destroy(_staticFilter.sharedMesh);
            _staticFilter.sharedMesh = pm.Build("lift_" + State.Id);
            _statusRenderer.transform.position = SurfaceAt(State.Bottom) + Vector3.up * (termH + 1.5f);
            _lastStatus = (LiftStatus)(-1);
        }

        private Vector3 SurfaceAt(Vec2 p) => _boot.SurfacePoint(p.X, p.Y);

        /// <summary>Position on the rope loop at a distance from the bottom terminal (0..2*length: up then back down).</summary>
        private Vector3 RopePoint(float dist, out Vector3 forward)
        {
            bool down = dist > _lineLength;
            float d = down ? 2f * _lineLength - dist : dist;
            d = Mathf.Clamp(d, 0f, _lineLength);
            int lo = 0, hi = _lineDist.Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (_lineDist[mid] <= d) lo = mid; else hi = mid; }
            float seg = Mathf.Max(0.001f, _lineDist[hi] - _lineDist[lo]);
            float t = (d - _lineDist[lo]) / seg;
            var p = Vector3.Lerp(_line[lo], _line[hi], t);
            forward = (_line[hi] - _line[lo]).normalized * (down ? -1f : 1f);
            var side = Vector3.Cross(Vector3.up, Bootstrap.ToUnity(State.Direction, 0f)).normalized * (Type.Family == LiftFamily.Surface ? 1.2f : 3.2f);
            return p + (down ? -side : side);
        }

        public void Present(float simSeconds)
        {
            if (State.Towers.Count != _lastTowerCount) Rebuild();
            if (State.Status != _lastStatus)
            {
                _lastStatus = State.Status;
                Color c;
                switch (State.Status)
                {
                    case LiftStatus.Open: c = new Color(0.3f, 0.9f, 0.3f); break;
                    case LiftStatus.WindHold: case LiftStatus.LightningHold: case LiftStatus.ColdHold: c = new Color(1f, 0.8f, 0.2f); break;
                    case LiftStatus.Breakdown: case LiftStatus.Evacuation: c = new Color(1f, 0.25f, 0.2f); break;
                    case LiftStatus.UnderConstruction: case LiftStatus.Planned: c = new Color(0.9f, 0.5f, 0.9f); break;
                    default: c = new Color(0.5f, 0.5f, 0.55f); break;
                }
                _statusMaterial.SetColor("_Color", c);
            }
            _staticRenderer.enabled = State.Status != LiftStatus.Planned;
            _carriers.Clear();
            if (!State.IsBuilt || _lineLength < 1f) return;
            bool reversible = Type.RopeConfiguration == RopeConfig.Reversible || Type.Family == LiftFamily.Rail;
            float speed = State.IsRunning ? Type.LineSpeedMs : 0f;
            _phase = speed * simSeconds;
            int carriers = Mathf.Max(reversible ? 2 : 2, State.Carriers);
            if (reversible)
            {
                float cycle = 2f * _lineLength / Mathf.Max(0.3f, Type.LineSpeedMs) + 2f * Type.LoadTimeS;
                float t = State.IsRunning ? Mathf.Repeat(simSeconds, cycle) / cycle : 0f;
                float travel = Mathf.Clamp01(t < 0.5f ? t * 2f : (1f - t) * 2f);
                var p1 = RopePoint(travel * _lineLength, out var f1);
                var p2 = RopePoint(2f * _lineLength - travel * _lineLength, out var f2);
                _carriers.Add(p1, Quaternion.LookRotation(f1), Vector3.one);
                _carriers.Add(p2, Quaternion.LookRotation(f2), Vector3.one);
            }
            else
            {
                float loop = 2f * _lineLength;
                float spacing = loop / carriers;
                for (int i = 0; i < carriers; i++)
                {
                    float dist = Mathf.Repeat(_phase + i * spacing, loop);
                    var p = RopePoint(dist, out var fwd);
                    _carriers.Add(p, Quaternion.LookRotation(fwd), Vector3.one);
                }
            }
            _carriers.Draw();
        }
    }
}
