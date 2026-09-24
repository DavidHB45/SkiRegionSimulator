using System.Collections.Generic;
using AlpineSim.Core.Vehicles;
using AlpineSim.Unity.Art;
using UnityEngine;

namespace AlpineSim.Unity.Vehicles
{
    /// <summary>
    /// One machine's GameObject: a generated model when the art pipeline has built one and procedural
    /// primitives when it has not, implement children posed from the sim state (lift, angle, engaged),
    /// headlights, and 20 Hz to frame interpolation of position and heading.
    /// Reads VehicleState; never writes it.
    /// </summary>
    public sealed class VehicleView : MonoBehaviour
    {
        public int VehicleId { get; private set; }
        public VehicleState State { get; private set; }
        public VehicleDef Def { get; private set; }
        public Transform CabAnchor { get; private set; }
        public Transform ChaseAnchor { get; private set; }

        /// <summary>The generated or authored model, or null when this machine is on the primitive tier.</summary>
        public GameObject Model { get; private set; }
        /// <summary>Chassis articulation, or null on the primitive tier.</summary>
        public ArticulationBinder Chassis { get; private set; }

        private Bootstrap _boot;
        private readonly Dictionary<SlotPosition, Transform> _implements = new Dictionary<SlotPosition, Transform>();
        private readonly Dictionary<SlotPosition, string> _implementIds = new Dictionary<SlotPosition, string>();
        private readonly Dictionary<SlotPosition, ArticulationBinder> _implementRigs = new Dictionary<SlotPosition, ArticulationBinder>();
        private readonly Dictionary<SlotPosition, Vector3> _implementRest = new Dictionary<SlotPosition, Vector3>();
        private ConditionVisuals _condition;
        private Light _lightL, _lightR;
        private Vector3 _prevPos, _curPos;
        private float _prevYaw, _curYaw, _prevPitch, _curPitch, _prevRoll, _curRoll;
        private bool _hasPrev;
        private float _bodyL, _bodyW, _bodyH, _trackH, _wheelR;
        private float _wheelAngle, _rotorAngle;

        // The transforms a chassis must publish, mirroring validate.REQUIRED_NODES in the asset
        // pipeline: the two agree or a model passes the build and fails at startup.
        private static readonly string[] TrackedRequired = { "track_L", "track_R" };
        private static readonly string[] ArticRequired = { "pivot_center" };
        private static readonly string[] NothingRequired = new string[0];
        private static readonly string[] ChassisOptional =
        {
            "cab", "exhaust", "turret", "bucket", "boom_01",
            "wheel_FL", "wheel_FR", "wheel_RL", "wheel_RR", "wheel_ML", "wheel_MR",
            "steer_FL", "steer_FR", "light_L", "light_R",
            "mount_Front", "mount_Rear", "mount_Mid", "mount_Roof", "mount_Tow",
        };
        private static readonly string[] ImplementOptional =
        {
            "blade_lift", "blade_angle_L", "blade_angle_R", "blade_tilt",
            "tiller_arm", "tiller_rotor", "finisher", "blower_impeller", "blower_chute",
            "spreader_disc", "winch_drum", "winch_boom", "fork_L", "fork_R",
        };

        public void Construct(Bootstrap boot, VehicleState state, VehicleDef def)
        {
            _boot = boot;
            State = state;
            Def = def;
            VehicleId = state.Id;
            name = "vehicle_" + state.Id + "_" + def.Id;
            var r = def.Visual ?? new MeshRecipe();
            _bodyL = r.BodyL; _bodyW = r.BodyW; _bodyH = r.BodyH; _trackH = r.TrackH; _wheelR = r.WheelRadiusM;
            gameObject.layer = LayerMask.NameToLayer("Vehicles") >= 0 ? LayerMask.NameToLayer("Vehicles") : 0;

            ModelRegistry.Configure(boot.Data.Render);
            Model = ModelRegistry.Machine(def, transform);
            if (Model != null)
            {
                Chassis = Model.GetComponent<ArticulationBinder>();
                if (Chassis != null) Chassis.Bind(RequiredFor(def.ChassisType), ChassisOptional);
                _condition = gameObject.AddComponent<ConditionVisuals>();
                if (_condition != null) _condition.Bind(state, boot.Data.Render, Chassis);
            }
            else
            {
                var chassis = new GameObject("chassis");
                chassis.transform.SetParent(transform, false);
                var mf = chassis.AddComponent<MeshFilter>();
                mf.sharedMesh = VehicleMeshBuilder.BuildChassis(def);
                var mr = chassis.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ProceduralMesh.VertexMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }

            float cabBase = (def.ChassisType == ChassisType.Tracked ? r.TrackH : r.WheelRadiusM) + r.BodyH;
            CabAnchor = new GameObject("cab_anchor").transform;
            CabAnchor.SetParent(transform, false);
            CabAnchor.localPosition = new Vector3(0f, cabBase + r.CabH * 0.75f, r.CabOffset + r.CabL * 0.1f);
            ChaseAnchor = new GameObject("chase").transform;
            ChaseAnchor.SetParent(transform, false);
            ChaseAnchor.localPosition = new Vector3(0f, cabBase + r.CabH, 0f);

            if (!def.IsStationary)
            {
                _lightL = MakeLight("light_L", new Vector3(-r.CabW * 0.4f, cabBase + r.CabH * 0.9f, r.CabOffset + r.CabL * 0.5f));
                _lightR = MakeLight("light_R", new Vector3(r.CabW * 0.4f, cabBase + r.CabH * 0.9f, r.CabOffset + r.CabL * 0.5f));
            }
            SyncImplements();
            SnapToState();
        }

        private static string[] RequiredFor(ChassisType chassis)
        {
            switch (chassis)
            {
                case ChassisType.Tracked: return TrackedRequired;
                case ChassisType.Artic: return ArticRequired;
                default: return NothingRequired;
            }
        }

        /// <summary>A headlight on the model's anchor when it publishes one, otherwise on the recipe's cab corner.</summary>
        private Light MakeLight(string anchorName, Vector3 local)
        {
            var go = new GameObject("headlight");
            if (Chassis != null && Chassis.TryGet(anchorName, out var anchor))
            {
                go.transform.SetParent(anchor, false);
                go.transform.localPosition = Vector3.zero;
            }
            else
            {
                go.transform.SetParent(transform, false);
                go.transform.localPosition = local;
            }
            go.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            var l = go.AddComponent<Light>();
            if (l == null) return null;
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
                if (_implements.TryGetValue(m.Slot, out var old)) { Destroy(old.gameObject); Forget(m.Slot); }
                var att = _boot.Data.Attachment(m.DefId);
                if (att == null) continue;
                var root = new GameObject("impl_" + m.Slot + "_" + att.Id).transform;

                // An implement's origin is its mount point, so a model hangs correctly off the socket
                // the chassis publishes; without a socket it goes where the mesh recipe says.
                Transform socket = MountSocket(m.Slot);
                root.SetParent(socket != null ? socket : transform, false);
                root.localPosition = socket != null ? Vector3.zero : MountPoint(m.Slot);
                _implementRest[m.Slot] = root.localPosition;

                var model = ModelRegistry.Attachment(att, root);
                if (model != null)
                {
                    var rig = model.GetComponent<ArticulationBinder>();
                    if (rig != null) { rig.Bind(null, ImplementOptional); _implementRigs[m.Slot] = rig; }
                }
                else
                {
                    var meshGo = new GameObject("mesh");
                    meshGo.transform.SetParent(root, false);
                    meshGo.AddComponent<MeshFilter>().sharedMesh = VehicleMeshBuilder.BuildAttachment(att, m.Slot);
                    var mr = meshGo.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = ProceduralMesh.VertexMaterial;
                }
                _implements[m.Slot] = root;
                _implementIds[m.Slot] = m.DefId;
            }
            var remove = new List<SlotPosition>();
            foreach (var kv in _implements) if (!seen.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var slot in remove) { Destroy(_implements[slot].gameObject); Forget(slot); }
            if (_condition != null) _condition.Refresh();
        }

        private void Forget(SlotPosition slot)
        {
            _implements.Remove(slot);
            _implementIds.Remove(slot);
            _implementRigs.Remove(slot);
            _implementRest.Remove(slot);
        }

        private Transform MountSocket(SlotPosition slot)
        {
            if (Chassis == null) return null;
            return Chassis.TryGet("mount_" + slot, out var socket) ? socket : null;
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

        /// <summary>Called after each batch of sim ticks: shift current to previous and read the new pose.</summary>
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

            PoseChassis();
            foreach (var m in State.Mounted)
            {
                if (!_implements.TryGetValue(m.Slot, out var root)) continue;
                if (_implementRigs.TryGetValue(m.Slot, out var rig) && rig != null) PoseImplement(m, rig);
                else PoseImplementRoot(m, root);
            }
            bool lights = State.LightsOn && State.EngineOn;
            if (_lightL != null) _lightL.enabled = lights;
            if (_lightR != null) _lightR.enabled = lights;
        }

        /// <summary>Wheels roll and knuckles steer on a model that publishes them; the belts scroll in ConditionVisuals.</summary>
        private void PoseChassis()
        {
            if (Chassis == null) return;
            float radius = Mathf.Max(0.15f, _wheelR);
            _wheelAngle = Mathf.Repeat(_wheelAngle + State.Speed / radius * Mathf.Rad2Deg * Time.deltaTime, 360f);
            var roll = Quaternion.Euler(_wheelAngle, 0f, 0f);
            Roll("wheel_FL", roll); Roll("wheel_FR", roll); Roll("wheel_RL", roll);
            Roll("wheel_RR", roll); Roll("wheel_ML", roll); Roll("wheel_MR", roll);

            // The sim gives a steering command, not a road-wheel angle, so the view scales it to a
            // plausible lock; nothing in the simulation depends on what the knuckles look like.
            float steer = Mathf.Clamp(State.Input != null ? State.Input.Steer : 0f, -1f, 1f) * 32f;
            var knuckle = Quaternion.Euler(0f, steer, 0f);
            if (Chassis.TryGet("steer_FL", out var sl)) sl.localRotation = knuckle;
            if (Chassis.TryGet("steer_FR", out var sr)) sr.localRotation = knuckle;
            if (Chassis.TryGet("pivot_center", out var pivot)) pivot.localRotation = Quaternion.Euler(0f, steer * 0.7f, 0f);
        }

        private void Roll(string boneName, Quaternion rotation)
        {
            if (Chassis.TryGet(boneName, out var bone)) bone.localRotation = rotation;
        }

        /// <summary>
        /// Pose an implement through the transforms docs/ART_CONTRACT.md section 3 names. The pivots are
        /// where the real cylinders anchor, so the mouldboard only rotates: the ad-hoc lift-and-tilt of
        /// the primitive tier exists because a box has nowhere to hinge.
        /// </summary>
        private void PoseImplement(MountedAttachment m, ArticulationBinder rig)
        {
            float lift = Mathf.Clamp01(m.Lift);
            // A front implement hangs off a pivot ahead of the machine and a rear one behind it, so the
            // same raise is an opposite rotation about X.
            float sign = m.Slot == SlotPosition.Rear ? 1f : -1f;
            if (rig.TryGet("blade_lift", out var bladeLift)) bladeLift.localRotation = Quaternion.Euler(sign * lift * 26f, 0f, 0f);
            float angleDeg = -m.Angle * Mathf.Rad2Deg;
            if (rig.TryGet("blade_angle_L", out var wingL)) wingL.localRotation = Quaternion.Euler(0f, angleDeg, 0f);
            if (rig.TryGet("blade_angle_R", out var wingR)) wingR.localRotation = Quaternion.Euler(0f, angleDeg, 0f);
            if (rig.TryGet("blade_tilt", out var tilt)) tilt.localRotation = Quaternion.Euler(0f, 0f, m.Tilt * Mathf.Rad2Deg);
            if (rig.TryGet("tiller_arm", out var arm)) arm.localRotation = Quaternion.Euler(lift * 22f, 0f, 0f);
            if (rig.TryGet("finisher", out var comb)) comb.localRotation = Quaternion.Euler(lift * 12f, 0f, 0f);

            bool spinning = m.Engaged && State.EngineOn;
            if (spinning) _rotorAngle = Mathf.Repeat(_rotorAngle + 540f * Time.deltaTime, 360f);
            if (rig.TryGet("tiller_rotor", out var rotor)) rotor.localRotation = Quaternion.Euler(_rotorAngle, 0f, 0f);
            if (rig.TryGet("blower_impeller", out var impeller)) impeller.localRotation = Quaternion.Euler(0f, 0f, _rotorAngle);
            if (rig.TryGet("spreader_disc", out var disc)) disc.localRotation = Quaternion.Euler(0f, _rotorAngle, 0f);
        }

        /// <summary>The primitive tier's posing: the whole implement lifts and tips, because it has no hinges.</summary>
        private void PoseImplementRoot(MountedAttachment m, Transform root)
        {
            float lift = m.Slot == SlotPosition.Rear || m.Slot == SlotPosition.Front || m.Slot == SlotPosition.Mid ? m.Lift : 0f;
            Vector3 basePos = _implementRest.TryGetValue(m.Slot, out var rest) ? rest : MountPoint(m.Slot);
            root.localPosition = basePos + new Vector3(0f, lift * 0.7f, 0f);
            float angle = -m.Angle * Mathf.Rad2Deg;
            float tilt = m.Tilt * Mathf.Rad2Deg;
            root.localRotation = Quaternion.Euler(m.Slot == SlotPosition.Rear ? -lift * 25f : lift * 15f, angle, tilt);
        }
    }
}
