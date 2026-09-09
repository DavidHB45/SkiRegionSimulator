using System.Collections.Generic;
using AlpineSim.Core.Tasks;
using AlpineSim.Core.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>The job board: every task with status, progress, blocking reason, and the machines that can take it.</summary>
    public sealed class TaskPanel : UiPanel
    {
        private RectTransform _list;
        private ScrollRect _scroll;
        private float _nextRefresh;
        private int _selectedTask = -1;
        private readonly List<TaskCandidate> _candidates = new List<TaskCandidate>();

        public TaskPanel() : base("Job Board") { }

        protected override void BuildBody(RectTransform body)
        {
            UiFactory.Label("Hint", body, "Assign a machine with an operator to a job, or drive to the site yourself and hold G / lower the implement.", 12, TextAnchor.UpperLeft, UiFactory.TextDim);
            _list = UiFactory.ScrollView("Tasks", body, out _scroll);
        }

        public override void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.5f;
            ClearChildren(_list);
            var ctx = Boot.Sim.Ctx;
            if (!Boot.Sim.TryGetSystem<TaskSystem>(out var ts)) return;
            var vs = Boot.Sim.GetSystem<VehicleSystem>();
            var tasks = ts.All(ctx);
            int shown = 0;
            for (int i = tasks.Count - 1; i >= 0; i--)
            {
                var t = tasks[i];
                if (!t.IsActive && t.Status != TaskStatus.Done) continue;
                shown++;
                var row = UiFactory.Row("task" + t.Id, _list, 24f);
                row.gameObject.AddComponent<Image>().color = t.Id == _selectedTask ? new Color(0.3f, 0.45f, 0.6f, 0.4f) : (shown % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt);
                Color sc = t.Status == TaskStatus.Done ? UiFactory.Good : (t.Status == TaskStatus.Blocked ? UiFactory.Bad : (t.Status == TaskStatus.InProgress ? UiFactory.Accent : UiFactory.TextColor));
                UiFactory.RowLabel(row, t.Title, 240f);
                UiFactory.RowLabel(row, UiFactory.ColorTag(sc, t.Status.ToString()) + (t.Status == TaskStatus.Blocked ? " " + t.BlockReason : ""), 200f);
                UiFactory.RowLabel(row, UiFactory.F(t.Progress * 100f, 0) + "%", 50f, TextAnchor.MiddleRight);
                string who = "-";
                if (t.AssignedVehicleId >= 0) { var v = vs.Get(ctx, t.AssignedVehicleId); who = v != null ? v.Name : "?"; }
                UiFactory.RowLabel(row, who, 150f);
                int id = t.Id;
                if (t.IsActive)
                {
                    var b = UiFactory.Button("Sel", row, _selectedTask == id ? "Close" : "Assign...", () => { _selectedTask = _selectedTask == id ? -1 : id; _nextRefresh = 0f; }, 12);
                    b.GetComponent<LayoutElement>().preferredWidth = 80f;
                    var c = UiFactory.Button("Cancel", row, "Cancel", () => { ts.Cancel(ctx, id); _nextRefresh = 0f; }, 12, new Color(0.45f, 0.2f, 0.2f));
                    c.GetComponent<LayoutElement>().preferredWidth = 60f;
                }
                if (_selectedTask == t.Id && t.IsActive)
                {
                    ts.Candidates(ctx, t, _candidates);
                    foreach (var cand in _candidates)
                    {
                        var v = vs.Get(ctx, cand.VehicleId);
                        if (v == null) continue;
                        var crow = UiFactory.Row("cand" + v.Id, _list, 22f);
                        UiFactory.RowLabel(crow, "    " + v.Name + "  (" + v.LocationLabel + ")", 300f, TextAnchor.MiddleLeft, 12);
                        UiFactory.RowLabel(crow, cand.Eligible ? UiFactory.ColorTag(UiFactory.Good, "can take it") : UiFactory.ColorTag(UiFactory.Warn, cand.Reason), 300f, TextAnchor.MiddleLeft, 12);
                        if (cand.Eligible)
                        {
                            int vid = v.Id;
                            var ab = UiFactory.Button("Go", crow, "Dispatch", () => { if (!ts.Assign(ctx, id, vid, out string r)) Boot.Sim.Log(r, Core.Sim.LogLevel.Warning); _selectedTask = -1; _nextRefresh = 0f; }, 12);
                            ab.GetComponent<LayoutElement>().preferredWidth = 80f;
                        }
                    }
                }
            }
            if (shown == 0) UiFactory.Label("Empty", _list, "No jobs on the board. Grooming jobs appear each evening; create them from the piste list or drive and groom freely.", 13, TextAnchor.UpperLeft, UiFactory.TextDim);
        }
    }
}
