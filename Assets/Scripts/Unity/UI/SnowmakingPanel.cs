using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Snowmaking;
using AlpineSim.Core.Vehicles;
using AlpineSim.Unity.Construction;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>
    /// Snowmaking operations: water, pumps, air and power; every gun with its wet-bulb, output and
    /// hydrant; placing guns and hydrants on the mountain; building pump and compressor stations.
    /// </summary>
    public sealed class SnowmakingPanel : UiPanel
    {
        private readonly PlacementController _placement;
        private Text _summary;
        private RectTransform _guns;
        private RectTransform _infra;
        private ScrollRect _scrollA, _scrollB;
        private float _nextRefresh;

        public SnowmakingPanel(PlacementController placement) : base("Snowmaking") { _placement = placement; }

        protected override void BuildBody(RectTransform body)
        {
            _summary = UiFactory.Label("Summary", body, "", 13);
            var actions = UiFactory.Row("Actions", body, 26f);
            UiFactory.Button("Hydrant", actions, "Install hydrant (click on a run)", InstallHydrant, 12);
            UiFactory.Button("AutoAll", actions, "All guns: auto", () => { if (Boot.Sim.TryGetSystem<SnowmakingSystem>(out var sm)) foreach (var g in Boot.Sim.World.Snowmaking.Guns) sm.SetAuto(Boot.Sim.Ctx, g.Id, true); _nextRefresh = 0f; }, 12);
            UiFactory.Button("StopAll", actions, "All guns: stop", () => { if (Boot.Sim.TryGetSystem<SnowmakingSystem>(out var sm)) foreach (var g in Boot.Sim.World.Snowmaking.Guns) { sm.SetAuto(Boot.Sim.Ctx, g.Id, false); sm.SetRunning(Boot.Sim.Ctx, g.Id, false); } _nextRefresh = 0f; }, 12);
            var split = UiFactory.CreateRect("Split", body);
            split.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            UiFactory.HorizontalRow(split, 6f);
            _guns = UiFactory.ScrollView("Guns", split, out _scrollA);
            _infra = UiFactory.ScrollView("Infra", split, out _scrollB);
            _infra.parent.parent.gameObject.GetComponent<LayoutElement>().preferredWidth = 360f;
            _infra.parent.parent.gameObject.GetComponent<LayoutElement>().flexibleWidth = 0f;
        }

        private void InstallHydrant()
        {
            _placement.Begin(new PlacementController.Session
            {
                Label = "Install hydrant: click beside a run (" + UiFactory.Money(Boot.Data.Stations.HydrantInstallCost) + ")",
                MaxPoints = 1,
                OnFinished = s =>
                {
                    if (!Boot.Sim.TryGetSystem<SnowmakingSystem>(out var sm)) return;
                    string piste = NearestPiste(Boot.Sim.World.Pistes, s.Points[0], 60f);
                    if (piste == null) { Boot.Sim.Log("Hydrants go beside a run (within 60 m of its centreline).", Core.Sim.LogLevel.Warning); return; }
                    if (!sm.InstallHydrant(Boot.Sim.Ctx, s.Points[0], piste, out string reason)) Boot.Sim.Log(reason, Core.Sim.LogLevel.Warning);
                    else Boot.Sim.Log("Hydrant installed on " + piste + ".");
                    _nextRefresh = 0f;
                    Show();
                },
            });
        }

        private void PlaceGun(int vehicleId)
        {
            var v = Boot.Sim.World.Vehicles.Get(vehicleId);
            if (v == null) return;
            _placement.Begin(new PlacementController.Session
            {
                Label = "Place " + v.Name + ": click on a run (scroll to aim; a hydrant within " + UiFactory.F(Boot.Data.Tuning.F("snowmaking.hoseMaxM"), 0) + " m connects automatically)",
                MaxPoints = 1,
                Preview = s =>
                {
                    if (!s.HasCursor || !Boot.Sim.TryGetSystem<SnowmakingSystem>(out var sm)) return "";
                    var h = sm.NearestHydrant(Boot.Sim.Ctx, s.Cursor, Boot.Data.Tuning.F("snowmaking.hoseMaxM"));
                    return "aim " + UiFactory.F(s.HeadingDeg, 0) + " deg   wet-bulb here " + UiFactory.F(sm.WetBulbAt(Boot.Sim.Ctx, s.Cursor), 1) + " C   " + (h != null ? UiFactory.ColorTag(UiFactory.Good, "hydrant in reach") : UiFactory.ColorTag(UiFactory.Warn, "no hydrant in hose reach"));
                },
                OnFinished = s =>
                {
                    if (!Boot.Sim.TryGetSystem<SnowmakingSystem>(out var sm)) return;
                    var gun = sm.Place(Boot.Sim.Ctx, vehicleId, s.Points[0], s.HeadingDeg, out string reason);
                    if (gun == null) Boot.Sim.Log(reason, Core.Sim.LogLevel.Warning);
                    _nextRefresh = 0f;
                    Show();
                },
            });
        }

        private static string NearestPiste(PisteNetwork net, Vec2 p, float maxD)
        {
            string best = null; float bestD = maxD;
            foreach (var seg in net.Segments)
            {
                float d = DistanceToSegment(p, seg.A, seg.B);
                if (d < bestD) { bestD = d; best = seg.PisteId; }
            }
            return best;
        }

        private static float DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
        {
            var ab = b - a;
            float len2 = ab.SqrLength;
            float t = len2 > 1e-6f ? MathUtil.Clamp01(Vec2.Dot(p - a, ab) / len2) : 0f;
            return Vec2.Distance(p, a + ab * t);
        }

        public override void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f;
            var ctx = Boot.Sim.Ctx;
            if (!Boot.Sim.TryGetSystem<SnowmakingSystem>(out var sm)) return;
            var s = Boot.Sim.World.Snowmaking;
            var w = Boot.Sim.World.Weather.Current;
            int running = 0; float output = 0f;
            foreach (var g in s.Guns) if (g.Running) { running++; output += g.OutputM3PerHour; }
            _summary.text = "<b>Reservoir " + UiFactory.F(s.ReservoirM3, 0) + " / " + UiFactory.F(s.ReservoirCapacityM3, 0) + " m3</b> (+" + UiFactory.F(s.ReservoirInflowM3PerHour, 0) + " m3/h)   pumps " + UiFactory.F(s.PumpCapacityLps, 0) + " L/s   air " + UiFactory.F(s.CompressorCapacityM3Min, 0) + " m3/min   power " + UiFactory.F(s.PowerAvailableKw, 0) + " kW"
                + "\nwet-bulb at base " + UiFactory.F(w.WetBulbC, 1) + " C   " + running + " of " + s.Guns.Count + " guns running, " + UiFactory.F(output, 0) + " m3/h   today: water " + UiFactory.F(s.WaterUsedTodayM3, 0) + " m3, " + UiFactory.F(s.KwhToday, 0) + " kWh   season snow made " + UiFactory.F(s.SnowMadeSeasonM3, 0) + " m3";

            ClearChildren(_guns);
            var header = UiFactory.Row("gh", _guns, 18f);
            UiFactory.RowLabel(header, "Gun", 150f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Status", 150f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Wet-bulb", 60f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Output m3/h", 80f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Hydrant", 60f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            int n = 0;
            foreach (var g in s.Guns)
            {
                n++;
                var v = Boot.Sim.World.Vehicles.Get(g.VehicleId);
                var row = UiFactory.Row("g" + g.Id, _guns, 22f);
                row.gameObject.AddComponent<Image>().color = n % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt;
                UiFactory.RowLabel(row, v != null ? v.Name : g.DefId, 150f, TextAnchor.MiddleLeft, 11);
                bool can = sm.CanRun(ctx, g, out string why);
                UiFactory.RowLabel(row, g.Running ? UiFactory.ColorTag(UiFactory.Good, "running") : (can ? UiFactory.ColorTag(UiFactory.Warn, "idle" + (g.AutoRun ? " (auto)" : "")) : UiFactory.ColorTag(UiFactory.Bad, why)), 150f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, UiFactory.F(sm.WetBulbAt(ctx, g.Pos), 1), 60f, TextAnchor.MiddleRight, 11);
                UiFactory.RowLabel(row, UiFactory.F(g.Running ? g.OutputM3PerHour : sm.PotentialOutputM3PerHour(ctx, g), 0), 80f, TextAnchor.MiddleRight, 11);
                UiFactory.RowLabel(row, g.HydrantId >= 0 ? "#" + g.HydrantId : "-", 60f, TextAnchor.MiddleLeft, 11);
                int id = g.Id;
                var run = UiFactory.Button("Run", row, g.Running ? "Stop" : "Start", () => { sm.SetAuto(ctx, id, false); sm.SetRunning(ctx, id, !Boot.Sim.World.Snowmaking.Gun(id).Running); _nextRefresh = 0f; }, 11);
                run.GetComponent<LayoutElement>().preferredWidth = 50f;
                var auto = UiFactory.Button("Auto", row, g.AutoRun ? "Auto: on" : "Auto: off", () => { sm.SetAuto(ctx, id, !Boot.Sim.World.Snowmaking.Gun(id).AutoRun); _nextRefresh = 0f; }, 11);
                auto.GetComponent<LayoutElement>().preferredWidth = 70f;
                if (g.HydrantId < 0)
                {
                    var c = UiFactory.Button("Conn", row, "Connect", () => { var h = sm.NearestHydrant(ctx, Boot.Sim.World.Snowmaking.Gun(id).Pos, Boot.Data.Tuning.F("snowmaking.hoseMaxM")); string r = "No free hydrant within hose reach."; if (h == null || !sm.Connect(ctx, id, h.Id, out r)) Boot.Sim.Log(r, Core.Sim.LogLevel.Warning); _nextRefresh = 0f; }, 11);
                    c.GetComponent<LayoutElement>().preferredWidth = 70f;
                }
                var pick = UiFactory.Button("Pick", row, "Pick up", () => { sm.Remove(ctx, id); _nextRefresh = 0f; }, 11, new Color(0.45f, 0.25f, 0.2f));
                pick.GetComponent<LayoutElement>().preferredWidth = 60f;
            }
            UiFactory.Spacer(_guns, 2f);
            UiFactory.Label("Unplaced", _guns, "<b>Guns in the yard</b>", 12);
            int yard = 0;
            foreach (var v in Boot.Sim.World.Vehicles.List)
            {
                var def = Boot.Data.Vehicle(v.DefId);
                if (def == null || !def.HasRole(VehicleRole.MakeSnow) || v.PlacedGunId >= 0) continue;
                yard++;
                var row = UiFactory.Row("y" + v.Id, _guns, 22f);
                UiFactory.RowLabel(row, v.Name + "  (" + def.DisplayName + ")", 300f, TextAnchor.MiddleLeft, 11);
                int vid = v.Id;
                var b = UiFactory.Button("Place", row, "Place on the mountain", () => PlaceGun(vid), 11);
                b.GetComponent<LayoutElement>().preferredWidth = 150f;
            }
            if (yard == 0) UiFactory.Label("NoYard", _guns, "No spare guns. Buy fan or lance guns on the market (F8).", 11, TextAnchor.UpperLeft, UiFactory.TextDim);

            ClearChildren(_infra);
            UiFactory.Label("Stations", _infra, "<b>Stations</b>", 12);
            foreach (var st in s.Stations) UiFactory.Label("st" + st.Id, _infra, st.Kind + " " + st.DefId + " (day " + (st.BuiltDay + 1) + ")", 11);
            UiFactory.Label("Build", _infra, "<b>Build</b>", 12);
            foreach (var d in Boot.Data.Stations.PumpStations)
            {
                string id = d.Id;
                var row = UiFactory.Row("pump" + d.Id, _infra, 22f);
                UiFactory.FlexLabel(row, d.DisplayName + " " + UiFactory.F(d.FlowLps, 0) + " L/s, " + UiFactory.F(d.HeadM, 0) + " m head, " + UiFactory.F(d.PowerKw, 0) + " kW", TextAnchor.MiddleLeft, 11);
                var b = UiFactory.Button("b", row, UiFactory.Money(d.Cost), () => { if (!sm.BuildStation(ctx, "pump", id, out string r)) Boot.Sim.Log(r, Core.Sim.LogLevel.Warning); _nextRefresh = 0f; }, 11);
                b.GetComponent<LayoutElement>().preferredWidth = 80f;
            }
            foreach (var d in Boot.Data.Stations.CompressorStations)
            {
                string id = d.Id;
                var row = UiFactory.Row("comp" + d.Id, _infra, 22f);
                UiFactory.FlexLabel(row, d.DisplayName + " " + UiFactory.F(d.AirM3PerMin, 0) + " m3/min, " + UiFactory.F(d.PowerKw, 0) + " kW", TextAnchor.MiddleLeft, 11);
                var b = UiFactory.Button("b", row, UiFactory.Money(d.Cost), () => { if (!sm.BuildStation(ctx, "compressor", id, out string r)) Boot.Sim.Log(r, Core.Sim.LogLevel.Warning); _nextRefresh = 0f; }, 11);
                b.GetComponent<LayoutElement>().preferredWidth = 80f;
            }
            UiFactory.Label("Hyd", _infra, "<b>Hydrants</b> " + s.Hydrants.Count + " installed", 12);
            foreach (var h in s.Hydrants)
                UiFactory.Label("h" + h.Id, _infra, "#" + h.Id + " " + h.PisteId + (h.ConnectedGunId >= 0 ? UiFactory.ColorTag(UiFactory.Good, "  gun " + h.ConnectedGunId) : UiFactory.ColorTag(UiFactory.TextDim, "  free")), 11);
        }
    }
}
