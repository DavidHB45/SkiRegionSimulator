using AlpineSim.Core.Terrain;
using NUnit.Framework;

namespace AlpineSim.Tests.Foundation
{
    public class TerrainTests
    {
        [Test]
        public void GenerationIsDeterministic()
        {
            var s = TestEnv.Data.GetScenario("test_small");
            var a = TerrainGenerator.Build(s, 123);
            var b = TerrainGenerator.Build(s, 123);
            CollectionAssert.AreEqual(a.Heights, b.Heights);
            var c = TerrainGenerator.Build(s, 124);
            CollectionAssert.AreNotEqual(a.Heights, c.Heights);
        }

        [Test]
        public void MountainRisesTowardRidgeAndFlatsAreFlat()
        {
            var s = TestEnv.Data.GetScenario("default");
            var t = TerrainGenerator.Build(s, 20260909);
            float baseH = t.SampleHeight(s.BaseArea.X, s.BaseArea.Y);
            float ridgeH = t.SampleHeight(1024, s.Terrain.RidgeY);
            Assert.Greater(ridgeH - baseH, 600f, "ridge must be well above the base");
            Assert.Less(t.SlopeDeg(s.BaseArea.X, s.BaseArea.Y), 3f, "base area is engineered flat");
            Assert.IsTrue(t.HasFlag(s.BaseArea.X, s.BaseArea.Y, TerrainFlags.Flat));
            // gorge is impassable and unbuildable
            var g = s.Terrain.Gorge;
            Assert.IsTrue(t.HasFlag(1600, g.Y, TerrainFlags.Gorge));
            Assert.IsTrue(t.HasFlag(1600, g.Y, TerrainFlags.NoFoundation));
            Assert.IsFalse(t.HasFlag(600, g.Y, TerrainFlags.Gorge), "gorge does not cross the western runs");
            Assert.Less(t.SampleHeight(1600, g.Y), t.SampleHeight(1600, g.Y + g.WidthM * 1.5f) - 100f);
        }

        [Test]
        public void SamplingIsContinuous()
        {
            var t = TerrainGenerator.Build(TestEnv.Data.GetScenario("test_small"), 5);
            float prev = t.SampleHeight(100.0f, 100.0f);
            for (int i = 1; i <= 100; i++)
            {
                float h = t.SampleHeight(100.0f + i * 0.1f, 100.0f);
                Assert.Less(System.Math.Abs(h - prev), 1.0f);
                prev = h;
            }
            var n = t.Normal(200, 200);
            Assert.AreEqual(1f, n.Length, 1e-4f);
            Assert.Greater(n.Z, 0.3f);
        }
    }
}
