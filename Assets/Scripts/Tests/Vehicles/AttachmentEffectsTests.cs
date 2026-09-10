using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Vehicles;
using NUnit.Framework;

namespace AlpineSim.Tests.Vehicles
{
    /// <summary>Attachments change what a machine does: a 6.0 m tiller covers 40 % more run per pass than a 4.3 m one.</summary>
    [TestFixture]
    public sealed class AttachmentEffectsTests
    {
        private static float TilledAreaAfterPass(string tillerId, out float distanceM)
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 21, "test_small");
            var ctx = sim.Ctx;
            var vs = sim.GetSystem<VehicleSystem>();
            // a heavy cat has the hydraulics and power for both tillers; start on the lower part of run t1 heading up it
            var v = vs.Spawn(ctx, "groomer_heavy", new Vec2(256f, 140f), MathUtil.Pi * 0.5f, "Test heavy", 100f, 100f, 1f);
            Assert.IsTrue(vs.Mount(ctx, v.Id, tillerId, SlotPosition.Rear, out string reason), reason);
            Assert.IsTrue(vs.TryEnter(ctx, v.Id, out reason), reason);
            for (int i = 0; i < 40 && !v.EngineOn; i++) vs.StartEngine(ctx, v.Id, out _);
            Assert.IsTrue(v.EngineOn, "engine did not start");
            var start = v.Pos;
            var input = new VehicleInput { Throttle = 0.6f, Tiller = true, BladeLift = 1f };
            for (int t = 0; t < SimTime.TicksPerSecond * 150; t++)
            {
                vs.SetInput(ctx, v.Id, input);
                sim.Step();
                if (v.Pos.Y > 360f) break;
            }
            distanceM = Vec2.Distance(start, v.Pos);
            return v.TilledM2Today;
        }

        [Test]
        public void SixMetreTillerCoversMoreThanFourPointThree()
        {
            float narrow = TilledAreaAfterPass("tiller_4_3", out float dNarrow);
            float wide = TilledAreaAfterPass("tiller_6_0", out float dWide);
            Assert.Greater(dNarrow, 60f, "the cat barely moved with the 4.3 m tiller");
            Assert.Greater(dWide, 60f, "the cat barely moved with the 6.0 m tiller");
            Assert.Greater(narrow, 0f);
            float narrowPerM = narrow / dNarrow, widePerM = wide / dWide;
            float ratio = widePerM / narrowPerM;
            Assert.AreEqual(6.0f / 4.3f, ratio, 0.14f, "tilled area per metre should scale with working width (got " + ratio + ")");
            Assert.AreEqual(4.3f, narrowPerM, 4.3f * 0.25f, "4.3 m tiller should till about 4.3 m2 per metre travelled");
        }

        [Test]
        public void HeavyTillerNeedsAHeavyCat()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 22, "test_small");
            var vs = sim.GetSystem<VehicleSystem>();
            var mid = vs.Spawn(sim.Ctx, "groomer_mid", new Vec2(240f, 90f), 0f, "Mid", 0f, 100f, 1f);
            Assert.IsFalse(vs.Mount(sim.Ctx, mid.Id, "tiller_6_0", SlotPosition.Rear, out string reason));
            StringAssert.Contains("kW", reason);
        }

        [Test]
        public void MachinesInTheSameCategoryGroomDifferently()
        {
            // work rate and working width differ between a light and a flagship cat with their default kit
            var sim = Simulation.CreateNew(TestEnv.Data, 23, "test_small");
            var vs = sim.GetSystem<VehicleSystem>();
            var light = vs.Spawn(sim.Ctx, "groomer_light", new Vec2(240f, 90f), 0f, "Light", 0f, 100f, 1f);
            var flagship = vs.Spawn(sim.Ctx, "groomer_flagship", new Vec2(244f, 90f), 0f, "Flagship", 0f, 100f, 1f);
            float wl = vs.WorkingWidthM(sim.Ctx, light, VehicleRole.Groom);
            float wf = vs.WorkingWidthM(sim.Ctx, flagship, VehicleRole.Groom);
            Assert.Greater(wf, wl * 1.05f, "flagship should groom a wider lane than the light cat");
        }
    }
}
