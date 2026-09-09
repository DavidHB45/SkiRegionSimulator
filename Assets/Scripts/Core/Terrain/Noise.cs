using System;

namespace AlpineSim.Core.Terrain
{
    /// <summary>Deterministic hash-based value noise with fBm. No System.Random, no UnityEngine.</summary>
    public static class Noise
    {
        private static uint Hash(int x, int y, uint seed)
        {
            unchecked
            {
                uint h = seed ^ 0x9E3779B9u;
                h ^= (uint)x * 0x85EBCA6Bu;
                h = (h ^ (h >> 13)) * 0xC2B2AE35u;
                h ^= (uint)y * 0x27D4EB2Fu;
                h = (h ^ (h >> 16)) * 0x165667B1u;
                h ^= h >> 15;
                return h;
            }
        }

        /// <summary>Lattice value in [0,1).</summary>
        public static float Lattice(int x, int y, uint seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;

        /// <summary>Smoothly interpolated value noise in [0,1).</summary>
        public static float Value(float x, float y, uint seed)
        {
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float v00 = Lattice(x0, y0, seed), v10 = Lattice(x0 + 1, y0, seed);
            float v01 = Lattice(x0, y0 + 1, seed), v11 = Lattice(x0 + 1, y0 + 1, seed);
            float a = v00 + (v10 - v00) * fx;
            float b = v01 + (v11 - v01) * fx;
            return a + (b - a) * fy;
        }

        /// <summary>Fractal Brownian motion in [-1,1] (approximately).</summary>
        public static float Fbm(float x, float y, uint seed, int octaves, float gain, float lacunarity)
        {
            float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
            for (int i = 0; i < octaves; i++)
            {
                sum += (Value(x * freq, y * freq, seed + (uint)i * 7919u) * 2f - 1f) * amp;
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Ridged variant in [0,1]: sharp crests, used to add spurs and gullies.</summary>
        public static float Ridged(float x, float y, uint seed, int octaves, float gain, float lacunarity)
        {
            float sum = 0f, amp = 1f, norm = 0f, freq = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - MathF.Abs(Value(x * freq, y * freq, seed + (uint)i * 104729u) * 2f - 1f);
                sum += n * n * amp;
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }
    }
}
