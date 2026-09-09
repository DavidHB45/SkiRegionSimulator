using System.Collections.Generic;
using AlpineSim.Core.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity.Vehicles
{
    /// <summary>
    /// One machine's GameObject: procedural chassis, implement children posed from the sim state
    /// (lift, angle, engaged), headlights, and 20 Hz → frame interpolation of position and heading.
    /// Reads VehicleState; never writes it.
    /// </summary>
    public sealed class VehicleView : MonoBehaviour
    {
        public int VehicleId { get; private set; }
        public VehicleState State { get; private set; }
        public VehicleDef Def { get; private set; }
        public Transform CabAnchor { get; private set; }
        public Transform ChaseAnchor { get; private set; }

        private Bootstrap _boot;
        private readonly Dictionary<SlotPosition, Transform> _implements = new Dictionary<SlotPosition, Transform>();
        private readonly Dictionary<SlotPosition, string> _implementIds = new Dictionary<SlotPosition, string>();
        private Light _lightL, _lightR;
        private Vector3 _prevPos, _curPos;
        private float _prevYaw, _curYaw, _prevPitch, _curPitch, _prevRoll, _curRoll;
        private bool _hasPrev;
        private float _bodyL, _bodyW, _bodyH, _trackH;

        public void Construct(Bootstrap boot, VehicleState state, VehicleDef def)
        {
            _boot = boot;
            State = state;
            Def = def;
            VehicleId = state.Id;
            name = "vehicle_" + state.Id + "_" + def.Id;
            var r = def.Visual ?? new MeshRecipe();
            _bodyL = r.BodyL; _bodyW = r.BodyW; _bodyH = r.BodyH; _trackH = r.TrackH;
            gameObject.layer = LayerMask.NameToLayer("Vehicles") >= 0 ? LayerMask.NameToLayer("Vehicles") : 0;

            var chassis = new GameObject("chassis");
            chassis.transform.SetParent(transform, false);
            var mf = chassis.AddComponent<MeshFilter>();
            mf.sharedMesh = VehicleMeshBuilder.BuildChassis(def);
            var mr = chassis.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ProceduralMesh.VertexMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            float cabBase = (def.ChassisType == ChassisType.Tracked ? r.TrackH : r.WheelRadiusM) + r.BodyH;
            CabAnchor = new GameObject("cab").transform;
            CabAnchor.SetParent(transform, false);
            CabAnchor.localPosition = new Vector3(0f, cabBase + r.CabH * 0.75f, r.CabOffset + r.CabL * 0.1f);
            ChaseAnchor = new GameObject("chase").transform;
            ChaseAnchor.SetParent(transform, false);
            ChaseAnchor.localPosition = new Vector3(0f, cabBase + r.CabH, 0f);

            if (!def.IsStationary)
            {
                _lightL = MakeLight(new Vector3(-r.CabW * 0.4f, cabBase + r.CabH * 0.9f, r.CabOffset + r.CabL * 0.5f));
                _lightR = MakeLight(new Vector3(r.CabW * 0.4f, cabBase + r.CabH * 0.9f, r.CabOffset + r.CabL * 0.5f));
            }
            SyncImplements();
            SnapToState();
        }

        private Light MakeLight(Vector3 local)
        {
            var go = new GameObject("headlight");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.spotAngle = 70f;
            l.range = Mathf.Clamp(Def.LightingLumens / 250f, 20f, 90f);
            l.intensity = Mathf.Clamp(Def.LightingLumens / 4000f, 1f, 6f);
            l.color = new Color(1f, 0.95f, 0.85f);
            l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        /// <summary>Re-creates implement children when the mounted set changed.</summary>
        public void SyncImplements()
        {
            var seen = new List<SlotPosition>();
            foreach (var m in State.Mounted)
            {
                seen.Add(m.Slot);
                if (_implementIds.TryGetValue(m.Slot, out var id) && id == m.DefId) continue;
                if (_implements.TryGetValue(m.Slot, out var old)) { Destroy(old.gameObject); _implements.Remove(m.Slot); _implementIds.Remove(m.Slot); }
                var att = _boot.Data.Attachment(m.DefId);
                if (att == null) continue;
                var root = new GameObject("impl_" + m.Slot + "_" + att.Id).transform;
                root.SetParent(transform, false);
                root.localPosition = MountPoint(m.Slot);
                var meshGo = new GameObject("mesh");
                meshGo.transform.SetParent(root, false);
                meshGo.AddComponent<MeshFilter>().sharedMesh = VehicleMeshBuilder.BuildAttachment(att, m.Slot);
                var mr = meshGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ProceduralMesh.VertexMaterial;
                _implements[m.Slot] = root;
                _implementIds[m.Slot] = m.DefId;
            }
            var remove = new List<SlotPosition>();
            foreach (var kv in _implements) if (!seen.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var slot in remove) { Destroy(_implements[slot].gameObject); _implements.Remove(slot); _implementIds.Remove(slot); }
        }

        private Vector3 MountPoint(SlotPosition slot)
        {
            float ground = Def.ChassisType == ChassisType.Tracked ? _trackH * 0.5f : 0.3f;
            switch (slot)
            {
                case SlotPosition.Front: return new Vector3(0f, ground, _bodyL * 0.5f);
                case SlotPosition.Rear: return new Vector3(0f, ground, -_bodyL * 0.5f);
                case SlotPosition.Roof: return new Vector3(0f, _trackH + _bodyH + 0.2f, -_bodyL * 0.2f);
                case SlotPosition.Tow: return new Vector3(0f, 0.4f, -_bodyL * 0.55f);
                default: return new Vector3(0f, ground, 0f);
            }
        }

        /// <summary>Called after each batch of sim ticks: shift current → previous and read the new pose.</summary>
        public void OnSimTicked()
        {
            _prevPos = _curPos; _prevYaw = _curYaw; _prevPitch = _curPitch; _prevRoll = _curRoll;
            ReadPose(out _curPos, out _curYaw, out _curPitch, out _curRoll);
            if (!_hasPrev) { _prevPos = _curPos; _prevYaw = _curYaw; _prevPitch = _curPitch; _prevRoll = _curRoll; _hasPrev = true; }
        }

        public void SnapToState()
        {
            ReadPose(out _curPos, out _curYaw, out _curPitch, out _curRoll);
            _prevPos = _curPos; _prevYaw = _curYaw; _prevPitch = _curPitch; _prevRoll = _curRoll;
            _hasPrev = true;
            transform.position = _curPos;
            transform.rotation = Quaternion.Euler(_curPitch, _curYaw, _curRoll);
        }

        private void ReadPose(out Vector3 pos, out float yaw, out float pitch, out float roll)
        {
            var sim = _boot.Sim;
            float h = sim.Terrain.SampleHeight(State.Pos) + sim.World.Snow.DepthAtMm(State.Pos) * 0.001f - State.Sinkage;
            pos = new Vector3(State.Pos.X, h, State.Pos.Y);
            yaw = 90f - State.Heading * Mathf.Rad2Deg;
            pitch = State.PitchDeg;
            roll = State.RollDeg;
        }

        /// <summary>Per frame: interpolate and pose implements.</summary>
        public void Present(float alpha)
        {
            transform.position = Vector3.Lerp(_prevPos, _curPos, alpha);
            float yaw = Mathf.LerpAngle(_prevYaw, _curYaw, alpha);
            float pitch = Mathf.Lerp(_prevPitch, _curPitch, alpha);
            float roll = Mathf.Lerp(_prevRoll, _curRoll, alpha);
            transform.rotation = Quaternion.Euler(pitch, yaw, roll);
            foreach (var m in State.Mounted)
            {
                if (!_implements.TryGetValue(m.Slot, out var root)) continue;
                float lift = m.Slot == SlotPosition.Rear || m.Slot == SlotPosition.Front || m.Slot == SlotPosition.Mid ? m.Lift : 0f;
                Vector3 basePos = MountPoint(m.Slot);
                root.localPosition = basePos + new Vector3(0f, lift * 0.7f, 0f);
                float angle = -m.Angle * Mathf.Rad2Deg;
                float tilt = m.Tilt * Mathf.Rad2Deg;
                root.localRotation = Quaternion.Euler(m.Slot == SlotPosition.Rear ? -lift * 25f : lift * 15f, angle, tilt);
            }
            bool lights = State.LightsOn && State.EngineOn;
            if (_lightL != null) _lightL.enabled = lights;
            if (_lightR != null) _lightR.enabled = lights;
        }
    }
}
