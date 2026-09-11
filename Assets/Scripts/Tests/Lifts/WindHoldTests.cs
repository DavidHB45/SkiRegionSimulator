using System.Collections.Generic;
using AlpineSim.Core.Lifts;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;
using NUnit.Framework;

namespace AlpineSim.Tests.Lifts
{
    /// <summary>Lifts go on wind hold in the order of their wind limits: surface lifts and open chairs last, exposed detachables and gondolas first.</summary>
    [TestFixture]
    public sealed class WindHoldTests
    {
        [Test]
        public void HoldOrderFollowsWindLimits()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 51, "test_small");
            var ctx = sim.Ctx;
            var ls = sim.GetSystem<LiftSystem>();
            var lifts = new List<LiftState>();
            int i = 0;
            foreach (var type in TestEnv.Data.LiftTypes)
            {
                float x = 40f + (i++ % 12) * 38f;
                var l = ls.Build(ctx, type.Id, new Vec2(x, 120f), new Vec2(x, 240f), type.Id, new List<string>(), "wh" + i, true);
                lifts.Add(l);
            }
            // same line for everyone, so the effective limit is the type's limit plus its option bonus
            lifts.Sort((a, b) => ls.WindHoldKmh(ctx, a).CompareTo(ls.WindHoldKmh(ctx, b)));
            float min = ls.WindHoldKmh(ctx, lifts[0]), max = ls.WindHoldKmh(ctx, lifts[lifts.Count - 1]);
            Assert.Greater(max, min + 20f, "the fleet of lift types should span a wide band of wind limits");
            for (float wind = 0f; wind <= max + 10f; wind += 5f)
            {
                bool seenOpen = false;
                foreach (var l in lifts)
                {
                    bool hold = ls.WouldHold(ctx, l, wind, false, -5f, out string reason);
                    if (!hold) seenOpen = true;
                    else Assert.IsFalse(seenOpen, "at " + wind + " km/h " + l.TypeId + " (limit " + ls.WindHoldKmh(ctx, l) + ") holds while a lift with a lower limit is still open");
                    if (hold) Assert.AreEqual("wind hold", reason);
                }
            }
            Assert.IsFalse(ls.WouldHold(ctx, lifts[0], 0f, false, -5f, out _));
            Assert.IsTrue(ls.WouldHold(ctx, lifts[lifts.Count - 1], max + 1f, false, -5f, out _));
        }

        [Test]
        public void SurfaceLiftsToleratMoreWindThanGondolasAndLightningStopsEveryone()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 52, "test_small");
            var ctx = sim.Ctx;
            var ls = sim.GetSystem<LiftSystem>();
            var tbar = ls.Build(ctx, "t_bar", new Vec2(60f, 120f), new Vec2(60f, 240f), "tbar", new List<string>(), "w1", true);
            var gondola = ls.Build(ctx, "gondola_8", new Vec2(120f, 120f), new Vec2(120f, 240f), "gondola", new List<string>(), "w2", true);
            var quad = ls.Build(ctx, "detachable_quad", new Vec2(180f, 120f), new Vec2(180f, 240f), "dq", new List<string>(), "w3", true);
            Assert.Greater(ls.WindHoldKmh(ctx, tbar), ls.WindHoldKmh(ctx, quad), "a T-bar hugs the ground and holds later than a detachable chair");
            Assert.IsTrue(ls.WouldHold(ctx, gondola, 0f, true, -5f, out string reason));
            Assert.AreEqual("lightning hold", reason);
            Assert.IsTrue(ls.WouldHold(ctx, quad, 0f, false, -45f, out reason));
            StringAssert.Contains("cold", reason);
        }

        [Test]
        public void WindAtTheTopGrowsWithExposure()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 53, "test_small");
            var ctx = sim.Ctx;
            var ls = sim.GetSystem<LiftSystem>();
            var low = ls.Build(ctx, "fixed_quad", new Vec2(60f, 120f), new Vec2(60f, 200f), "low", new List<string>(), "e1", true);
            var high = ls.Build(ctx, "fixed_quad", new Vec2(120f, 120f), new Vec2(256f, 480f), "high", new List<string>(), "e2", true);
            Assert.GreaterOrEqual(ls.WindAtTopKmh(ctx, high, 30f), ls.WindAtTopKmh(ctx, low, 30f));
        }
    }
}
