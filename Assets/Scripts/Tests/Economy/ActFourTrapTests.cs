using AlpineSim.Core.Economy;
using AlpineSim.Core.Sim;
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
        private static Simulation Run(string quadType, int seed)
        {
            var data = TestEnv.FreshData();
            data.Tuning.Override("simulation.guestsPerAgent", 40f);
            var scen = data.GetScenario("default");
            foreach (var l in scen.Lifts) if (l.Id == "quad") l.TypeId = quadType;
            var sim = Simulation.CreateNew(data, seed, "default");
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
            float pqiF = fixedRun.World.Pistes.ResortPqi, pqiD = detachRun.World.Pistes.ResortPqi;
            Assert.Less(pqiD, pqiF, "more uphill capacity on ungroomed runs should leave the snow worse");
            Assert.Less(netD, netF, "the detachable should net less over days 21-30 without extra grooming (fixed " + EconomySystem.Money(netF) + ", detachable " + EconomySystem.Money(netD) + ")");
        }
    }
}
