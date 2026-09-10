using System.Collections.Generic;
using AlpineSim.Core.Economy;
using AlpineSim.Core.Guests;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>The books: cash, net worth, act, reputation, ticket price, today's flow, yesterday's report by category, loans and the ledger tail.</summary>
    public sealed class LedgerPanel : UiPanel
    {
        private Text _top;
        private Text _price;
        private Slider _priceSlider;
        private RectTransform _report;
        private RectTransform _ledger;
        private ScrollRect _scrollA, _scrollB;
        private float _nextRefresh;
        private float _pendingPrice = -1f;

        public LedgerPanel() : base("Books") { }

        protected override void BuildBody(RectTransform body)
        {
            _top = UiFactory.Label("Top", body, "", 13);
            var priceRow = UiFactory.Row("Price", body, 24f);
            _price = UiFactory.RowLabel(priceRow, "Day ticket", 200f);
            _priceSlider = UiFactory.Slider("PriceSlider", priceRow, 30f, 200f, 70f, true, v => _pendingPrice = v);
            var apply = UiFactory.Button("Apply", priceRow, "Set price", () =>
            {
                if (_pendingPrice > 0f && Boot.Sim.TryGetSystem<EconomySystem>(out var es)) es.SetTicketPrice(Boot.Sim.Ctx, _pendingPrice);
                _nextRefresh = 0f;
            }, 12);
            apply.GetComponent<LayoutElement>().preferredWidth = 90f;
            var loanRow = UiFactory.Row("Loans", body, 24f);
            UiFactory.RowLabel(loanRow, "Borrow", 60f);
            foreach (double amount in new[] { 50000.0, 200000.0, 1000000.0 })
            {
                double a = amount;
                var b = UiFactory.Button("Loan", loanRow, UiFactory.Money(a), () =>
                {
                    if (!Boot.Sim.TryGetSystem<EconomySystem>(out var es)) return;
                    var loan = es.TakeLoan(Boot.Sim.Ctx, a, Boot.Data.Economy.LoanTermDaysDefault, "operating loan", out string reason);
                    if (loan == null) Boot.Sim.Log(reason, Core.Sim.LogLevel.Warning);
                    _nextRefresh = 0f;
                }, 12);
                b.GetComponent<LayoutElement>().preferredWidth = 90f;
            }
            UiFactory.FlexLabel(loanRow, "", TextAnchor.MiddleLeft, 12, UiFactory.TextDim).name = "LoanInfo";
            var split = UiFactory.CreateRect("Split", body);
            var le = split.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            UiFactory.HorizontalRow(split, 6f);
            _report = UiFactory.ScrollView("Report", split, out _scrollA);
            _ledger = UiFactory.ScrollView("Ledger", split, out _scrollB);
        }

        public override void OnWorldRebuilt()
        {
            if (Boot.Sim != null) _priceSlider.value = Boot.Sim.World.Economy.TicketPrice;
        }

        public override void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f;
            var ctx = Boot.Sim.Ctx;
            var e = Boot.Sim.World.Economy;
            if (!Boot.Sim.TryGetSystem<EconomySystem>(out var es)) return;
            var act = es.ActDef(ctx);
            float rep = Boot.Sim.World.Guests.Reputation;
            _top.text = "<b>Cash " + UiFactory.Money(e.Cash) + "</b>   net worth " + UiFactory.Money(es.NetWorth(ctx)) + "   debt " + UiFactory.Money(es.TotalDebt(ctx))
                + "\nAct " + e.Act + " - " + act.DisplayName + "   reputation " + UiFactory.ColorTag(rep >= 60f ? UiFactory.Good : (rep >= 40f ? UiFactory.Warn : UiFactory.Bad), UiFactory.F(rep, 0)) + "/100"
                + "   day " + (Boot.Sim.World.Time.Day + 1) + "   today: revenue " + UiFactory.Money(e.TodayRevenue) + "  costs " + UiFactory.Money(e.TodayCosts)
                + "\nlift tier up to " + act.MaxLiftTier + ", machine tier up to " + act.MaxVehicleTier + ", credit line " + UiFactory.Money(act.MaxLoan) + " at " + UiFactory.F(es.QuoteLoanRatePct(ctx, 100000), 1) + "%";
            _price.text = "Day ticket " + UiFactory.Money(e.TicketPrice) + (_pendingPrice > 0f && Mathf.Abs(_pendingPrice - e.TicketPrice) > 0.5f ? "  -> " + UiFactory.Money(_pendingPrice) : "") + "   half-day " + UiFactory.Money(e.HalfDayPrice) + "   season " + UiFactory.Money(e.SeasonPassPrice);

            ClearChildren(_report);
            var last = es.LastDay(ctx);
            if (last != null)
            {
                UiFactory.Label("Title", _report, "<b>Day " + (last.Day + 1) + " close</b>  " + last.Guests + " guests, PQI " + UiFactory.F(last.AvgPqi, 0) + ", net " + Colored(last.Net), 13);
                var keys = new List<string>(last.ByCategory.Keys);
                keys.Sort((a, b) => System.Math.Abs(last.ByCategory[b]).CompareTo(System.Math.Abs(last.ByCategory[a])));
                foreach (var k in keys)
                {
                    var row = UiFactory.Row("cat_" + k, _report, 18f);
                    UiFactory.RowLabel(row, k, 150f, TextAnchor.MiddleLeft, 12);
                    UiFactory.RowLabel(row, Colored(last.ByCategory[k]), 100f, TextAnchor.MiddleRight, 12);
                }
            }
            else UiFactory.Label("None", _report, "No closed day yet. The books close at midnight.", 12, TextAnchor.UpperLeft, UiFactory.TextDim);
            if (e.Loans.Count > 0)
            {
                UiFactory.Spacer(_report, 2f);
                foreach (var l in e.Loans)
                {
                    if (l.Closed) continue;
                    UiFactory.Label("loan" + l.Id, _report, "Loan " + UiFactory.Money(l.Principal) + " at " + UiFactory.F(l.AnnualRatePct, 1) + "%  balance " + UiFactory.Money(l.Balance) + "  " + UiFactory.Money(l.DailyPayment) + "/day", 12);
                }
            }
            var season = e.Season;
            UiFactory.Spacer(_report, 2f);
            UiFactory.Label("Season", _report, "<b>Season</b>  revenue " + UiFactory.Money(season.Revenue) + "  costs " + UiFactory.Money(season.Costs) + "  net " + Colored(season.Net) + "  guests " + season.Guests, 12);

            ClearChildren(_ledger);
            int shown = 0;
            for (int i = e.Ledger.Count - 1; i >= 0 && shown < 60; i--, shown++)
            {
                var en = e.Ledger[i];
                var row = UiFactory.Row("led" + i, _ledger, 18f);
                UiFactory.RowLabel(row, "D" + (en.Day + 1) + " " + en.Hour.ToString("00") + "h", 60f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
                UiFactory.RowLabel(row, en.Category.ToString(), 100f, TextAnchor.MiddleLeft, 11);
                UiFactory.FlexLabel(row, en.Memo, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, Colored(en.Amount), 90f, TextAnchor.MiddleRight, 11);
            }
        }

        private static string Colored(double v) => UiFactory.ColorTag(v >= 0 ? UiFactory.Good : UiFactory.Bad, UiFactory.Money(v));
    }
}
