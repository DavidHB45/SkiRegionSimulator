using System.Collections.Generic;
using AlpineSim.Core.Lifts;
using AlpineSim.Core.Math;
using AlpineSim.Unity.Art;
using AlpineSim.Unity.Guests;
using AlpineSim.Unity.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity.Lifts
{
    /// <summary>
    /// One lift's visuals: towers, terminals and a sagging haul rope built once from the sim's tower
    /// positions, plus carriers animated from sim time while the lift runs.
    /// <para>
    /// Towers and terminals come from the art pipeline when it has built them and from primitives when
    /// it has not, and the two mix freely: a line whose terminals are generated and whose towers are not
    /// still runs. The rope is always procedural - it is a curve through the towers, not an asset.
    /// </para>
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
        private Transform _models;                                    // generated towers, terminals and barn
        private readonly List<Transform> _carrierObjects = new List<Transform>();
        private readonly List<Matrix4x4> _carrierMatrices = new List<Matrix4x4>();
        private Matrix4x4[] _carrierBatch;
        private Mesh _carrierProxyMesh;
        private Material[] _carrierProxyMaterials;
        private bool _carrierModels;                                  // generated carriers as GameObjects
        private Vector3 _carrierGrip;                                 // where the model hangs from the rope
        private readonly List<Vector3> _line = new List<Vector3>();   // sampled rope polyline (up-line), world space
        private readonly List<float> _lineDist = new List<float>();
        private float _lineLength;
        private float _towerHeight;
        private LiftStatus _lastStatus = LiftStatus.Planned;
        private int _lastTowerCount = -1;
        private float _phase;

        // A carrier's origin is the bottom of the cabin, like every other model's, so it is hung from
        // whichever of these it publishes: the grip is the real rope attachment, the hanger arm is the
        // next best thing, and a magic carpet's belt has neither because it never leaves the ground.
        private static readonly string[] CarrierHang = { "grip_arm", "cabin_hanger" };

        // Steelwork is galvanised, not painted, so towers and terminals take the same near-white tint
        // and only the carriers wear the family's colour.
        private static readonly Color SteelLivery = new Color(0.74f, 0.76f, 0.78f, 1f);
        private static readonly Color SteelAccent = new Color(0.28f, 0.30f, 0.33f, 1f);

        public void Construct(Bootstrap boot, LiftState state, LiftTypeDef type)
        {
            _boot = boot;
            State = state;
            Type = type;
            LiftId = state.Id;
            name = "lift_" + state.Id + "_" + state.TypeId;
            ModelRegistry.Configure(boot.Data.Render);
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
            if (_models != null) Destroy(_models.gameObject);
            _models = new GameObject("models").transform;
            _models.SetParent(transform, false);
            _carrierObjects.Clear();
            _carrierModels = false;
            _carrierGrip = Vector3.zero;
            _carrierProxyMesh = null;
            _carrierProxyMaterials = null;

            var pm = new ProceduralMesh();
            Color32 steel = new Color32(120, 128, 136, 255);
            Color32 dark = new Color32(60, 64, 70, 255);
            Color32 terminal = new Color32(150, 150, 160, 255);
            bool rail = Type.Family == LiftFamily.Rail;
            var dir = Bootstrap.ToUnity(State.Direction, 0f);
            var rot = Quaternion.LookRotation(dir);
            var side = Vector3.Cross(Vector3.up, dir).normalized;

            var bottom = SurfaceAt(State.Bottom);
            var top = SurfaceAt(State.Top);
            var points = new List<Vector3>();
            points.Add(bottom + Vector3.up * (rail ? 0.3f : _towerHeight * 0.6f));
            foreach (var t in State.Towers)
            {
                var basePos = SurfaceAt(t);
                var topPos = basePos + Vector3.up * _towerHeight;
                if (rail) { pm.Box(basePos + Vector3.up * 0.15f, new Vector3(3f, 0.3f, 3f), dark); points.Add(basePos + Vector3.up * 0.3f); continue; }

                var tower = ModelRegistry.LiftComponent(Type, TowerClassFor(basePos, bottom, top), _models, SteelLivery, SteelAccent);
                if (tower != null)
                {
                    tower.transform.SetPositionAndRotation(basePos, rot);
                    // The rope rides the sheave train, so the model decides how high this span runs.
                    var rig = tower.GetComponent<ArticulationBinder>();
                    if (rig != null && rig.TryGet("sheave_01", out var sheave))
                        topPos = new Vector3(basePos.x, sheave.position.y, basePos.z);
                    points.Add(topPos);
                    continue;
                }

                pm.Cylinder(basePos + Vector3.up * _towerHeight * 0.5f, Type.Family == LiftFamily.Aerial ? 1.2f : 0.45f, _towerHeight, 1, 8, steel);
                pm.Box(basePos + Vector3.up * 0.4f, new Vector3(2.4f, 0.8f, 2.4f), dark);
                // cross arm with sheave assemblies on both sides
                float arm = Type.Family == LiftFamily.Surface ? 1.2f : 3.2f;
                pm.Box(topPos, new Vector3(0.3f, 0.3f, 0.3f), steel);
                pm.Beam(topPos - side * arm, topPos + side * arm, 0.25f, steel);
                pm.Box(topPos - side * arm, new Vector3(0.6f, 0.4f, 1.6f), rot, dark);
                pm.Box(topPos + side * arm, new Vector3(0.6f, 0.4f, 1.6f), rot, dark);
                points.Add(topPos);
            }
            points.Add(top + Vector3.up * (rail ? 0.3f : _towerHeight * 0.6f));

            // Terminals. The bottom end drives: that is where lifts.json puts the motor room, and where
            // the barn spur runs off for types that garage their cabins overnight.
            float termL = Type.Family == LiftFamily.Surface ? 4f : (Type.Grip == GripType.Detachable || Type.IsEnclosed ? 22f : 10f);
            float termW = Type.Family == LiftFamily.Surface ? 2f : 8f;
            float termH = Type.Family == LiftFamily.Surface ? 3f : _towerHeight * 0.7f;
            var drive = ModelRegistry.LiftComponent(Type, LiftPart.TerminalDrive, _models, SteelLivery, SteelAccent);
            if (drive != null) drive.transform.SetPositionAndRotation(bottom, rot);
            else pm.Box(bottom + Vector3.up * termH * 0.5f, new Vector3(termW, termH, termL), rot, terminal);
            var ret = ModelRegistry.LiftComponent(Type, LiftPart.TerminalReturn, _models, SteelLivery, SteelAccent);
            if (ret != null) ret.transform.SetPositionAndRotation(top, rot);
            else pm.Box(top + Vector3.up * termH * 0.5f, new Vector3(termW, termH, termL), rot, terminal);

            if (Type.CabinBarnCapex > 0.0)
            {
                var barn = ModelRegistry.LiftComponent(Type, LiftPart.Barn, _models, SteelLivery, SteelAccent);
                if (barn != null) barn.transform.SetPositionAndRotation(SurfaceAt(State.Bottom) + side * (termW + 8f), rot);
            }

            // haul rope: parabolic sag between consecutive support points, both directions
            _line.Clear(); _lineDist.Clear(); _lineLength = 0f;
            var sideOffset = side * (Type.Family == LiftFamily.Surface ? 1.2f : 3.2f);
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
            _statusRenderer.transform.position = bottom + Vector3.up * (termH + 1.5f);
            _lastStatus = (LiftStatus)(-1);
        }

        /// <summary>
        /// Which of the three tower height classes this ground wants. A line crosses a roll that needs a
        /// short tower and a dip that needs a tall one, so the class is chosen by how far the rope runs
        /// above the ground here, not by the lift type alone.
        /// </summary>
        private LiftPart TowerClassFor(Vector3 basePos, Vector3 bottom, Vector3 top)
        {
            float total = Vector3.Distance(new Vector3(bottom.x, 0f, bottom.z), new Vector3(top.x, 0f, top.z));
            float along = total > 0.5f ? Vector3.Distance(new Vector3(bottom.x, 0f, bottom.z), new Vector3(basePos.x, 0f, basePos.z)) / total : 0f;
            float ropeY = Mathf.Lerp(bottom.y + _towerHeight, top.y + _towerHeight, Mathf.Clamp01(along));
            float needed = ropeY - basePos.y;
            if (needed < _towerHeight * 0.78f) return LiftPart.TowerLow;
            if (needed > _towerHeight * 1.22f) return LiftPart.TowerHigh;
            return LiftPart.Tower;
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
            if (_models != null) _models.gameObject.SetActive(State.Status != LiftStatus.Planned);
            _carriers.Clear();
            _carrierMatrices.Clear();
            if (!State.IsBuilt || _lineLength < 1f) { HideCarrierObjects(0); return; }

            bool reversible = Type.RopeConfiguration == RopeConfig.Reversible || Type.Family == LiftFamily.Rail;
            float speed = State.IsRunning ? Type.LineSpeedMs : 0f;
            _phase = speed * simSeconds;
            int carriers = Mathf.Max(2, State.Carriers);
            PrepareCarriers(carriers);
            int placed = 0;
            if (reversible)
            {
                float cycle = 2f * _lineLength / Mathf.Max(0.3f, Type.LineSpeedMs) + 2f * Type.LoadTimeS;
                float t = State.IsRunning ? Mathf.Repeat(simSeconds, cycle) / cycle : 0f;
                float travel = Mathf.Clamp01(t < 0.5f ? t * 2f : (1f - t) * 2f);
                var p1 = RopePoint(travel * _lineLength, out var f1);
                var p2 = RopePoint(2f * _lineLength - travel * _lineLength, out var f2);
                PlaceCarrier(placed++, p1, Quaternion.LookRotation(f1));
                PlaceCarrier(placed++, p2, Quaternion.LookRotation(f2));
            }
            else
            {
                float loop = 2f * _lineLength;
                float spacing = loop / carriers;
                for (int i = 0; i < carriers; i++)
                {
                    float dist = Mathf.Repeat(_phase + i * spacing, loop);
                    var p = RopePoint(dist, out var fwd);
                    PlaceCarrier(placed++, p, Quaternion.LookRotation(fwd));
                }
            }
            HideCarrierObjects(placed);
            if (_carrierProxyMesh != null) DrawCarrierProxies();
            else if (!_carrierModels) _carriers.Draw();
        }

        /// <summary>
        /// Decide once per frame how this lift's carriers are drawn: a handful of full models, hundreds
        /// of copies of the merged proxy mesh, or the primitive carrier when the pipeline has built
        /// nothing. A gondola line carries more carriers than is worth a GameObject each.
        /// </summary>
        private void PrepareCarriers(int count)
        {
            if (_carrierModels)
            {
                EnsureCarrierObjects(count);
                return;
            }
            if (_carrierProxyMesh != null) return;

            var livery = CarrierColor(Type);
            if (!ModelRegistry.HasLiftComponent(Type, LiftPart.Carrier, livery, SteelAccent)) return;
            _carrierGrip = ModelRegistry.LiftPartAnchor(Type, LiftPart.Carrier, CarrierHang, livery, SteelAccent);
            if (count <= _boot.Data.Render.MaxGeneratedCarrierObjects)
            {
                _carrierModels = true;
                EnsureCarrierObjects(count);
                return;
            }
            if (ModelRegistry.TryGetProxy(Type, LiftPart.Carrier, livery, SteelAccent, out var mesh, out var materials))
            {
                _carrierProxyMesh = mesh;
                _carrierProxyMaterials = materials;
            }
        }

        private void EnsureCarrierObjects(int count)
        {
            var livery = CarrierColor(Type);
            while (_carrierObjects.Count < count)
            {
                var go = ModelRegistry.LiftComponent(Type, LiftPart.Carrier, _models, livery, SteelAccent);
                if (go == null) { _carrierModels = false; HideCarrierObjects(0); return; }
                _carrierObjects.Add(go.transform);
            }
        }

        private void PlaceCarrier(int index, Vector3 pos, Quaternion rot)
        {
            if (_carrierModels && index < _carrierObjects.Count)
            {
                var t = _carrierObjects[index];
                if (t == null) return;
                t.gameObject.SetActive(true);
                t.SetPositionAndRotation(pos - rot * _carrierGrip, rot);
                return;
            }
            if (_carrierProxyMesh != null) { _carrierMatrices.Add(Matrix4x4.TRS(pos - rot * _carrierGrip, rot, Vector3.one)); return; }
            _carriers.Add(pos, rot, Vector3.one);
        }

        private void HideCarrierObjects(int from)
        {
            for (int i = from; i < _carrierObjects.Count; i++)
                if (_carrierObjects[i] != null) _carrierObjects[i].gameObject.SetActive(false);
        }

        /// <summary>Draw the merged carrier proxy once per submesh, in batches of the instancing limit.</summary>
        private void DrawCarrierProxies()
        {
            if (_carrierMatrices.Count == 0 || _carrierProxyMaterials == null) return;
            if (_carrierBatch == null) _carrierBatch = new Matrix4x4[1023];
            int submeshes = Mathf.Max(1, _carrierProxyMesh.subMeshCount);
            for (int start = 0; start < _carrierMatrices.Count; start += _carrierBatch.Length)
            {
                int n = Mathf.Min(_carrierBatch.Length, _carrierMatrices.Count - start);
                for (int i = 0; i < n; i++) _carrierBatch[i] = _carrierMatrices[start + i];
                for (int s = 0; s < submeshes; s++)
                {
                    var material = _carrierProxyMaterials[Mathf.Min(s, _carrierProxyMaterials.Length - 1)];
                    if (material == null) continue;
                    Graphics.DrawMeshInstanced(_carrierProxyMesh, s, material, _carrierBatch, n);
                }
            }
        }
    }
}
