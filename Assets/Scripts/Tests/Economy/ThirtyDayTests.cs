using System;
using AlpineSim.Core.Economy;
using AlpineSim.Core.Sim;
using NUnit.Framework;

namespace AlpineSim.Tests.Economy
{
    /// <summary>Thirty days of Silberhorn with the books closing every night; every entry must reconcile.</summary>
    [TestFixture]
    public sealed class ThirtyDayTests
    {
        private static Simulation RunThirtyDays(int seed, Action<Core.Data.GameData> configure = null)
        {
            var data = TestEnv.FreshData();
            data.Tuning.Override("simulation.guestsPerAgent", 40f); // coarser cohorts keep the test fast; the loop is the same
            configure?.Invoke(data);
            var sim = Simulation.CreateNew(data, seed, "default");
            sim.StepDays(30);
            return sim;
        }

        [Test]
        public void BooksCloseEveryDayAndReconcile()
        {
            var sim = RunThirtyDays(81);
            var e = sim.World.Economy;
            Assert.GreaterOrEqual(e.Daily.Count, 30, "thirty daily closes expected");
            double runningCash = -1;
            foreach (var d in e.Daily)
            {
                Assert.AreEqual(d.Revenue - d.Costs, d.Net, 0.01, "day " + (d.Day + 1) + " net must equal revenue minus costs");
                Assert.AreEqual(d.CashStart + d.Net, d.CashEnd, 0.01, "day " + (d.Day + 1) + " cash must move by the net");
                if (runningCash >= 0) Assert.AreEqual(runningCash, d.CashStart, 0.01, "day " + (d.Day + 1) + " must open with yesterday's close");
                runningCash = d.CashEnd;
                double byCat = 0; foreach (var kv in d.ByCategory) byCat += kv.Value;
                Assert.AreEqual(d.Net, byCat, 0.01, "day " + (d.Day + 1) + " categories must sum to the net");
                Assert.Greater(d.Costs, 0, "a resort always has costs (day " + (d.Day + 1) + ")");
            }
            double ledgerSum = 0; foreach (var en in e.Ledger) ledgerSum += en.Amount;
            Assert.AreEqual(e.Cash - sim.Scenario.StartCash, ledgerSum, Math.Max(1.0, Math.Abs(e.Cash) * 1e-6), "the retained ledger must explain the cash movement");
            int daysWithGuests = 0; foreach (var d in e.Daily) if (d.Guests > 0) daysWithGuests++;
            Assert.GreaterOrEqual(daysWithGuests, 20, "guests should come most days");
            double revenue = 0; foreach (var d in e.Daily) revenue += d.Revenue;
            Assert.Greater(revenue, 0);
            Assert.AreEqual(revenue, e.Season.Revenue, Math.Max(1.0, revenue * 1e-6));
            Assert.AreEqual(e.Daily.Count, e.DaysInBusiness);
        }

        [Test]
        public void WeeklyReportsCoverTheMonth()
        {
            var sim = RunThirtyDays(82);
            var e = sim.World.Economy;
            Assert.GreaterOrEqual(e.Weekly.Count, 4);
            foreach (var w in e.Weekly)
            {
                Assert.AreEqual(w.Revenue - w.Costs, w.Net, 0.01);
                Assert.AreEqual(7, w.DayEnd - w.DayStart + 1);
            }
        }
    }
}
