using AlpineSim.Core.Pistes;
using AlpineSim.Core.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>Resort overview: every run with its PQI, traffic and grooming state; post grooming jobs; open or close runs.</summary>
    public sealed class PistePanel : UiPanel
    {
        private Text _summary;
        private RectTransform _list;
        private ScrollRect _scroll;
        private float _nextRefresh;

        public PistePanel() : base("Runs & Snow") { }

        protected override void BuildBody(RectTransform body)
        {
            _summary = UiFactory.Label("Summary", body, "", 13);
            var row = UiFactory.Row("Actions", body, 26f);
            UiFactory.Button("PostAll", row, "Post grooming jobs for tonight", () =>
            {
                if (Boot.Sim.TryGetSystem<TaskSystem>(out var ts)) { int n = ts.PostEveningGroomJobs(Boot.Sim.Ctx); if (n == 0) Boot.Sim.Log("Every open run already has a job or is fresh.", Core.Sim.LogLevel.Info); }
                _nextRefresh = 0f;
            }, 12);
            _list = UiFactory.ScrollView("Pistes", body, out _scroll);
        }

        public override void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.75f;
            var ctx = Boot.Sim.Ctx;
            var net = Boot.Sim.World.Pistes;
            var w = Boot.Sim.World.Weather.Current;
            _summary.text = "Resort PQI " + PqiTag(net.ResortPqi) + "   snow at base " + UiFactory.F(Boot.Sim.World.Snow != null ? Boot.Sim.World.Snow.DepthAtMm(Boot.Sim.Scenario.BaseArea.Pos) / 10f : 0f, 0) + " cm"
                + "   air " + UiFactory.F(w.TempC, 1) + " C  wet-bulb " + UiFactory.F(w.WetBulbC, 1) + " C  wind " + UiFactory.F(w.WindKmh, 0) + " km/h"
                + (w.SnowfallCmPerHour > 0.05f ? "  snowing " + UiFactory.F(w.SnowfallCmPerHour, 1) + " cm/h" : "");
            ClearChildren(_list);
            Boot.Sim.TryGetSystem<TaskSystem>(out var ts);
            int n = 0;
            var header = UiFactory.Row("Header", _list, 20f);
            UiFactory.RowLabel(header, "Run", 190f, TextAnchor.MiddleLeft, 12, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Grade", 70f, TextAnchor.MiddleLeft, 12, UiFactory.TextDim);
            UiFactory.RowLabel(header, "PQI", 50f, TextAnchor.MiddleRight, 12, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Skier passes today", 130f, TextAnchor.MiddleRight, 12, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Length / drop", 130f, TextAnchor.MiddleLeft, 12, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Job", 120f, TextAnchor.MiddleLeft, 12, UiFactory.TextDim);
            foreach (var p in net.Pistes)
            {
                n++;
                var row = UiFactory.Row("piste_" + p.Id, _list, 24f);
                row.gameObject.AddComponent<Image>().color = n % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt;
                UiFactory.RowLabel(row, DifficultyTag(p.Difficulty) + " " + p.Name + (p.Open ? "" : UiFactory.ColorTag(UiFactory.Bad, " CLOSED")), 190f);
                UiFactory.RowLabel(row, UiFactory.F(p.MaxGradeDeg, 0) + " deg", 70f);
                UiFactory.RowLabel(row, PqiTag(p.Pqi), 50f, TextAnchor.MiddleRight);
                UiFactory.RowLabel(row, UiFactory.F(p.TrafficToday, 0), 130f, TextAnchor.MiddleRight);
                UiFactory.RowLabel(row, UiFactory.F(p.LengthM, 0) + " m / " + UiFactory.F(p.VerticalM, 0) + " m", 130f);
                string job = "-";
                if (ts != null && ts.HasActiveGroomJob(ctx, p.Id)) job = UiFactory.ColorTag(UiFactory.Accent, "groom job posted");
                else if (!p.Groomable) job = UiFactory.ColorTag(UiFactory.TextDim, "not groomed");
                UiFactory.RowLabel(row, job, 120f);
                string id = p.Id;
                if (ts != null && p.Groomable && !ts.HasActiveGroomJob(ctx, p.Id))
                {
                    var b = UiFactory.Button("Groom", row, "Post groom job", () =>
                    {
                        var piste = Boot.Sim.World.Pistes.Piste(id);
                        if (piste == null) return;
                        var t = ts.Create(ctx, TaskKind.Groom, "Groom " + piste.Name, piste.Points[piste.Points.Count - 1], piste.Id, -1, 1f, 1.2f, -1, true);
                        Boot.Sim.Log("Posted: " + t.Title);
                        _nextRefresh = 0f;
                    }, 12);
                    b.GetComponent<LayoutElement>().preferredWidth = 110f;
                }
                var toggle = UiFactory.Button("Open", row, p.Open ? "Close run" : "Open run", () => { var piste = Boot.Sim.World.Pistes.Piste(id); if (piste != null) { piste.Open = !piste.Open; Boot.Sim.Log(piste.Name + (piste.Open ? " opened." : " closed.")); } _nextRefresh = 0f; }, 12, p.Open ? new Color(0.45f, 0.2f, 0.2f) : new Color(0.2f, 0.4f, 0.25f));
                toggle.GetComponent<LayoutElement>().preferredWidth = 80f;
            }
        }

        public static string PqiTag(float pqi)
        {
            Color c = pqi >= 75f ? UiFactory.Good : (pqi >= 50f ? UiFactory.Warn : UiFactory.Bad);
            return UiFactory.ColorTag(c, UiFactory.F(pqi, 0));
        }

        public static string DifficultyTag(PisteDifficulty d)
        {
            switch (d)
            {
                case PisteDifficulty.Green: return UiFactory.ColorTag(new Color(0.4f, 0.9f, 0.4f), "●");
                case PisteDifficulty.Blue: return UiFactory.ColorTag(new Color(0.35f, 0.6f, 1f), "■");
                case PisteDifficulty.Red: return UiFactory.ColorTag(new Color(1f, 0.35f, 0.3f), "◆");
                case PisteDifficulty.Black: return UiFactory.ColorTag(new Color(0.2f, 0.2f, 0.2f), "◆◆");
                case PisteDifficulty.Nordic: return UiFactory.ColorTag(new Color(0.8f, 0.8f, 0.5f), "~");
                default: return UiFactory.ColorTag(new Color(1f, 0.7f, 0.2f), "P");
            }
        }
    }
}
