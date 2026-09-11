using AlpineSim.Core.Save;
using AlpineSim.Core.Sim;
using NUnit.Framework;

namespace AlpineSim.Tests.Sim
{
    /// <summary>Same seed + same inputs = same WorldState hash, over a long run and across a save/load boundary.</summary>
    [TestFixture]
    public sealed class DeterminismTests
    {
        private const int Ticks = 10000;

        [Test]
        public void TenThousandTicksHashIdenticalForSameSeed()
        {
            var a = Simulation.CreateNew(TestEnv.Data, 4242, "test_small");
            var b = Simulation.CreateNew(TestEnv.Data, 4242, "test_small");
            a.StepTicks(Ticks);
            b.StepTicks(Ticks);
            Assert.AreEqual(a.ComputeStateHash(), b.ComputeStateHash(), "two runs with the same seed diverged");
            Assert.AreEqual(Ticks, a.World.Time.Tick);
        }

        [Test]
        public void DifferentSeedsProduceDifferentHashes()
        {
            var a = Simulation.CreateNew(TestEnv.Data, 1, "test_small");
            var b = Simulation.CreateNew(TestEnv.Data, 2, "test_small");
            a.StepTicks(2000);
            b.StepTicks(2000);
            Assert.AreNotEqual(a.ComputeStateHash(), b.ComputeStateHash());
        }

        [Test]
        public void SaveLoadMidRunContinuesOnTheSameTrajectory()
        {
            var straight = Simulation.CreateNew(TestEnv.Data, 99, "test_small");
            var interrupted = Simulation.CreateNew(TestEnv.Data, 99, "test_small");
            straight.StepTicks(Ticks);
            interrupted.StepTicks(Ticks / 2);
            string json = SaveSystem.Serialize(interrupted.World);
            var resumed = Simulation.FromState(TestEnv.Data, SaveSystem.Deserialize(json));
            resumed.StepTicks(Ticks - Ticks / 2);
            Assert.AreEqual(straight.ComputeStateHash(), resumed.ComputeStateHash(), "a save/load in the middle of a run changed the trajectory");
        }

        [Test]
        public void TickOrderIsCanonical()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 5, "test_small");
            int last = -1;
            foreach (var s in sim.Systems)
            {
                int rank = System.Array.IndexOf(Simulation.TickOrder, s.Name);
                Assert.IsTrue(rank >= 0, "system " + s.Name + " is not in Simulation.TickOrder");
                Assert.IsTrue(rank >= last, "system " + s.Name + " ticks out of order");
                last = rank;
            }
        }
    }
}
