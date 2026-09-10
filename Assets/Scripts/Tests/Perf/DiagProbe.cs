using AlpineSim.Core.Construction;
using AlpineSim.Core.Guests;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;
using NUnit.Framework;

namespace AlpineSim.Tests.Perf
{
    [TestFixture]
    public sealed class DiagProbe
    {
        [Test]
        public void Probe()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 72, "test_small");
            var o = TestContext.Progress;
            o.WriteLine("lifts: " + sim.World.Lifts.Lifts.Count + " warnings: " + string.Join(" | ", TestEnv.Data.Warnings.ToArray()));
            foreach (var l in sim.World.Lifts.Lifts) o.WriteLine(" lift " + l.Name + " " + l.TypeId + " status " + l.Status + " " + l.StatusReason + " bottom " + l.BottomNodeId + " top " + l.TopNodeId + " len " + l.LengthM);
            sim.StepUntilHour(12);
            var g = sim.World.Guests;
            o.WriteLine("12:00 open=" + sim.GetSystem<GuestSystem>().IsOpen(sim.Ctx) + " demand " + g.TodayDemand + " arrived " + g.TodayArrived + " agents " + g.Agents.Count + " onMountain " + g.GuestsOnMountain + " rep " + g.Reputation + " openToday " + g.ResortOpenToday);
            foreach (var l in sim.World.Lifts.Lifts) o.WriteLine(" lift " + l.Name + " status " + l.Status + " " + l.StatusReason + " queue " + l.QueueGuests + " loaded " + l.LoadedToday);
            var phases = new System.Collections.Generic.Dictionary<GuestPhase, int>();
            foreach (var a in g.Agents) { phases.TryGetValue(a.Phase, out int n); phases[a.Phase] = n + 1; }
            foreach (var kv in phases) o.WriteLine(" phase " + kv.Key + " " + kv.Value);
            foreach (var e in sim.World.Log) o.WriteLine(" log: " + e.Message);
            var w = sim.World.Weather.Current; o.WriteLine(" weather " + w.TempC + " C wind " + w.WindKmh + " lightning " + w.Lightning);

            var d = Simulation.CreateNew(TestEnv.Data, 61, "default"); d.World.Economy.Act = 5;
            var cs = d.GetSystem<ConstructionSystem>();
            foreach (var t in new[] { "t_bar", "platter", "detachable_quad", "tricable_3s", "aerial_tram_80", "fixed_quad" })
            {
                var p = cs.Stake(d.Ctx, t, new Vec2(1180f, 450f), new Vec2(1700f, 1700f));
                o.WriteLine(t + " gorge: ok=" + p.Ok + " len " + p.LengthM + " vert " + p.VerticalM + " span " + p.MaxSpanM + " grade " + p.MaxGradeDeg + " :: " + string.Join("; ", p.Reasons.ToArray()));
                p = cs.Stake(d.Ctx, t, new Vec2(990f, 430f), new Vec2(930f, 1300f));
                o.WriteLine(t + " quad line: ok=" + p.Ok + " len " + p.LengthM + " vert " + p.VerticalM + " span " + p.MaxSpanM + " grade " + p.MaxGradeDeg + " :: " + string.Join("; ", p.Reasons.ToArray()));
            }
        }
    }
}
