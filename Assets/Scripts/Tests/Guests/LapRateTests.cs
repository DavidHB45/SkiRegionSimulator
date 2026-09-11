using AlpineSim.Core.Guests;
using AlpineSim.Core.Sim;
using NUnit.Framework;

namespace AlpineSim.Tests.Guests
{
    /// <summary>Lap rate couples to uphill capacity: starve the quad and guests ski fewer laps and wait longer (Silberhorn, one day).</summary>
    [TestFixture]
    public sealed class LapRateTests
    {
        private static DailyGuestReport RunOneDay(float capacityFactor, out float lapsPerGuest)
        {
            var data = TestEnv.FreshData();
            foreach (var type in data.LiftTypes) type.CapacityPph *= capacityFactor; // every lift, so guests cannot sidestep the queue on another one
            var sim = Simulation.CreateNew(data, 71, "default");
            sim.StepUntilHour(17); // through the first full resort day
            var history = sim.World.Guests.History;
            Assert.IsTrue(history.Count >= 1, "no guest day closed");
            var report = history[history.Count - 1];
            Assert.Greater(report.Arrived, 0, "nobody came");
            lapsPerGuest = report.Laps / (float)report.Arrived;
            return report;
        }

        [Test]
        public void StarvingLiftCapacityCutsLapsAndLengthensQueues()
        {
            var full = RunOneDay(1f, out float lapsFull);
            var half = RunOneDay(0.15f, out float lapsHalf);
            Assert.Greater(lapsFull, 0.5f, "guests should complete laps on a normal day");
            Assert.Less(lapsHalf, lapsFull * 0.9f, "a fraction of the uphill capacity should cost at least 10 % of the laps (" + lapsHalf + " vs " + lapsFull + ")");
            Assert.Greater(half.AvgQueueMin, full.AvgQueueMin, "queues should lengthen when capacity halves");
        }

        [Test]
        public void GuestsRideTheLiftAndSkiTheRuns()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 72, "default");
            sim.StepUntilHour(10);
            sim.StepMinutes(30);
            int riding = 0, skiing = 0;
            foreach (var a in sim.World.Guests.Agents)
            {
                if (a.Phase == GuestPhase.Riding) riding++;
                if (a.Phase == GuestPhase.Skiing) skiing++;
            }
            Assert.Greater(riding + skiing, 0, "mid-morning somebody should be on the hill");
            long loaded = 0;
            foreach (var lift in sim.World.Lifts.Lifts) loaded += lift.LoadedToday;
            Assert.Greater(loaded, 0);
            float traffic = 0f;
            foreach (var p in sim.World.Pistes.Pistes) traffic += p.TrafficToday;
            Assert.Greater(traffic, 0f, "skiers should register traffic on the runs");
        }
    }
}
