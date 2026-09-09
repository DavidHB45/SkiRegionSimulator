using AlpineSim.Core.Sim;
using NUnit.Framework;

namespace AlpineSim.Tests.Foundation
{
    public class SimTimeTests
    {
        [Test]
        public void CalendarDerivesFromTicks()
        {
            var t = new SimTime { StartHour = 18, StartDayOfWeek = 4 };
            Assert.AreEqual(0, t.Day);
            Assert.AreEqual(18, t.HourOfDay);
            Assert.AreEqual(4, t.DayOfWeek);
            t.Tick = SimTime.TicksPerHour * 6;
            Assert.AreEqual(1, t.Day);
            Assert.AreEqual(0, t.HourOfDay);
            Assert.IsTrue(t.IsDayStart);
            Assert.IsTrue(t.IsHourStart);
            Assert.AreEqual(5, t.DayOfWeek);
            t.Tick += SimTime.TicksPerMinute * 90 + 5;
            Assert.AreEqual(1, t.HourOfDay);
            Assert.AreEqual(30, t.MinuteOfHour);
            Assert.IsFalse(t.IsHourStart);
        }

        [Test]
        public void ClockConsumesTicksNotDt()
        {
            var data = TestEnv.Data;
            var sim = Simulation.CreateNew(data, 1, "test_small");
            var clock = new SimulationClock(sim);
            clock.Advance(0.5);
            Assert.AreEqual(10, sim.World.Time.Tick);
            clock.SetCompression(60);
            clock.Advance(0.1);
            Assert.AreEqual(10 + 120, sim.World.Time.Tick);
            clock.Paused = true;
            clock.Advance(10);
            Assert.AreEqual(130, sim.World.Time.Tick);
            clock.Paused = false;
            clock.SetCompression(1);
            clock.Advance(1000); // slow frame: capped, never a death spiral
            Assert.AreEqual(130 + clock.MaxTicksPerAdvance, sim.World.Time.Tick);
        }

        [Test]
        public void HourAndDayEventsFire()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 3, "test_small");
            int hours = 0, days = 0;
            sim.Events.Subscribe<HourChangedEvent>(e => hours++);
            sim.Events.Subscribe<DayChangedEvent>(e => days++);
            sim.StepHours(7); // 18:00 -> 01:00 next day
            Assert.AreEqual(7, hours);
            Assert.AreEqual(1, days);
        }
    }
}
