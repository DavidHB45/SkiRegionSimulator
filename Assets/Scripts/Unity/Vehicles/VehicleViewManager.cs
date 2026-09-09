using System;
using System.Collections.Generic;
using System.Text;
using AlpineSim.Core.Math;
using AlpineSim.Core.Tasks;
using AlpineSim.Core.Vehicles;
using AlpineSim.Unity.Cameras;
using AlpineSim.Unity.UI;
using UnityEngine;

namespace AlpineSim.Unity.Vehicles
{
    /// <summary>
    /// Keeps a VehicleView per machine, feeds the player's controls into the sim before each batch
    /// of ticks, handles enter/leave, and contributes the vehicle HUD lines.
    /// </summary>
    public sealed class VehicleViewManager : MonoBehaviour
    {
        private Bootstrap _boot;
        private VehicleSystem _vs;
        private readonly Dictionary<int, VehicleView> _views = new Dictionary<int, VehicleView>();
        private readonly VehicleInput _input = new VehicleInput();
        private bool _tiller, _implement, _lights = true;
        private readonly StringBuilder _sb = new StringBuilder(256);
        private Action<VehicleSpawnedEvent> _onSpawn;
        private Action<VehicleRemovedEvent> _onRemove;
        private Action<AttachmentChangedEvent> _onAttach;
        private Action<PlayerVehicleChangedEvent> _onPlayer;
        private Action<int> _onTicked;

        public IReadOnlyDictionary<int, VehicleView> Views => _views;
        public VehicleView PlayerView => _boot.Sim != null && _views.TryGetValue(_boot.Sim.World.Vehicles.PlayerVehicleId, out var v) ? v : null;

        public void Construct(Bootstrap boot)
        {
            _boot = boot;
            _vs = boot.Sim.GetSystem<VehicleSystem>();
            foreach (var s in boot.Sim.World.Vehicles.List) CreateView(s);
            _onSpawn = e => { var s = _vs.Get(_boot.Sim.Ctx, e.VehicleId); if (s != null) CreateView(s); };
            _onRemove = e => { if (_views.TryGetValue(e.VehicleId, out var v)) { Destroy(v.gameObject); _views.Remove(e.VehicleId); } };
            _onAttach = e => { if (_views.TryGetValue(e.VehicleId, out var v)) v.SyncImplements(); };
            _onPlayer = e => { _tiller = false; _implement = false; _boot.Cameras.SetMode(e.VehicleId >= 0 ? CameraMode.Chase : CameraMode.Free); };
            _onTicked = n => { foreach (var v in _views.Values) v.OnSimTicked(); };
            boot.Sim.Events.Subscribe(_onSpawn);
            boot.Sim.Events.Subscribe(_onRemove);
            boot.Sim.Events.Subscribe(_onAttach);
            boot.Sim.Events.Subscribe(_onPlayer);
            boot.Runner.Ticked += _onTicked;
            boot.Ui.Hud.AddStatusProvider(VehicleStatus);
            boot.Ui.Hud.AddLegend("Enter enter/leave machine  W/S A/D drive  Space brake  R/F blade  Q/E angle  T tiller  V implement  G work  L lights  I engine");
        }

        private void OnDestroy()
        {
            if (_boot != null && _boot.Runner != null) _boot.Runner.Ticked -= _onTicked;
        }

        private void CreateView(VehicleState s)
        {
            if (_views.ContainsKey(s.Id)) return;
            var def = _boot.Data.Vehicle(s.DefId);
            if (def == null) return;
            var go = new GameObject("vehicle");
            go.transform.SetParent(transform, false);
            var view = go.AddComponent<VehicleView>();
            view.Construct(_boot, s, def);
            _views[s.Id] = view;
        }

        private void Update()
        {
            if (_boot.Sim == null) return;
            var ctx = _boot.Sim.Ctx;
            var input = _boot.Input;
            var player = _vs.PlayerVehicle(ctx);

            if (input.Game.Interact.WasPressedThisFrame() && !_boot.Ui.PointerOverUi)
            {
                if (player != null) _vs.Exit(ctx);
                else
                {
                    Vec2 at = Bootstrap.ToMap(_boot.Cameras.Camera.transform.position);
                    var near = _vs.Nearest(ctx, at, _boot.Data.Tuning.F("vehicles.playerReachM") * 6f);
                    if (near != null)
                    {
                        if (!_vs.TryEnter(ctx, near.Id, out string reason)) _boot.Sim.Log(reason, Core.Sim.LogLevel.Warning);
                    }
                    else _boot.Sim.Log("No machine nearby. Fly closer and press Enter.", Core.Sim.LogLevel.Info);
                }
            }

            if (player != null)
            {
                var v = input.Vehicle;
                if (v.TillerToggle.WasPressedThisFrame()) _tiller = !_tiller;
                if (v.ImplementToggle.WasPressedThisFrame()) _implement = !_implement;
                if (v.LightsToggle.WasPressedThisFrame()) _lights = !_lights;
                if (v.EngineToggle.WasPressedThisFrame())
                {
                    if (player.EngineOn) _vs.StopEngine(ctx, player.Id);
                    else if (!_vs.StartEngine(ctx, player.Id, out string reason)) _boot.Sim.Log(reason, Core.Sim.LogLevel.Warning);
                }
                _input.Throttle = v.Throttle.ReadValue<float>();
                _input.Steer = v.Steer.ReadValue<float>();
                _input.Brake = v.Brake.ReadValue<float>();
                _input.BladeLift = v.BladeLift.ReadValue<float>();
                _input.BladeAngle = v.BladeAngle.ReadValue<float>();
                _input.BladeTilt = v.BladeTilt.ReadValue<float>();
                _input.Tiller = _tiller;
                _input.Implement = _implement;
                _input.Lights = _lights;
                _input.Work = v.Work.IsPressed();
                _vs.SetInput(ctx, player.Id, _input);
            }
        }

        private void LateUpdate()
        {
            if (_boot.Sim == null || _boot.Clock == null) return;
            float alpha = _boot.Clock.InterpolationAlpha;
            foreach (var v in _views.Values) v.Present(alpha);
        }

        private string VehicleStatus()
        {
            var ctx = _boot.Sim.Ctx;
            var p = _vs.PlayerVehicle(ctx);
            if (p == null)
            {
                int count = ctx.World.Vehicles.List.Count;
                int active = 0;
                foreach (var v in ctx.World.Vehicles.List) if (v.EngineOn) active++;
                return "Fleet: " + count + " machines, " + active + " running. Press Enter near one to drive.";
            }
            var def = p.Def ?? _boot.Data.Vehicle(p.DefId);
            _sb.Length = 0;
            _sb.Append("<b>").Append(p.Name).Append("</b> ").Append(p.EngineOn ? UiFactory.ColorTag(UiFactory.Good, "running") : UiFactory.ColorTag(UiFactory.Warn, "engine off"));
            if (p.Stranded) _sb.Append(' ').Append(UiFactory.ColorTag(UiFactory.Bad, p.StrandedReason));
            _sb.Append('\n');
            _sb.Append(UiFactory.F(p.SpeedKmh, 1)).Append(" km/h  ").Append(UiFactory.F(p.Rpm, 0)).Append(" rpm  load ").Append(UiFactory.F(p.LoadFrac * 100f, 0)).Append("%  slip ").Append(UiFactory.F(p.SlipFrac * 100f, 0)).Append("%\n");
            float fuelFrac = def.EnergyCapacity > 0f ? p.Fuel / def.EnergyCapacity : 0f;
            string fuel = (def.IsElectric ? "charge " : "fuel ") + UiFactory.F(fuelFrac * 100f, 0) + "% (" + UiFactory.F(p.Fuel, 0) + (def.IsElectric ? " kWh" : " L") + ", " + UiFactory.F(p.FuelBurnLph, 1) + "/h)";
            _sb.Append(fuelFrac < _boot.Data.Tuning.F("vehicles.fuelReserveWarningFrac") ? UiFactory.ColorTag(UiFactory.Bad, fuel) : fuel).Append('\n');
            foreach (var m in p.Mounted)
            {
                var att = _boot.Data.Attachment(m.DefId);
                if (att == null) continue;
                string pose = m.Slot == SlotPosition.Rear ? (m.Engaged ? UiFactory.ColorTag(UiFactory.Good, "DOWN / working") : "raised") : (m.Lift < 0.35f ? UiFactory.ColorTag(UiFactory.Good, "DOWN") : (m.Lift > 0.9f ? "raised" : "lift " + UiFactory.F(m.Lift * 100f, 0) + "%"));
                _sb.Append(m.Slot).Append(": ").Append(att.DisplayName).Append(' ').Append(pose);
                if (m.LoadKg > 1f) _sb.Append("  carrying ").Append(UiFactory.F(m.LoadKg / 1000f, 2)).Append(" t");
                if (Mathf.Abs(m.Angle) > 0.02f) _sb.Append("  angle ").Append(UiFactory.F(m.Angle * Mathf.Rad2Deg, 0)).Append("°");
                _sb.Append('\n');
            }
            if (p.CargoKg > 0f) _sb.Append("cargo ").Append(UiFactory.F(p.CargoKg / 1000f, 2)).Append(" t ").Append(p.CargoKind).Append('\n');
            _sb.Append("tilled today ").Append(UiFactory.F(p.TilledM2Today / 10000f, 2)).Append(" ha  plowed ").Append(UiFactory.F(p.PlowedM2Today / 10000f, 2)).Append(" ha\n");
            float cond = p.Condition.ConditionPct;
            string condText = "condition " + UiFactory.F(cond, 0) + "%  hours " + UiFactory.F(p.HoursMeter, 0) + "  warm-up " + UiFactory.F(p.WarmupFrac * 100f, 0) + "%";
            _sb.Append(cond < 40f ? UiFactory.ColorTag(UiFactory.Warn, condText) : condText).Append('\n');
            if (p.Condition.ActiveFailures.Count > 0) _sb.Append(UiFactory.ColorTag(UiFactory.Bad, "FAILURE: " + p.Condition.ActiveFailures[0].Description)).Append('\n');
            if (_boot.Sim.TryGetSystem<TaskSystem>(out var ts))
            {
                var task = p.TaskId >= 0 ? ts.Get(ctx, p.TaskId) : ts.TaskAt(ctx, p);
                if (task != null) _sb.Append("task: ").Append(task.Title).Append(' ').Append(UiFactory.F(task.Progress * 100f, 0)).Append("%").Append(task.Status == TaskStatus.Blocked ? " " + UiFactory.ColorTag(UiFactory.Bad, task.BlockReason) : "");
            }
            return _sb.ToString();
        }
    }
}
