using System.Collections.Generic;
using AlpineSim.Core.Construction;
using AlpineSim.Core.Economy;
using AlpineSim.Core.Lifts;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Unity.Construction;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>
    /// Stake and commit lifts and runs, then follow the driven construction chain project by project.
    /// A lift is staked by picking a type and clicking bottom and top on the terrain; the plan's
    /// gating reasons and capex are shown before any money moves.
    /// </summary>
    public sealed class ConstructionPanel : UiPanel
    {
        private readonly PlacementController _placement;
        private RectTransform _types;
        private Text _planText;
        private InputField _nameField;
        private Toggle _heliToggle;
        private RectTransform _projects;
        private ScrollRect _scrollTypes, _scrollProjects;
        private string _selectedType = "";
        private LiftPlanResult _plan;
        private bool _heli;
        private float _nextRefresh;
        private PisteDifficulty _runDifficulty = PisteDifficulty.Blue;
        private float _runWidth = 40f;
        private int _runCounter = 1;

        public ConstructionPanel(PlacementController placement) : base("Construction")
        {
            _placement = placement;
        }

        private ConstructionView View => Boot.ConstructionView;

        protected override void BuildBody(RectTransform body)
        {
            var split = UiFactory.CreateRect("Split", body);
            split.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            UiFactory.HorizontalRow(split, 8f);

            var left = UiFactory.CreateRect("Left", split);
            left.gameObject.AddComponent<LayoutElement>().preferredWidth = 380f;
            UiFactory.VerticalStack(left, 4f, 2);
            UiFactory.Label("LiftTitle", left, "<b>New lift</b>  pick a type, then click bottom and top on the mountain", 12, TextAnchor.UpperLeft, UiFactory.TextDim);
            _types = UiFactory.ScrollView("Types", left, out _scrollTypes);
            _planText = UiFactory.Label("Plan", left, "", 12);
            var nameRow = UiFactory.Row("Name", left, 24f);
            UiFactory.RowLabel(nameRow, "Name", 50f);
            _nameField = UiFactory.InputField("NameField", nameRow, "New lift", null, 12);
            _heliToggle = UiFactory.Toggle("Heli", left, "Fly towers in by helicopter (faster, costly)", false, v => _heli = v, 12);
            var commitRow = UiFactory.Row("Commit", left, 26f);
            UiFactory.Button("Commit", commitRow, "Commit and start construction", CommitLift, 12, new Color(0.2f, 0.45f, 0.25f));
            UiFactory.Button("Clear", commitRow, "Clear plan", () => { _plan = null; View.ShowPlan(null); _placement.Cancel(); _nextRefresh = 0f; }, 12);

            UiFactory.Spacer(left, 2f);
            UiFactory.Label("RunTitle", left, "<b>New run</b>  click the line from top to bottom, Enter to finish", 12, TextAnchor.UpperLeft, UiFactory.TextDim);
            var diffRow = UiFactory.Row("Diff", left, 24f);
            foreach (PisteDifficulty d in new[] { PisteDifficulty.Green, PisteDifficulty.Blue, PisteDifficulty.Red, PisteDifficulty.Black })
            {
                var dd = d;
                UiFactory.Button("d" + d, diffRow, d.ToString(), () => { _runDifficulty = dd; _nextRefresh = 0f; }, 12);
            }
            var widthRow = UiFactory.Row("Width", left, 22f);
            var widthLabel = UiFactory.RowLabel(widthRow, "Width 40 m", 90f, TextAnchor.MiddleLeft, 12);
            UiFactory.Slider("WidthSlider", widthRow, 15f, 80f, 40f, true, v => { _runWidth = v; widthLabel.text = "Width " + UiFactory.F(v, 0) + " m"; });
            UiFactory.Button("StakeRun", left, "Stake a run on the mountain", StakeRun, 12);

            var right = UiFactory.CreateRect("Right", split);
            right.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            UiFactory.VerticalStack(right, 4f, 2);
            UiFactory.Label("ProjTitle", right, "<b>Projects</b>  each stage is a job on the board; machines drive there and do the work", 12, TextAnchor.UpperLeft, UiFactory.TextDim);
            _projects = UiFactory.ScrollView("Projects", right, out _scrollProjects);
        }

        private void BeginLiftStake(string typeId)
        {
            _selectedType = typeId;
            var type = Boot.Data.LiftType(typeId);
            _placement.Begin(new PlacementController.Session
            {
                Label = "Stake " + (type != null ? type.DisplayName : typeId) + ": click the bottom terminal, then the top",
                MaxPoints = 2,
                Preview = s =>
                {
                    if (s.Points.Count != 1 || !s.HasCursor) return "";
                    var p = Boot.Sim.GetSystem<ConstructionSystem>().Stake(Boot.Sim.Ctx, _selectedType, s.Points[0], s.Cursor);
                    return "length " + UiFactory.F(p.LengthM, 0) + " m, vertical " + UiFactory.F(p.VerticalM, 0) + " m, span " + UiFactory.F(p.MaxSpanM, 0) + " m, grade " + UiFactory.F(p.MaxGradeDeg, 0) + " deg, capex " + UiFactory.Money(p.CapexTotal) + (p.Ok ? UiFactory.ColorTag(UiFactory.Good, "  buildable") : UiFactory.ColorTag(UiFactory.Bad, "  " + (p.Reasons.Count > 0 ? p.Reasons[0] : "not buildable")));
                },
                OnFinished = s =>
                {
                    _plan = Boot.Sim.GetSystem<ConstructionSystem>().Stake(Boot.Sim.Ctx, _selectedType, s.Points[0], s.Points[1]);
                    View.ShowPlan(_plan);
                    _nextRefresh = 0f;
                    Show();
                },
            });
        }

        private void CommitLift()
        {
            if (_plan == null) { Boot.Sim.Log("Stake a lift first: pick a type and click bottom and top.", Core.Sim.LogLevel.Warning); return; }
            var cs = Boot.Sim.GetSystem<ConstructionSystem>();
            string name = string.IsNullOrEmpty(_nameField.text) ? "New lift" : _nameField.text;
            var project = cs.Commit(Boot.Sim.Ctx, _plan, name, null, _heli, out string reason);
            if (project == null) { Boot.Sim.Log(reason, Core.Sim.LogLevel.Warning); return; }
            _plan = null;
            View.ShowPlan(null);
            _nextRefresh = 0f;
        }

        private void StakeRun()
        {
            _placement.Begin(new PlacementController.Session
            {
                Label = "Stake run: click points from the top down, Enter to finish",
                MaxPoints = 24,
                Preview = s =>
                {
                    if (s.Points.Count == 0) return "";
                    var pts = new List<Vec2>(s.Points);
                    if (s.HasCursor) pts.Add(s.Cursor);
                    var cs = Boot.Sim.GetSystem<ConstructionSystem>();
                    bool ok = cs.StakeRun(Boot.Sim.Ctx, "run_preview", "preview", _runDifficulty, _runWidth, pts, "", "", out double cost, out var reasons);
                    return "cost " + UiFactory.Money(cost) + (ok ? UiFactory.ColorTag(UiFactory.Good, "  buildable") : UiFactory.ColorTag(UiFactory.Bad, "  " + (reasons.Count > 0 ? reasons[0] : "")));
                },
                OnFinished = s =>
                {
                    var cs = Boot.Sim.GetSystem<ConstructionSystem>();
                    var net = Boot.Sim.World.Pistes;
                    string top = NearestNode(net, s.Points[0]);
                    string bottom = NearestNode(net, s.Points[s.Points.Count - 1]);
                    string id = "run_" + Boot.Sim.World.Time.Day + "_" + (_runCounter++);
                    string name = _runDifficulty + " run " + _runCounter;
                    var p = cs.CommitRun(Boot.Sim.Ctx, id, name, _runDifficulty, _runWidth, s.Points, top, bottom, out string reason);
                    if (p == null) Boot.Sim.Log(reason, Core.Sim.LogLevel.Warning);
                    _nextRefresh = 0f;
                    Show();
                },
            });
        }

        private static string NearestNode(PisteNetwork net, Vec2 p)
        {
            string best = "";
            float bestD = float.MaxValue;
            foreach (var n in net.Nodes)
            {
                float d = Vec2.Distance(n.Pos, p);
                if (d < bestD) { bestD = d; best = n.Id; }
            }
            return best;
        }

        public override void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f;
            var ctx = Boot.Sim.Ctx;
            if (!Boot.Sim.TryGetSystem<ConstructionSystem>(out var cs)) return;
            Boot.Sim.TryGetSystem<EconomySystem>(out var es);
            ClearChildren(_types);
            int n = 0;
            foreach (var type in Boot.Data.LiftTypes)
            {
                bool allowed = es == null || es.CanBuyLiftTier(ctx, type.Tier);
                n++;
                var row = UiFactory.Row("type_" + type.Id, _types, 22f);
                row.gameObject.AddComponent<Image>().color = type.Id == _selectedType ? new Color(0.3f, 0.45f, 0.6f, 0.4f) : (n % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt);
                UiFactory.RowLabel(row, "T" + type.Tier + " " + type.DisplayName, 170f, TextAnchor.MiddleLeft, 11, allowed ? UiFactory.TextColor : UiFactory.TextDim);
                UiFactory.RowLabel(row, UiFactory.F(type.CapacityPph, 0) + " pph  " + UiFactory.Money(type.CapexPerKm) + "/km", 130f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
                string id = type.Id;
                if (allowed)
                {
                    var b = UiFactory.Button("Stake", row, "Stake", () => BeginLiftStake(id), 11);
                    b.GetComponent<LayoutElement>().preferredWidth = 50f;
                }
                else UiFactory.RowLabel(row, "act " + RequiredAct(type.Tier), 50f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            }
            if (_plan != null)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append(_plan.Ok ? UiFactory.ColorTag(UiFactory.Good, "<b>Buildable</b>") : UiFactory.ColorTag(UiFactory.Bad, "<b>Not buildable</b>"));
                sb.Append("  ").Append(UiFactory.F(_plan.LengthM, 0)).Append(" m, ").Append(UiFactory.F(_plan.VerticalM, 0)).Append(" m vertical, ").Append(_plan.Towers.Count).Append(" towers, max span ").Append(UiFactory.F(_plan.MaxSpanM, 0)).Append(" m, grade ").Append(UiFactory.F(_plan.MaxGradeDeg, 0)).Append(" deg");
                if (_plan.CrossesGorge) sb.Append(UiFactory.ColorTag(UiFactory.Warn, "  crosses the valley"));
                sb.Append("\ncapex ").Append(UiFactory.Money(_plan.CapexTotal)).Append(" (line ").Append(UiFactory.Money(_plan.CapexLine)).Append(", towers ").Append(UiFactory.Money(_plan.CapexTowers)).Append(", terminals ").Append(UiFactory.Money(_plan.CapexTerminals)).Append(", carriers ").Append(UiFactory.Money(_plan.CapexCarriers)).Append(")");
                sb.Append("\n").Append(_plan.Carriers).Append(" carriers, ").Append(UiFactory.F(_plan.CapacityPph, 0)).Append(" pph, ride ").Append(UiFactory.F(_plan.RideTimeS / 60f, 1)).Append(" min, ").Append(UiFactory.F(_plan.ConcreteM3, 0)).Append(" m3 concrete, ~").Append(UiFactory.F(_plan.EstimatedBuildDays, 0)).Append(" days");
                if (_plan.NeedsHelicopter) sb.Append(UiFactory.ColorTag(UiFactory.Warn, "\nhelicopter required (" + UiFactory.F(_plan.HelicopterHours, 0) + " flight hours)"));
                foreach (var r in _plan.Reasons) sb.Append("\n").Append(UiFactory.ColorTag(UiFactory.Bad, "- " + r));
                _planText.text = sb.ToString();
            }
            else _planText.text = _placement.Active ? UiFactory.ColorTag(UiFactory.Accent, "Staking... click on the mountain.") : "No plan staked.";

            ClearChildren(_projects);
            int shown = 0;
            foreach (var p in cs.Projects(ctx))
            {
                shown++;
                var row = UiFactory.Row("p_" + p.Id, _projects, 24f);
                row.gameObject.AddComponent<Image>().color = shown % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt;
                Color sc = p.Status == ProjectStatus.Complete ? UiFactory.Good : (p.Status == ProjectStatus.Cancelled ? UiFactory.TextDim : (p.Status == ProjectStatus.InProgress ? UiFactory.Accent : UiFactory.Warn));
                UiFactory.RowLabel(row, "<b>" + p.Name + "</b> (" + p.Kind + ")", 200f, TextAnchor.MiddleLeft, 12);
                UiFactory.RowLabel(row, UiFactory.ColorTag(sc, p.Status.ToString()) + (string.IsNullOrEmpty(p.StatusReason) ? "" : " " + p.StatusReason), 220f, TextAnchor.MiddleLeft, 12);
                UiFactory.RowLabel(row, UiFactory.F(p.OverallProgress * 100f, 0) + "%", 45f, TextAnchor.MiddleRight, 12);
                UiFactory.RowLabel(row, UiFactory.Money(p.Spent) + " / " + UiFactory.Money(p.Budget), 150f, TextAnchor.MiddleRight, 12);
                int id = p.Id;
                if (p.Status != ProjectStatus.Complete && p.Status != ProjectStatus.Cancelled)
                {
                    var h = UiFactory.Button("Heli", row, p.HelicopterMode ? "Heli: on" : "Heli: off", () => { cs.SetHelicopterMode(ctx, id, !Boot.Sim.World.Construction.Get(id).HelicopterMode); _nextRefresh = 0f; }, 11);
                    h.GetComponent<LayoutElement>().preferredWidth = 70f;
                    var c = UiFactory.Button("Cancel", row, "Cancel", () => { if (!cs.Cancel(ctx, id, out string r)) Boot.Sim.Log(r, Core.Sim.LogLevel.Warning); _nextRefresh = 0f; }, 11, new Color(0.45f, 0.2f, 0.2f));
                    c.GetComponent<LayoutElement>().preferredWidth = 60f;
                }
                if (p.Status != ProjectStatus.Complete && p.Status != ProjectStatus.Cancelled)
                {
                    for (int i = 0; i < p.Stages.Count; i++)
                    {
                        var st = p.Stages[i];
                        var srow = UiFactory.Row("s" + i, _projects, 18f);
                        string mark = st.Complete ? UiFactory.ColorTag(UiFactory.Good, "done") : (i == p.StageIndex ? UiFactory.ColorTag(UiFactory.Accent, "active") : UiFactory.ColorTag(UiFactory.TextDim, "queued"));
                        UiFactory.RowLabel(srow, "    " + (i + 1) + ". " + st.DisplayName, 260f, TextAnchor.MiddleLeft, 11);
                        UiFactory.RowLabel(srow, mark, 60f, TextAnchor.MiddleLeft, 11);
                        UiFactory.RowLabel(srow, st.UnitsDone + "/" + st.UnitsRequired + " units, " + UiFactory.F(st.WorkDone, 0) + "/" + UiFactory.F(st.WorkRequired, 0) + " h", 200f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
                    }
                    if (p.Kind == ProjectKind.Lift && p.ConcreteRequiredM3 > 0f)
                        UiFactory.Label("conc" + p.Id, _projects, "    concrete " + UiFactory.F(p.ConcreteDeliveredM3, 0) + " / " + UiFactory.F(p.ConcreteRequiredM3, 0) + " m3   towers set " + p.TowersSet + " / " + p.TowersTotal, 11, TextAnchor.MiddleLeft, UiFactory.TextDim);
                }
            }
            if (shown == 0) UiFactory.Label("Empty", _projects, "No projects. Stake a lift or a run.", 12, TextAnchor.UpperLeft, UiFactory.TextDim);
        }

        private int RequiredAct(int tier)
        {
            foreach (var a in Boot.Data.Economy.Acts) if (a.MaxLiftTier >= tier) return a.Act;
            return 5;
        }
    }
}
