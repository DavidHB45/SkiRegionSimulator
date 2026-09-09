using AlpineSim.Core.Random;
using NUnit.Framework;

namespace AlpineSim.Tests.Foundation
{
    public class RngTests
    {
        [Test]
        public void SameSeedSameSequence()
        {
            var a = new XorShift128Plus(42);
            var b = new XorShift128Plus(42);
            for (int i = 0; i < 1000; i++) Assert.AreEqual(a.NextULong(), b.NextULong());
        }

        [Test]
        public void DifferentSeedsDiffer()
        {
            var a = new XorShift128Plus(1);
            var b = new XorShift128Plus(2);
            int same = 0;
            for (int i = 0; i < 100; i++) if (a.NextULong() == b.NextULong()) same++;
            Assert.Less(same, 3);
        }

        [Test]
        public void RangesAreWithinBounds()
        {
            var r = new XorShift128Plus(7);
            for (int i = 0; i < 10000; i++)
            {
                float f = r.NextFloat();
                Assert.IsTrue(f >= 0f && f < 1f);
                int n = r.Range(-3, 4);
                Assert.IsTrue(n >= -3 && n < 4);
                float g = r.Range(2f, 5f);
                Assert.IsTrue(g >= 2f && g < 5f);
            }
        }

        [Test]
        public void GaussianHasUnitVariance()
        {
            var r = new XorShift128Plus(99);
            double sum = 0, sq = 0; int n = 20000;
            for (int i = 0; i < n; i++) { float g = r.NextGaussian(); sum += g; sq += g * g; }
            double mean = sum / n, var = sq / n - mean * mean;
            Assert.AreEqual(0.0, mean, 0.03);
            Assert.AreEqual(1.0, var, 0.05);
        }

        [Test]
        public void ForkIsIndependentAndDeterministic()
        {
            var r1 = new XorShift128Plus(5);
            var r2 = new XorShift128Plus(5);
            var f1 = r1.Fork(3);
            var f2 = r2.Fork(3);
            Assert.AreEqual(r1.S0, r2.S0, "fork must not advance the parent");
            Assert.AreEqual(f1.NextULong(), f2.NextULong());
            Assert.AreNotEqual(r1.Fork(4).NextULong(), f1.Clone().NextULong());
        }
    }
}
