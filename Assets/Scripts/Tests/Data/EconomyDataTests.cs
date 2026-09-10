using System.Collections.Generic;
using AlpineSim.Core.Pistes;
using NUnit.Framework;

namespace AlpineSim.Tests.Data
{
    /// <summary>Schema and balance invariants of guests.json, economy.json and climate.json.</summary>
    public class EconomyDataTests
    {
        [Test]
        public void GuestsHaveSevenArchetypesWithSharesNearOneHundred()
        {
            var g = TestEnv.Data.Guests;
            Assert.AreEqual(7, g.Archetypes.Count, "guests.json defines exactly 7 archetypes");
            var ids = new HashSet<string>();
            float share = 0f;
            foreach (var a in g.Archetypes)
            {
                Assert.IsTrue(ids.Add(a.Id), "duplicate archetype " + a.Id);
                Assert.Greater(a.Share, 0f, a.Id + " share");
                share += a.Share;
                Assert.LessOrEqual((int)a.MinDifficulty, (int)a.PreferredDifficulty, a.Id + " min <= preferred difficulty");
                Assert.LessOrEqual((int)a.PreferredDifficulty, (int)a.MaxDifficulty, a.Id + " preferred <= max difficulty");
                Assert.LessOrEqual((int)a.MaxDifficulty, (int)PisteDifficulty.Black, a.Id + " uses Green..Black");
                Assert.GreaterOrEqual(a.SkiSpeedMs, 5f, a.Id + " ski speed");
                Assert.LessOrEqual(a.SkiSpeedMs, 14f, a.Id + " ski speed");
                Assert.Greater(a.LapsPerDayTarget, 0, a.Id + " laps");
                Assert.Greater(a.StayHours, 0f, a.Id + " stay");
                Assert.Greater(a.QueuePatienceMin, 0f, a.Id + " patience");
                Assert.Greater(a.WeekendMultiplier, 0f, a.Id + " weekend multiplier");
                Assert.Greater(a.HolidayMultiplier, 0f, a.Id + " holiday multiplier");
                Assert.GreaterOrEqual(a.RentalProbability, 0f, a.Id); Assert.LessOrEqual(a.RentalProbability, 1f, a.Id);
                Assert.GreaterOrEqual(a.LessonProbability, 0f, a.Id); Assert.LessOrEqual(a.LessonProbability, 1f, a.Id);
                Assert.GreaterOrEqual(a.ParkingProbability, 0f, a.Id); Assert.LessOrEqual(a.ParkingProbability, 1f, a.Id);
                Assert.IsFalse(string.IsNullOrEmpty(a.Comment), a.Id + " has a comment");
            }
            Assert.GreaterOrEqual(share, 95f, "shares sum to about 100");
            Assert.LessOrEqual(share, 105f, "shares sum to about 100");
            foreach (var id in new[] { "beginner", "family", "intermediate", "expert", "powder_hound", "park_rider", "season_local" })
                Assert.IsNotNull(g.Archetype(id), "archetype " + id + " exists");
            Assert.AreEqual(PisteDifficulty.Green, g.Archetype("beginner").MinDifficulty);
            Assert.AreEqual(PisteDifficulty.Black, g.Archetype("expert").MaxDifficulty);
            Assert.Less(g.Archetype("season_local").WeekendMultiplier, 1f, "locals avoid weekends");
            Assert.Greater(g.Archetype("family").HolidayMultiplier, 1.5f, "families fill holidays");
        }

        [Test]
        public void HolidaysCoverChristmasNewYearFebruaryAndEaster()
        {
            var days = TestEnv.Data.Guests.HolidaySeasonDays;
            for (int d = 19; d <= 31; d++) Assert.IsTrue(days.Contains(d), "season day " + d + " (Christmas/New Year) is a holiday");
            bool february = false, easter = false;
            foreach (var d in days)
            {
                Assert.GreaterOrEqual(d, 0);
                Assert.Less(d, TestEnv.Data.Climate.SeasonLengthDays, "holiday inside the season");
                if (d >= 58 && d <= 85) february = true;
                if (d >= 100 && d <= 135) easter = true;
            }
            Assert.IsTrue(february, "a February break");
            Assert.IsTrue(easter, "an Easter holiday");
            var sorted = new List<int>(days);
            sorted.Sort();
            CollectionAssert.AreEqual(sorted, days, "holiday days are listed in order");
            CollectionAssert.AllItemsAreUnique(days);
        }

        [Test]
        public void ActsProgressWithNonDecreasingThresholdsAndTiers()
        {
            var e = TestEnv.Data.Economy;
            Assert.AreEqual(5, e.Acts.Count, "economy.json defines five acts");
            for (int i = 0; i < e.Acts.Count; i++)
            {
                var a = e.Acts[i];
                Assert.AreEqual(i + 1, a.Act, "acts are numbered 1..5 in order");
                Assert.IsFalse(string.IsNullOrEmpty(a.DisplayName), "act " + a.Act + " has a name");
                Assert.Greater(a.MaxLoan, 0.0, "act " + a.Act + " loan ceiling");
                Assert.Greater(a.DemandBase, 0f, "act " + a.Act + " demand");
                Assert.Greater(a.TicketPriceBase, 0f, "act " + a.Act + " ticket");
                if (i == 0) continue;
                var prev = e.Acts[i - 1];
                Assert.GreaterOrEqual(a.MinNetWorth, prev.MinNetWorth, "net worth threshold never falls");
                Assert.GreaterOrEqual(a.MinReputation, prev.MinReputation, "reputation threshold never falls");
                Assert.GreaterOrEqual(a.MaxLiftTier, prev.MaxLiftTier, "lift tier never falls");
                Assert.GreaterOrEqual(a.MaxVehicleTier, prev.MaxVehicleTier, "vehicle tier never falls");
                Assert.GreaterOrEqual(a.MaxLoan, prev.MaxLoan, "loan ceiling never falls");
                Assert.GreaterOrEqual(a.TicketPriceBase, prev.TicketPriceBase, "ticket base never falls");
                Assert.GreaterOrEqual(a.DemandBase, prev.DemandBase, "demand base never falls");
            }
            Assert.AreEqual(1, e.Acts[0].MaxLiftTier, "Act I is surface lifts and the first chairs");
            Assert.AreEqual(5, e.Acts[4].MaxLiftTier, "Act V unlocks the whole ladder");
            Assert.AreEqual(5, e.Acts[4].MaxVehicleTier);
            Assert.AreSame(e.Acts[2], e.Act(3));
            Assert.AreSame(e.Acts[4], e.Act(9), "acts above five resolve to the last");
        }

        [Test]
        public void WagesAndPricesAreDefined()
        {
            var e = TestEnv.Data.Economy;
            foreach (var role in new[] { "liftOperator", "mechanic", "groomerOperator", "patrol", "foodBev", "rental", "instructor", "construction" })
            {
                bool found = false;
                foreach (var w in e.Wages)
                {
                    if (w.Role != role) continue;
                    found = true;
                    Assert.Greater(w.PerHour, 0f, role + " wage");
                    Assert.Greater(w.ShiftHours, 0f, role + " shift");
                }
                Assert.IsTrue(found, "wage for " + role);
            }
            Assert.Greater(e.Wage("mechanic"), e.Wage("liftOperator"), "a mechanic earns more than a lift attendant");
            Assert.Less(e.TicketPriceMin, e.Act(1).TicketPriceBase, "Act I ticket above the floor");
            Assert.Less(e.Act(1).TicketPriceBase, e.TicketPriceMax, "Act I ticket below the cap");
            Assert.Less(e.Act(5).TicketPriceBase, e.TicketPriceMax, "Act V ticket below the cap");
            Assert.Greater(e.HalfDayFactor, 0f); Assert.Less(e.HalfDayFactor, 1f);
            Assert.Greater(e.SeasonPassFactor, 1f);
            Assert.Greater(e.RentalPrice, 0f);
            Assert.Greater(e.LessonPrice, 0f);
            Assert.Greater(e.ParkingPrice, 0f);
            Assert.Greater(e.FoodMarginPct, 0f); Assert.Less(e.FoodMarginPct, 100f);
            Assert.Greater(e.LoanBaseRatePct, 0f);
            Assert.Greater(e.LoanTermDaysDefault, 0);
            Assert.Greater(e.ElectricityPricePerKwh, 0f);
            Assert.Greater(e.DieselPricePerL, 0f);
            Assert.Greater(e.ReputationLagDays, 0f);
            Assert.Greater(e.DemandPriceElasticity, 0f);
            Assert.Greater(e.RegionalCompetitionFactor, 0f); Assert.LessOrEqual(e.RegionalCompetitionFactor, 1f);
            Assert.Greater(e.OperatingHoursPerDay, 0f);
            Assert.IsFalse(string.IsNullOrEmpty(e.Comment));
        }

        [Test]
        public void ClimatePeriodsAreOrderedAndColdestInJanuary()
        {
            var c = TestEnv.Data.Climate;
            Assert.AreEqual("alpine", c.Id);
            Assert.AreEqual(150, c.SeasonLengthDays);
            Assert.GreaterOrEqual(c.Periods.Count, 8, "at least eight climate periods");
            Assert.AreEqual(0, c.Periods[0].SeasonDayStart, "the first period starts on season day 0");
            int coldest = 0;
            for (int i = 0; i < c.Periods.Count; i++)
            {
                var p = c.Periods[i];
                if (i > 0) Assert.Greater(p.SeasonDayStart, c.Periods[i - 1].SeasonDayStart, "periods are sorted by season day");
                Assert.Less(p.SeasonDayStart, c.SeasonLengthDays, p.Label + " starts inside the season");
                Assert.IsFalse(string.IsNullOrEmpty(p.Label), "period " + i + " has a label");
                Assert.IsFalse(string.IsNullOrEmpty(p.Comment), p.Label + " has a comment");
                Assert.Greater(p.DiurnalAmplitudeC, 0f, p.Label + " diurnal");
                Assert.Greater(p.TempStdDevC, 0f, p.Label + " std dev");
                Assert.GreaterOrEqual(p.PrecipDayProbability, 0f, p.Label); Assert.LessOrEqual(p.PrecipDayProbability, 1f, p.Label);
                Assert.Greater(p.PrecipMmPerDayMean, 0f, p.Label + " precipitation");
                Assert.Greater(p.WindMeanKmh, 0f, p.Label + " wind");
                Assert.GreaterOrEqual(p.WindGustFactor, 1f, p.Label + " gusts");
                Assert.GreaterOrEqual(p.WindDirMeanDeg, 250f, p.Label + " prevailing west-north-west");
                Assert.LessOrEqual(p.WindDirMeanDeg, 320f, p.Label + " prevailing west-north-west");
                Assert.GreaterOrEqual(p.CloudMean, 0f, p.Label); Assert.LessOrEqual(p.CloudMean, 1f, p.Label);
                Assert.GreaterOrEqual(p.LightningProbability, 0f, p.Label); Assert.LessOrEqual(p.LightningProbability, 1f, p.Label);
                Assert.Greater(p.HumidityMeanPct, 0f, p.Label); Assert.LessOrEqual(p.HumidityMeanPct, 100f, p.Label);
                if (p.MeanTempC < c.Periods[coldest].MeanTempC) coldest = i;
            }
            // day 0 = 5 December, so January is season days 27..57
            int coldStart = c.Periods[coldest].SeasonDayStart;
            Assert.GreaterOrEqual(coldStart, 27, "coldest period (" + c.Periods[coldest].Label + ") starts in January");
            Assert.LessOrEqual(coldStart, 57, "coldest period (" + c.Periods[coldest].Label + ") starts in January");
            Assert.Less(c.Periods[coldest].MeanTempC, c.Periods[0].MeanTempC, "January is colder than early December");
            Assert.Greater(c.Periods[c.Periods.Count - 1].MeanTempC, c.Periods[coldest].MeanTempC, "spring is warmer than January");
            Assert.Greater(c.TempAutocorrelation, 0f); Assert.Less(c.TempAutocorrelation, 1f);
            Assert.Greater(c.WindAutocorrelation, 0f); Assert.Less(c.WindAutocorrelation, 1f);
            Assert.Greater(c.StormPersistence, 0f); Assert.Less(c.StormPersistence, 1f);
            Assert.Greater(c.SnowToLiquidRatioCold, c.SnowToLiquidRatioWarm, "cold snow is lighter");
            Assert.Greater(c.ForecastErrorPerDayC, 0f);
            Assert.Greater(c.LapseRateCPer100m, 0f);
            Assert.AreEqual(1400f, c.ReferenceElevationM, 0.01f, "statistics at the Silberhorn base");
        }

        [Test]
        public void NoLoadWarningsAboutDataTables()
        {
            foreach (var w in TestEnv.Data.Warnings)
            {
                string lower = w.ToLowerInvariant();
                Assert.IsFalse(lower.Contains("lift"), "warning: " + w);
                Assert.IsFalse(lower.Contains("guest"), "warning: " + w);
                Assert.IsFalse(lower.Contains("econom"), "warning: " + w);
                Assert.IsFalse(lower.Contains("construction"), "warning: " + w);
                Assert.IsFalse(lower.Contains("climate"), "warning: " + w);
            }
        }
    }
}
