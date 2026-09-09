using System;

namespace AlpineSim.Core.Random
{
    /// <summary>
    /// Deterministic xorshift128+ PRNG. The only source of randomness inside Core.
    /// Its state lives in WorldState so a save/load round-trip continues the exact same sequence.
    /// </summary>
    [Serializable]
    public sealed class XorShift128Plus
    {
        public ulong S0;
        public ulong S1;

        public XorShift128Plus() { Reseed(1); }
        public XorShift128Plus(int seed) { Reseed(seed); }
        public XorShift128Plus(ulong s0, ulong s1) { S0 = s0; S1 = s1; if (S0 == 0 && S1 == 0) S1 = 0x9E3779B97F4A7C15UL; }

        /// <summary>Seeds both state words via splitmix64 so nearby integer seeds are decorrelated.</summary>
        public void Reseed(int seed)
        {
            ulong x = unchecked((ulong)(long)seed) ^ 0x5DEECE66DUL;
            S0 = SplitMix64(ref x);
            S1 = SplitMix64(ref x);
            if (S0 == 0 && S1 == 0) S1 = 1;
        }

        public static ulong SplitMix64(ref ulong state)
        {
            unchecked
            {
                ulong z = (state += 0x9E3779B97F4A7C15UL);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public ulong NextULong()
        {
            unchecked
            {
                ulong s1 = S0;
                ulong s0 = S1;
                ulong result = s0 + s1;
                S0 = s0;
                s1 ^= s1 << 23;
                S1 = s1 ^ s0 ^ (s1 >> 18) ^ (s0 >> 5);
                return result;
            }
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat() => (float)((NextULong() >> 40) * (1.0 / 16777216.0));

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong span = (ulong)(long)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextULong() % span);
        }

        /// <summary>Uniform float in [min, max).</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        public bool Chance(float probability) => NextFloat() < probability;

        /// <summary>Standard normal deviate (Box-Muller, no caching so the stream is stateless beyond S0/S1).</summary>
        public float NextGaussian()
        {
            double u1 = 1.0 - NextDouble();
            double u2 = NextDouble();
            return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
        }

        public float NextGaussian(float mean, float stdDev) => mean + stdDev * NextGaussian();

        /// <summary>Derives an independent child stream (e.g. per-day weather stream) without disturbing this one.</summary>
        public XorShift128Plus Fork(int salt)
        {
            ulong x = S0 ^ unchecked((ulong)(long)salt * 0x9E3779B97F4A7C15UL) ^ (S1 << 1);
            ulong a = SplitMix64(ref x);
            ulong b = SplitMix64(ref x);
            return new XorShift128Plus(a, b);
        }

        public XorShift128Plus Clone() => new XorShift128Plus(S0, S1);
    }
}
