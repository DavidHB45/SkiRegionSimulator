using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Save;
using AlpineSim.Core.Serialization;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Snow;
using NUnit.Framework;

namespace AlpineSim.Tests.Snow
{
    public class SnowGridTests
    {
        private static Simulation NewSim(int seed = 11) => Simulation.CreateNew(TestEnv.Data, seed, "test_small");

        [Test]
        public void EnvelopeCoversPistesAndZonesOnly()
        {
            var sim = NewSim();
            var g = sim.World.Snow;
            var net = sim.World.Pistes;
            Assert.AreEqual(2, net.Pistes.Count);
            Assert.AreEqual(6, net.Segments.Count);
            Assert.Greater(g.ChunkCount, 10);
            Assert.Less(g.ChunkCount, 256, "envelope must be sparse, not the whole map");
            int onPiste = g.CellIdAt(new Vec2(256f, 300f));
            Assert.GreaterOrEqual(onPiste, 0);
            Assert.AreEqual(SurfaceType.Piste, g.SurfaceOf(onPiste));
            Assert.GreaterOrEqual(g.Segment[onPiste], 0);
            Assert.AreEqual(-1, g.CellIdAt(new Vec2(60f, 480f)), "far off-piste is not allocated");
            foreach (var seg in net.Segments) Assert.Greater(seg.Cells.Count, 100, "segment " + seg.Id + " has cells");
            Assert.AreEqual(SurfaceType.Lot, g.SurfaceOf(g.CellIdAt(new Vec2(210f, 40f))));
            Assert.AreEqual(SurfaceType.Road, g.SurfaceOf(g.CellIdAt(new Vec2(256f, 30f))));
        }

        [Test]
        public void BladePushConservesMass()
        {
            var sim = NewSim();
            var g = sim.World.Snow;
            double before = g.TotalMassKg();
            // A blade pass: cut 80 mm from a strip of cells and drop it on the cells in front.
            var t = TestEnv.Data.Tuning;
            for (int i = 0; i < 40; i++)
            {
                int src = g.CellIdAt(new Vec2(250f + i * 0.5f, 300f));
                int dst = g.CellIdAt(new Vec2(250f + i * 0.5f, 302f));
                float mass = SnowOps.CutDepth(g, src, 80f, out float density);
                SnowOps.DepositPacked(g, dst, mass, density);
            }
            // and a few generic moves
            SnowOps.Move(g, g.CellIdAt(new Vec2(256f, 250f)), g.CellIdAt(new Vec2(256f, 251f)), 30f);
            double after = g.TotalMassKg();
            Assert.AreEqual(before, after, before * 1e-6, "blade moves snow, it does not create it");
        }

        [Test]
        public void CompactionConvergesToCorduroyThenOvergroomsToIce()
        {
            var sim = NewSim();
            var g = sim.World.Snow;
            var t = TestEnv.Data.Tuning;
            int id = g.CellIdAt(new Vec2(256f, 300f));
            // start from deep fresh snow on a thin base
            g.LooseMm[id] = 300f; g.PackedMm[id] = 100f; g.Density[id] = 250f; g.Roughness[id] = 0.6f;
            float lo = t.F("snow.corduroyDensityLo"), hi = t.F("snow.corduroyDensityHi");
            float target = t.F("snow.tillerTargetDensity");
            float comp = t.F("snow.tillerCompactionPerPass");
            double massBefore = g.MassKgPerM2(id);
            var densities = new List<float>();
            for (int pass = 1; pass <= 40; pass++)
            {
                SnowOps.Compact(g, id, 6f, 1f, t);
                SnowOps.Till(g, id, target, 0.6f, comp, 0.9f, 0, pass * 100, t);
                densities.Add(g.Density[id]);
            }
            Assert.AreEqual(massBefore, g.MassKgPerM2(id), 1e-3, "tilling conserves mass");
            Assert.IsTrue(densities[3] >= lo && densities[3] <= hi, "after 4 passes density is in the corduroy band: " + densities[3]);
            Assert.IsTrue(densities[5] >= lo && densities[5] <= hi, "after 6 passes still in band: " + densities[5]);
            Assert.Greater(densities[39], hi, "40 passes over-groom past the band into ice: " + densities[39]);
            for (int i = 1; i < densities.Count; i++) Assert.GreaterOrEqual(densities[i], densities[i - 1] - 1e-3f, "density never drops while tilling dry snow");
            Assert.Less(g.Roughness[id], 0.01f, "tiller removes roughness");
        }

        [Test]
        public void MoreTrafficWithoutGroomingNeverRaisesPqi()
        {
            var sim = NewSim();
            var g = sim.World.Snow;
            var net = sim.World.Pistes;
            var t = TestEnv.Data.Tuning;
            var seg = net.Segments[1];
            // groom the segment once so freshness is defined
            foreach (var id in seg.Cells) SnowOps.Till(g, id, t.F("snow.tillerTargetDensity"), 0.6f, t.F("snow.tillerCompactionPerPass"), 0.9f, 0, 0, t);
            float prev = PqiCalculator.ComputeSegment(g, seg, 100, t);
            Assert.Greater(prev, 60f, "freshly groomed segment scores well: " + prev);
            var dir = seg.Direction;
            long tick = 100;
            // A busy day: ~300 passes on the centre lane, fewer toward the edges, spread over 12 hours.
            for (int round = 0; round < 60; round++)
            {
                foreach (var id in seg.Cells)
                {
                    float lat = System.Math.Abs(g.Lateral[id]);
                    float passes = lat < 0.35f ? 5f : (lat < 0.7f ? 2f : 0.5f);
                    SnowOps.SkierPass(g, id, passes, dir, t);
                }
                tick += SimTime.TicksPerMinute * 12;
                float now = PqiCalculator.ComputeSegment(g, seg, tick, t);
                Assert.LessOrEqual(now, prev + 1e-4f, "PQI rose without grooming at round " + round + ": " + prev + " -> " + now);
                prev = now;
            }
            Assert.Less(prev, 70f, "a busy day degrades a groomed run substantially (from ~92): " + prev);
        }

        [Test]
        public void SkierTrafficConservesMass()
        {
            var sim = NewSim();
            var g = sim.World.Snow;
            var seg = sim.World.Pistes.Segments[1];
            double before = g.TotalMassKg();
            var t = TestEnv.Data.Tuning;
            foreach (var id in seg.Cells) SnowOps.SkierPass(g, id, 20f, seg.Direction, t);
            Assert.AreEqual(before, g.TotalMassKg(), before * 1e-6);
        }

        [Test]
        public void SnowfallAddsMassAndMeltRemovesIt()
        {
            var sim = NewSim();
            var g = sim.World.Snow;
            var snow = sim.GetSystem<SnowSystem>();
            var w = sim.World.Weather.Current;
            double m0 = g.TotalMassKg();
            w.SnowfallCmPerHour = 3f; w.TempC = -8f; w.SolarFrac = 0f; w.WindKmh = 0f;
            for (int c = 0; c < g.ChunkCount; c++) snow.ProcessChunk(sim.Ctx, c, 1f);
            double m1 = g.TotalMassKg();
            Assert.Greater(m1, m0);
            w.SnowfallCmPerHour = 0f; w.TempC = 6f; w.SolarFrac = 1f;
            for (int h = 0; h < 6; h++) for (int c = 0; c < g.ChunkCount; c++) snow.ProcessChunk(sim.Ctx, c, 1f);
            Assert.Less(g.TotalMassKg(), m1);
        }

        [Test]
        public void HourlyPqiIsPublishedAndTickIsCheap()
        {
            var sim = NewSim();
            int published = 0;
            sim.Events.Subscribe<PqiPublishedEvent>(e => published++);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            sim.StepHours(1);
            sw.Stop();
            Assert.Greater(published, 0);
            Assert.Greater(sim.World.Pistes.ResortPqi, 0f);
            Assert.Less(sw.ElapsedMilliseconds, 4000, "one sim hour of an idle small resort must be fast: " + sw.ElapsedMilliseconds + " ms");
        }

        [Test]
        public void GridSurvivesSaveLoadExactly()
        {
            var sim = NewSim();
            var g = sim.World.Snow;
            var t = TestEnv.Data.Tuning;
            foreach (var id in sim.World.Pistes.Segments[0].Cells) SnowOps.Till(g, id, 540f, 0.6f, 20f, 0.9f, 37, 4242, t);
            sim.StepMinutes(2);
            ulong h0 = sim.ComputeStateHash();
            string json = SaveSystem.Serialize(sim.World);
            var world = SaveSystem.Deserialize(json);
            var sim2 = Simulation.FromState(TestEnv.Data, world);
            Assert.AreEqual(h0, sim2.ComputeStateHash());
            Assert.AreEqual(g.ChunkCount, sim2.World.Snow.ChunkCount);
            int id0 = sim.World.Pistes.Segments[0].Cells[10];
            Assert.AreEqual(g.Density[id0], sim2.World.Snow.Density[id0]);
            Assert.AreEqual(sim.World.Pistes.Segments[0].Cells.Count, sim2.World.Pistes.Segments[0].Cells.Count, "derived cell lists rebuilt on load");
            sim.StepMinutes(1);
            sim2.StepMinutes(1);
            Assert.AreEqual(sim.ComputeStateHash(), sim2.ComputeStateHash(), "loaded world continues identically");
        }
    }
}
