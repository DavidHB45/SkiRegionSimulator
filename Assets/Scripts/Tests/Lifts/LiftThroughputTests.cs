using System.Collections.Generic;
using AlpineSim.Core.Lifts;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;
using NUnit.Framework;

namespace AlpineSim.Tests.Lifts
{
    /// <summary>Every lift type moves its rated capacity through a saturated queue, within 5 %.</summary>
    [TestFixture]
    public sealed class LiftThroughputTests
    {
        [Test]
        public void SaturatedQueueLoadsRatedCapacityForEveryType()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 41, "test_small");
            var ctx = sim.Ctx;
            var ls = sim.GetSystem<LiftSystem>();
            sim.StepUntilHour(10); // daytime, so the resort is open and lifts may run
            // pin calm, warm-enough weather so no lift goes on hold during the hour
            var w = sim.World.Weather.Current;
            w.WindKmh = 0f; w.Lightning = false; w.TempC = -5f;
            foreach (var s in sim.World.Weather.Timeline) { s.WindKmh = 0f; s.Lightning = false; s.TempC = -5f; }
            var built = new List<LiftState>();
            int i = 0;
            foreach (var type in TestEnv.Data.LiftTypes)
            {
                // short, gentle lines so ride time is small relative to the hour; each on its own strip of the map
                float x = 40f + (i++ % 12) * 38f;
                float y0 = 120f + (i / 12) * 160f;
                var lift = ls.Build(ctx, type.Id, new Vec2(x, y0), new Vec2(x, y0 + 120f), "TP " + type.Id, new List<string>(), "tp" + i, true);
                lift.Condition = 1f;
                ls.SetOpen(ctx, lift.Id, true);
                built.Add(lift);
            }
            int agent = 100000;
            long ticks = SimTime.TicksPerHour;
            for (long t = 0; t < ticks; t++)
            {
                // keep every queue saturated: top up whenever it drops below a few carriers' worth
                foreach (var l in built)
                {
                    var type = TestEnv.Data.LiftType(l.TypeId);
                    int target = type.SeatsOrCabinCapacity * 6 + 20;
                    while (l.QueueGuests < target) ls.Enqueue(ctx, l.Id, agent++, 1);
                }
                sim.Step();
                if (t % SimTime.TicksPerMinute == 0)
                {
                    var cur = sim.World.Weather.Current; cur.WindKmh = 0f; cur.Lightning = false; cur.TempC = -5f;
                }
            }
            var failures = new List<string>();
            foreach (var l in built)
            {
                var type = TestEnv.Data.LiftType(l.TypeId);
                Assert.AreEqual(LiftStatus.Open, l.Status, type.Id + " should have stayed open: " + l.StatusReason);
                float expected = ls.EffectiveCapacityPph(ctx, l);
                float ratio = l.LoadedToday / expected;
                if (ratio < 0.95f || ratio > 1.05f) failures.Add(type.Id + ": loaded " + l.LoadedToday + " of " + expected + " pph (" + (ratio * 100f).ToString("0") + "%)");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures.ToArray()));
        }
    }
}
