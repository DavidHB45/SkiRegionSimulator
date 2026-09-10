using AlpineSim.Core.Fleet;
using AlpineSim.Core.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>Operators: competence, licences, wage, assignment and training; candidates to hire.</summary>
    public sealed class StaffPanel : UiPanel
    {
        private RectTransform _content;
        private ScrollRect _scroll;
        private Text _summary;
        private float _nextRefresh;
        private int _assigning = -1;

        public StaffPanel() : base("Staff") { }

        protected override void BuildBody(RectTransform body)
        {
            _summary = UiFactory.Label("Summary", body, "", 13);
            _content = UiFactory.ScrollView("Content", body, out _scroll);
        }

        public override void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f;
            var ctx = Boot.Sim.Ctx;
            if (!Boot.Sim.TryGetSystem<FleetSystem>(out var fs)) return;
            var f = Boot.Sim.World.Fleet;
            double wages = 0; foreach (var o in f.Operators) if (!o.IsPlayer) wages += o.WagePerHour * Boot.Data.Operators.ShiftHours;
            _summary.text = f.Operators.Count + " operators   wages about " + UiFactory.Money(wages) + "/day   today's wages " + UiFactory.Money(f.WagesToday) + "   " + f.Candidates.Count + " candidates (list refreshes weekly)";
            ClearChildren(_content);
            UiFactory.Label("Ops", _content, "<b>Operators</b>  an unlicensed driver on a licensed machine is an accident waiting to happen", 12);
            int n = 0;
            foreach (var o in f.Operators)
            {
                n++;
                var row = UiFactory.Row("o" + o.Id, _content, 22f);
                row.gameObject.AddComponent<Image>().color = n % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt;
                UiFactory.RowLabel(row, "<b>" + o.Name + "</b>" + (o.IsPlayer ? " (you)" : ""), 150f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, "skill " + UiFactory.F(o.Competence * 100f, 0) + "%  slope " + UiFactory.F(o.MaxSlopeDeg, 0) + " deg", 150f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, Licences(o), 200f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
                UiFactory.RowLabel(row, UiFactory.Money(o.WagePerHour) + "/h", 60f, TextAnchor.MiddleRight, 11);
                var v = o.AssignedVehicleId >= 0 ? Boot.Sim.World.Vehicles.Get(o.AssignedVehicleId) : null;
                string status = v != null ? "on " + v.Name : "unassigned";
                if (o.TrainingLicensePending >= 0) status = UiFactory.ColorTag(UiFactory.Warn, "training " + (OperatorLicense)o.TrainingLicensePending + " until day " + (o.TrainingDoneDay + 1));
                else if (o.OnShift) status += UiFactory.ColorTag(UiFactory.Good, " on shift " + UiFactory.F(o.HoursToday, 1) + " h");
                if (o.Fatigue > 0.7f) status += UiFactory.ColorTag(UiFactory.Bad, " tired");
                UiFactory.RowLabel(row, status, 200f, TextAnchor.MiddleLeft, 11);
                if (o.IsPlayer) continue;
                int id = o.Id;
                if (v != null) UiFactory.Button("un", row, "Unassign", () => { fs.UnassignOperator(ctx, id); _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 70f;
                else UiFactory.Button("as", row, _assigning == id ? "Pick..." : "Assign", () => { _assigning = _assigning == id ? -1 : id; _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 70f;
                UiFactory.Button("tr", row, "Train...", () => { _assigning = _assigning == -id - 2 ? -1 : -id - 2; _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 60f;
                UiFactory.Button("fi", row, "Fire", () => { if (!fs.Fire(ctx, id, out string r)) Boot.Sim.Log(r, Core.Sim.LogLevel.Warning); _nextRefresh = 0f; }, 11, new Color(0.45f, 0.2f, 0.2f)).GetComponent<LayoutElement>().preferredWidth = 50f;
                if (_assigning == o.Id)
                {
                    foreach (var veh in Boot.Sim.World.Vehicles.List)
                    {
                        var def = Boot.Data.Vehicle(veh.DefId);
                        if (def == null || def.IsStationary) continue;
                        var vrow = UiFactory.Row("av" + veh.Id, _content, 20f);
                        bool licensed = fs.OperatorLicensedFor(ctx, o, def);
                        UiFactory.RowLabel(vrow, "    " + veh.Name + " (" + def.DisplayName + ")" + (veh.OperatorId >= 0 ? "  taken" : "") + (licensed ? "" : UiFactory.ColorTag(UiFactory.Warn, "  needs " + def.OperatorLicense)), 420f, TextAnchor.MiddleLeft, 11);
                        int vid = veh.Id;
                        UiFactory.Button("go", vrow, "Assign", () => { if (!fs.AssignOperator(ctx, id, vid, out string r)) Boot.Sim.Log(r, Core.Sim.LogLevel.Warning); _assigning = -1; _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 60f;
                    }
                }
                if (_assigning == -o.Id - 2)
                {
                    foreach (var lic in Boot.Data.Operators.Licenses)
                    {
                        if (o.Has(lic.License)) continue;
                        var lrow = UiFactory.Row("lic" + lic.License, _content, 20f);
                        UiFactory.RowLabel(lrow, "    " + lic.DisplayName + "  " + UiFactory.Money(lic.TrainingCost) + ", " + UiFactory.F(lic.TrainingDays, 0) + " days, +" + UiFactory.Money(lic.WagePremiumPerHour) + "/h" + (lic.Requires.Count > 0 ? "  requires " + string.Join(", ", lic.Requires.ConvertAll(l => l.ToString()).ToArray()) : ""), 460f, TextAnchor.MiddleLeft, 11);
                        var ll = lic.License;
                        UiFactory.Button("t", lrow, "Train", () => { if (!fs.Train(ctx, id, ll, out string r)) Boot.Sim.Log(r, Core.Sim.LogLevel.Warning); _assigning = -1; _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 60f;
                    }
                }
            }
            UiFactory.Spacer(_content, 2f);
            UiFactory.Label("Cand", _content, "<b>Candidates</b>", 12);
            n = 0;
            foreach (var c in f.Candidates)
            {
                n++;
                var row = UiFactory.Row("c" + c.Id, _content, 22f);
                row.gameObject.AddComponent<Image>().color = n % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt;
                UiFactory.RowLabel(row, c.Name, 150f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, "skill " + UiFactory.F(c.Competence * 100f, 0) + "%", 90f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, Licences(c), 260f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
                UiFactory.RowLabel(row, UiFactory.Money(c.WagePerHour) + "/h", 60f, TextAnchor.MiddleRight, 11);
                int id = c.Id;
                UiFactory.Button("hire", row, "Hire", () => { if (fs.Hire(ctx, id, out string r) == null) Boot.Sim.Log(r, Core.Sim.LogLevel.Warning); _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 60f;
            }
            if (n == 0) UiFactory.Label("nc", _content, "Nobody is looking for work this week.", 11, TextAnchor.UpperLeft, UiFactory.TextDim);
        }

        private static string Licences(OperatorState o)
        {
            if (o.Licenses.Count == 0) return "no licences";
            var sb = new System.Text.StringBuilder();
            foreach (var l in o.Licenses) { if (sb.Length > 0) sb.Append(", "); sb.Append(l); }
            return sb.ToString();
        }
    }
}
