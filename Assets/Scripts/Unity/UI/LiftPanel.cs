using AlpineSim.Core.Lifts;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>Lift operations: status and reason, queue and expected wait, loads, condition; open/close and singles line per lift.</summary>
    public sealed class LiftPanel : UiPanel
    {
        private Text _summary;
        private RectTransform _list;
        private ScrollRect _scroll;
        private float _nextRefresh;

        public LiftPanel() : base("Lifts") { }

        protected override void BuildBody(RectTransform body)
        {
            _summary = UiFactory.Label("Summary", body, "", 13);
            _list = UiFactory.ScrollView("Lifts", body, out _scroll);
        }

        public override void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.75f;
            var ctx = Boot.Sim.Ctx;
            if (!Boot.Sim.TryGetSystem<LiftSystem>(out var ls)) return;
            var w = Boot.Sim.World.Weather.Current;
            int open = 0, total = 0, queued = 0;
            foreach (var l in ls.All(ctx)) { if (!l.IsBuilt) continue; total++; if (l.IsRunning) open++; queued += l.QueueGuests; }
            _summary.text = open + " of " + total + " lifts running   " + queued + " guests in queues   wind at base " + UiFactory.F(w.WindKmh, 0) + " km/h" + (w.Lightning ? UiFactory.ColorTag(UiFactory.Bad, "  LIGHTNING") : "") + "   on mountain: " + Boot.Sim.World.Guests.GuestsOnMountain;
            ClearChildren(_list);
            int n = 0;
            var header = UiFactory.Row("Header", _list, 20f);
            UiFactory.RowLabel(header, "Lift", 180f, TextAnchor.MiddleLeft, 12, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Status", 170f, TextAnchor.MiddleLeft, 12, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Queue / wait", 110f, TextAnchor.MiddleLeft, 12, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Loaded today", 90f, TextAnchor.MiddleRight, 12, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Cap. pph", 70f, TextAnchor.MiddleRight, 12, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Condition", 70f, TextAnchor.MiddleRight, 12, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Wind hold", 70f, TextAnchor.MiddleRight, 12, UiFactory.TextDim);
            foreach (var l in ls.All(ctx))
            {
                n++;
                var type = Boot.Data.LiftType(l.TypeId);
                var row = UiFactory.Row("lift_" + l.Id, _list, 24f);
                row.gameObject.AddComponent<Image>().color = n % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt;
                UiFactory.RowLabel(row, "<b>" + l.Name + "</b>  " + (type != null ? type.DisplayName : l.TypeId), 180f, TextAnchor.MiddleLeft, 12);
                UiFactory.RowLabel(row, StatusTag(l), 170f, TextAnchor.MiddleLeft, 12);
                UiFactory.RowLabel(row, l.QueueGuests + " / " + UiFactory.F(ls.ExpectedWaitMin(ctx, l.Id), 0) + " min", 110f, TextAnchor.MiddleLeft, 12);
                UiFactory.RowLabel(row, l.LoadedToday.ToString(), 90f, TextAnchor.MiddleRight, 12);
                UiFactory.RowLabel(row, UiFactory.F(ls.EffectiveCapacityPph(ctx, l), 0), 70f, TextAnchor.MiddleRight, 12);
                float cond = l.Condition * 100f;
                UiFactory.RowLabel(row, UiFactory.ColorTag(cond > 60f ? UiFactory.Good : (cond > 30f ? UiFactory.Warn : UiFactory.Bad), UiFactory.F(cond, 0) + "%"), 70f, TextAnchor.MiddleRight, 12);
                UiFactory.RowLabel(row, UiFactory.F(ls.WindHoldKmh(ctx, l), 0) + " km/h", 70f, TextAnchor.MiddleRight, 12);
                int id = l.Id;
                if (l.IsBuilt && l.Status != LiftStatus.Decommissioned)
                {
                    bool wantOpen = l.Status == LiftStatus.Closed;
                    var b = UiFactory.Button("Toggle", row, wantOpen ? "Open" : "Close", () => { ls.SetOpen(ctx, id, wantOpen); _nextRefresh = 0f; }, 12, wantOpen ? new Color(0.2f, 0.4f, 0.25f) : new Color(0.45f, 0.2f, 0.2f));
                    b.GetComponent<LayoutElement>().preferredWidth = 60f;
                    var s = UiFactory.Button("Singles", row, l.SinglesLine ? "Singles: on" : "Singles: off", () => { ls.SetSinglesLine(ctx, id, !Boot.Sim.World.Lifts.Get(id).SinglesLine); _nextRefresh = 0f; }, 12);
                    s.GetComponent<LayoutElement>().preferredWidth = 90f;
                }
            }
            if (n == 0) UiFactory.Label("Empty", _list, "No lifts. Stake one from the Construction panel (F11).", 13, TextAnchor.UpperLeft, UiFactory.TextDim);
        }

        public static string StatusTag(LiftState l)
        {
            Color c;
            switch (l.Status)
            {
                case LiftStatus.Open: c = UiFactory.Good; break;
                case LiftStatus.WindHold: case LiftStatus.LightningHold: case LiftStatus.ColdHold: case LiftStatus.Inspection: c = UiFactory.Warn; break;
                case LiftStatus.Breakdown: case LiftStatus.Evacuation: c = UiFactory.Bad; break;
                default: c = UiFactory.TextDim; break;
            }
            string s = l.Status.ToString();
            if (!string.IsNullOrEmpty(l.StatusReason) && l.Status != LiftStatus.Open) s += " (" + l.StatusReason + ")";
            return UiFactory.ColorTag(c, s);
        }
    }
}
