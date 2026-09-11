using AlpineSim.Core.Economy;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Vehicles;
using NUnit.Framework;

namespace AlpineSim.Tests.Economy
{
    /// <summary>
    /// The Act IV trap: replacing the fixed quad with a detachable without adding grooming moves more
    /// skiers onto the same runs. They chop the snow faster, PQI falls, satisfaction and demand follow,
    /// and the bigger lift costs more to run: after thirty days the resort nets less, not more.
    /// </summary>
    [TestFixture]
    public sealed class ActFourTrapTests
    {
        /// <summary>
        /// Silberhorn with the quad as the given type. The detachable is an upgrade the resort has to pay
        /// for, so its run borrows the capex difference over the default loan term; grooming is whatever the
        /// night foreman gets out of the scenario's two cats in both runs.
        /// </summary>
        private static Simulation Run(string quadType, int seed)
        {
            var data = TestEnv.FreshData();
            data.Tuning.Override("simulation.guestsPerAgent", 40f);
            // the comparison is about capacity against grooming, not about which run happened to lose a cat
            // to a random breakdown or an operator accident, so both are switched off for both runs
            foreach (var v in data.Vehicles) v.MtbfHours = 1e6f;
            data.Operators.LicensedAccidentProbabilityPerHour = 0f;
            var scen = data.GetScenario("default");
            foreach (var l in scen.Lifts) if (l.Id == "quad") l.TypeId = quadType;
            // an Act IV resort has a driver for each of its two cats; the same two cats groom in both runs
            foreach (var so in scen.StartingOperators) if (so.Name.StartsWith("Tobias") && !so.Licenses.Contains(OperatorLicense.Groomer)) so.Licenses.Add(OperatorLicense.Groomer);
            var sim = Simulation.CreateNew(data, seed, "default");
            sim.World.Economy.Act = 4; // the Act IV decision: both runs at Act IV demand and credit
            if (quadType != "fixed_quad")
            {
                var quad = sim.World.Lifts.Lifts.Find(l => l.TypeId == quadType);
                var baseline = Core.Lifts.LiftSystem.Capex(sim.Ctx, data.RequireLiftType("fixed_quad"), quad.LengthM, quad.Towers.Count, quad.Carriers, quad.Options);
                double upgrade = quad.CapexTotal - baseline;
                Assert.Greater(upgrade, 0, "a detachable must cost more than a fixed grip");
                var eco = sim.GetSystem<EconomySystem>();
                var loan = eco.TakeLoan(sim.Ctx, upgrade, data.Economy.LoanTermDaysDefault, "detachable upgrade", out string reason);
                Assert.IsNotNull(loan, reason);
                sim.World.Economy.Cash -= upgrade; // the money went to the lift builder
            }
            sim.StepDays(30);
            return sim;
        }

        [Test]
        public void DetachableWithoutGroomingNetsLessByDayThirty()
        {
            var fixedRun = Run("fixed_quad", 91);
            var detachRun = Run("detachable_quad", 91);
            var eF = fixedRun.World.Economy;
            var eD = detachRun.World.Economy;
            Assert.GreaterOrEqual(eF.Daily.Count, 30);
            Assert.GreaterOrEqual(eD.Daily.Count, 30);
            double netF = 0, netD = 0;
            for (int d = 20; d < 30; d++) { netF += eF.Daily[d].Net; netD += eD.Daily[d].Net; }
            // the snow the guests actually skied on over the last ten days, not the resort average after the night's grooming
            var hF = fixedRun.World.Guests.History; var hD = detachRun.World.Guests.History;
            Assert.GreaterOrEqual(hF.Count, 30); Assert.GreaterOrEqual(hD.Count, 30);
            float pqiF = 0f, pqiD = 0f;
            for (int d = 20; d < 30; d++) { pqiF += hF[d].AvgPqi; pqiD += hD[d].AvgPqi; }
            Assert.Less(pqiD, pqiF, "more uphill capacity on the same runs should leave the snow the guests ski on worse (fixed " + (pqiF / 10f).ToString("0.0") + ", detachable " + (pqiD / 10f).ToString("0.0") + ")");
            Assert.Less(netD, netF, "the detachable should net less over days 21-30 without extra grooming (fixed " + EconomySystem.Money(netF) + ", detachable " + EconomySystem.Money(netD) + ")");
        }
    }
}
