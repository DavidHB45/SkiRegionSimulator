using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity.Art
{
    /// <summary>
    /// Drives every per-instance shader parameter of one machine from its sim state: how worn it looks,
    /// how filthy, and how fast its belts are running.
    /// <para>
    /// Wear is the point of the exercise. The machine's condition is a number the simulation already
    /// tracks, and feeding it to <c>_WearAmount</c> is what makes a 40 percent machine visibly a
    /// 40 percent machine instead of a fact hidden in the fleet screen. It moves toward its target over
    /// a few seconds rather than snapping, so a rebuild reads as a machine coming back clean rather than
    /// as a pop between two frames.
    /// </para>
    /// <para>
    /// Soiling has no sim variable of its own - there is no per-machine wash timestamp in the save, only
    /// a workshop-wide wash bay - so it follows hours since the last service, which is the nearest thing
    /// the simulation tracks to "how long since anyone looked after this".
    /// </para>
    /// <para>
    /// Everything goes through MaterialPropertyBlocks: the material set is shared by every machine of a
    /// class, and writing wear into the material itself would age the whole class at once.
    /// </para>
    /// </summary>
    public sealed class ConditionVisuals : MonoBehaviour
    {
        private VehicleState _state;
        private RenderData _render;
        private ArticulationBinder _chassis;
        private readonly List<Renderer> _body = new List<Renderer>();
        private readonly List<Renderer> _belts = new List<Renderer>();
        private MaterialPropertyBlock _bodyBlock;
        private MaterialPropertyBlock _beltBlock;
        private float _wear, _soil, _scroll;
        private float _appliedWear = -1f, _appliedSoil = -1f;

        /// <summary>0 = factory fresh, 1 = worn out.</summary>
        public float Wear => _wear;
        /// <summary>0 = washed, 1 = a season of salt.</summary>
        public float Soiling => _soil;

        public void Bind(VehicleState state, RenderData render, ArticulationBinder chassis)
        {
            _state = state;
            _render = render ?? new RenderData();
            _chassis = chassis;
            _bodyBlock = new MaterialPropertyBlock();
            _beltBlock = new MaterialPropertyBlock();
            Refresh();
            _wear = TargetWear();
            _soil = TargetSoiling();
            Apply();
        }

        /// <summary>Re-collect renderers. Called when an implement is mounted or dropped.</summary>
        public void Refresh()
        {
            _body.Clear();
            _belts.Clear();
            var all = GetComponentsInChildren<Renderer>(true);
            var belts = new HashSet<Renderer>();
            if (_chassis != null)
            {
                CollectBelt(belts, "track_L");
                CollectBelt(belts, "track_R");
                CollectBelt(belts, "carpet_belt");
            }
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null) continue;
                if (belts.Contains(r)) _belts.Add(r); else _body.Add(r);
            }
            _appliedWear = -1f;
        }

        private void CollectBelt(HashSet<Renderer> into, string boneName)
        {
            if (!_chassis.TryGet(boneName, out var bone)) return;
            var rends = bone.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++) if (rends[i] != null) into.Add(rends[i]);
        }

        private void LateUpdate()
        {
            if (_state == null) return;
            float rate = Time.deltaTime / Mathf.Max(0.05f, _render.WearBlendSeconds);
            _wear = Mathf.MoveTowards(_wear, TargetWear(), rate);
            _soil = Mathf.MoveTowards(_soil, TargetSoiling(), rate);

            // Belt scroll is distance travelled, not speed: the texture has to keep the ground it has
            // already covered, or the tracks jump every time the machine changes pace.
            bool moving = _belts.Count > 0 && Mathf.Abs(_state.Speed) > 0.01f;
            if (moving) _scroll = Mathf.Repeat(_scroll + _state.Speed * Time.deltaTime * Mathf.Max(0f, _render.BeltUvPerMetre), 1f);

            if (!moving && Mathf.Abs(_wear - _appliedWear) < 0.002f && Mathf.Abs(_soil - _appliedSoil) < 0.002f) return;
            Apply();
        }

        private void Apply()
        {
            _appliedWear = _wear;
            _appliedSoil = _soil;
            _bodyBlock.SetFloat("_WearAmount", _wear);
            _bodyBlock.SetFloat("_SoilAmount", _soil);
            for (int i = 0; i < _body.Count; i++) if (_body[i] != null) _body[i].SetPropertyBlock(_bodyBlock);
            if (_belts.Count == 0) return;
            _beltBlock.SetFloat("_WearAmount", _wear);
            _beltBlock.SetFloat("_SoilAmount", _soil);
            _beltBlock.SetVector("_UvScroll", new Vector4(0f, _scroll, 0f, 0f));
            for (int i = 0; i < _belts.Count; i++) if (_belts[i] != null) _belts[i].SetPropertyBlock(_beltBlock);
        }

        private float TargetWear()
        {
            float condition = _state.Condition != null ? _state.Condition.ConditionPct : 100f;
            return Mathf.InverseLerp(_render.WearStartConditionPct, _render.WearFullConditionPct, condition);
        }

        private float TargetSoiling()
        {
            float hours = _state.Condition != null ? _state.Condition.HoursSinceService : 0f;
            return Mathf.Clamp01(hours / Mathf.Max(1f, _render.SoilingHoursForFull));
        }
    }
}
