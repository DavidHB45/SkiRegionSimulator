using System.Collections.Generic;
using AlpineSim.Core.Construction;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;
using NUnit.Framework;

namespace AlpineSim.Tests.Construction
{
    /// <summary>Terrain gates what can be built: length, span between buildable tower sites, grade, and the valley.</summary>
    [TestFixture]
    public sealed class TerrainGatingTests
    {
        /// <summary>Silberhorn at Act V, so only terrain (not the act ladder) gates the plans.</summary>
        private static Simulation Silberhorn()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 61, "default");
            sim.World.Economy.Act = 5;
            return sim;
        }

        private static readonly Vec2 GorgeBottom = new Vec2(1180f, 450f);
        private static readonly Vec2 GorgeTop = new Vec2(1700f, 1700f);

        [Test]
        public void TBarCannotBeTooLong()
        {
            var sim = Silberhorn();
            var cs = sim.GetSystem<ConstructionSystem>();
            var plan = cs.Stake(sim.Ctx, "t_bar", new Vec2(1120f, 440f), new Vec2(930f, 1850f));
            Assert.IsFalse(plan.Ok);
            Assert.IsTrue(plan.Reasons.Exists(r => r.Contains("length")), string.Join("; ", plan.Reasons.ToArray()));
        }

        [Test]
        public void FixedGripCannotSpanTheValley()
        {
            var sim = Silberhorn();
            var cs = sim.GetSystem<ConstructionSystem>();
            var plan = cs.Stake(sim.Ctx, "fixed_quad", GorgeBottom, GorgeTop);
            Assert.IsFalse(plan.Ok);
            Assert.IsTrue(plan.CrossesGorge, "the east bowl line crosses the valley");
            Assert.IsTrue(plan.Reasons.Exists(r => r.Contains("span")), string.Join("; ", plan.Reasons.ToArray()));
        }

        [Test]
        public void SurfaceLiftCannotClimbTheNorthFace()
        {
            var sim = Silberhorn();
            var cs = sim.GetSystem<ConstructionSystem>();
            var plan = cs.Stake(sim.Ctx, "platter", new Vec2(986f, 470f), new Vec2(700f, 1620f));
            Assert.IsFalse(plan.Ok);
            Assert.IsTrue(plan.Reasons.Exists(r => r.Contains("grade") || r.Contains("length")), string.Join("; ", plan.Reasons.ToArray()));
            // an aerial tram on the same line is limited by nothing but money
            var tram = cs.Stake(sim.Ctx, "aerial_tram_80", new Vec2(986f, 470f), new Vec2(700f, 1620f));
            Assert.IsTrue(tram.Ok, string.Join("; ", tram.Reasons.ToArray()));
        }

        [Test]
        public void OnlyTricableAndTramsCrossTheValley()
        {
            var sim = Silberhorn();
            var cs = sim.GetSystem<ConstructionSystem>();
            var canBuild = new List<string>();
            foreach (var type in TestEnv.Data.LiftTypes)
            {
                var plan = cs.Stake(sim.Ctx, type.Id, GorgeBottom, GorgeTop);
                if (plan.Ok) canBuild.Add(type.Id);
            }
            canBuild.Sort();
            CollectionAssert.AreEqual(new[] { "aerial_tram_150", "aerial_tram_80", "tricable_3s" }, canBuild);
        }

        [Test]
        public void ABuildablePlanPricesTowersTerminalsLineAndCarriers()
        {
            var sim = Silberhorn();
            var cs = sim.GetSystem<ConstructionSystem>();
            var plan = cs.Stake(sim.Ctx, "detachable_quad", new Vec2(990f, 430f), new Vec2(930f, 1300f));
            Assert.IsTrue(plan.Ok, string.Join("; ", plan.Reasons.ToArray()));
            Assert.Greater(plan.Towers.Count, 4);
            Assert.Greater(plan.CapexTowers, 0); Assert.Greater(plan.CapexTerminals, 0); Assert.Greater(plan.CapexLine, 0); Assert.Greater(plan.CapexCarriers, 0);
            double parts = plan.CapexTowers + plan.CapexTerminals + plan.CapexLine + plan.CapexCarriers + plan.CapexBarn;
            Assert.Greater(plan.CapexTotal, parts + plan.ConcreteM3 * TestEnv.Data.Construction.ConcretePricePerM3, "total must add concrete, stages and inspection to the hardware");
            Assert.Less(plan.CapexTotal, parts * 1.5, "soft costs should not exceed half the hardware");
            Assert.Greater(plan.CapacityPph, 2000f);
            Assert.Greater(plan.EstimatedBuildDays, 5f);
        }

        [Test]
        public void RunsMustDescendAndStayOutOfTheValley()
        {
            var sim = Silberhorn();
            var cs = sim.GetSystem<ConstructionSystem>();
            var up = new List<Vec2> { new Vec2(992f, 450f), new Vec2(930f, 1300f) };
            Assert.IsFalse(cs.StakeRun(sim.Ctx, "r1", "uphill", Core.Pistes.PisteDifficulty.Blue, 40f, up, "", "", out _, out var reasons));
            Assert.IsTrue(reasons.Exists(r => r.Contains("descend") || r.Contains("climbs")), string.Join("; ", reasons.ToArray()));
            var across = new List<Vec2> { new Vec2(1700f, 1700f), new Vec2(1450f, 1150f), new Vec2(1190f, 450f) };
            Assert.IsFalse(cs.StakeRun(sim.Ctx, "r2", "gorge", Core.Pistes.PisteDifficulty.Red, 40f, across, "", "", out _, out reasons));
            Assert.IsTrue(reasons.Exists(r => r.Contains("valley")), string.Join("; ", reasons.ToArray()));
            var fine = new List<Vec2> { new Vec2(930f, 1300f), new Vec2(960f, 900f), new Vec2(1000f, 520f) };
            Assert.IsTrue(cs.StakeRun(sim.Ctx, "r3", "fine", Core.Pistes.PisteDifficulty.Blue, 40f, fine, "lift:quad:top", "lift:quad:bottom", out double cost, out reasons), string.Join("; ", reasons.ToArray()));
            Assert.Greater(cost, 10000);
        }
    }
}
