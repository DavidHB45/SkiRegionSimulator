using AlpineSim.Core.Economy;
using AlpineSim.Core.Guests;
using AlpineSim.Unity.Guests;
using AlpineSim.Unity.Lifts;
using AlpineSim.Unity.UI;
using UnityEngine;

namespace AlpineSim.Unity
{
    public sealed partial class Bootstrap
    {
        public LiftViewManager Lifts { get; private set; }
        public GuestView Guests { get; private set; }
        private PistePanel _pistePanel;
        private LiftPanel _liftPanel;
        private LedgerPanel _ledgerPanel;
        private bool _m3HudBound;

        partial void StartM3()
        {
            var lifts = new GameObject("Lifts");
            lifts.transform.SetParent(WorldRoot, false);
            Lifts = lifts.AddComponent<LiftViewManager>();
            Lifts.Construct(this);

            var guests = new GameObject("Guests");
            guests.transform.SetParent(WorldRoot, false);
            Guests = guests.AddComponent<GuestView>();
            Guests.Construct(this);

            if (_pistePanel == null) _pistePanel = Ui.RegisterWindow(new PistePanel(), 1, 980f, 520f);
            if (_liftPanel == null) _liftPanel = Ui.RegisterWindow(new LiftPanel(), 3, 1000f, 480f);
            if (_ledgerPanel == null) _ledgerPanel = Ui.RegisterWindow(new LedgerPanel(), 4, 1000f, 600f);
            if (!_m3HudBound)
            {
                _m3HudBound = true;
                Ui.Hud.AddRightProvider(EconomyLine);
                Ui.Hud.AddStatusProvider(ResortLine);
            }
        }

        partial void TeardownM3()
        {
            Lifts = null;
            Guests = null;
        }

        private string EconomyLine()
        {
            if (Sim == null) return "";
            var e = Sim.World.Economy;
            string cash = UiFactory.Money(e.Cash);
            return "<b>" + (e.Cash < 0 ? UiFactory.ColorTag(UiFactory.Bad, cash) : cash) + "</b>  Act " + e.Act + "  PQI " + PistePanel.PqiTag(Sim.World.Pistes.ResortPqi) + "  rep " + UiFactory.F(Sim.World.Guests.Reputation, 0);
        }

        private string ResortLine()
        {
            if (Sim == null) return "";
            var g = Sim.World.Guests;
            var w = Sim.World.Weather.Current;
            string weather = UiFactory.F(w.TempC, 0) + " C, wind " + UiFactory.F(w.WindKmh, 0) + " km/h" + (w.SnowfallCmPerHour > 0.05f ? ", snowing " + UiFactory.F(w.SnowfallCmPerHour, 1) + " cm/h" : (w.PrecipMmPerHour > 0.05f ? ", rain" : ""));
            if (Sim.TryGetSystem<GuestSystem>(out var gs) && gs.IsOpen(Sim.Ctx))
                return "Guests on mountain " + g.GuestsOnMountain + " (arrived " + g.TodayArrived + ", demand " + g.TodayDemand + ")  satisfaction " + UiFactory.F(gs.LiveSatisfaction(Sim.Ctx) * 100f, 0) + "%\n" + weather;
            return "Resort closed. " + weather;
        }
    }
}
