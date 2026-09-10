using System;

namespace AlpineSim.Core.Math
{
    /// <summary>Small deterministic math helpers. Core never uses the engine math library.</summary>
    public static class MathUtil
    {
        public const float Pi = 3.14159265358979f;
        public const float TwoPi = 6.28318530717959f;
        public const float Deg2Rad = Pi / 180f;
        public const float Rad2Deg = 180f / Pi;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float LerpClamped(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => System.Math.Abs(b - a) < 1e-12f ? 0f : Clamp01((v - a) / (b - a));
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (System.Math.Abs(target - current) <= maxDelta) return target;
            return current + System.Math.Sign(target - current) * maxDelta;
        }
        public static float Abs(float v) => v < 0f ? -v : v;
        public static float Sign(float v) => v < 0f ? -1f : (v > 0f ? 1f : 0f);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b, float c) => Max(Max(a, b), c);
        public static float Min(float a, float b, float c) => Min(Min(a, b), c);
        public static int FloorToInt(float v) => (int)MathF.Floor(v);
        public static int CeilToInt(float v) => (int)MathF.Ceiling(v);
        public static int RoundToInt(float v) => (int)MathF.Round(v);
        public static float Repeat(float t, float length) => Clamp(t - MathF.Floor(t / length) * length, 0f, length);

        /// <summary>Wraps an angle in radians into (-pi, pi].</summary>
        public static float WrapAngle(float a)
        {
            a = Repeat(a + Pi, TwoPi) - Pi;
            return a;
        }

        /// <summary>Signed shortest difference b - a in radians.</summary>
        public static float DeltaAngle(float a, float b) => WrapAngle(b - a);

        /// <summary>
        /// Piecewise-linear interpolation over a monotonically increasing table of (x, y).
        /// Clamps outside the table range. Tables are how all balance curves are expressed in JSON.
        /// </summary>
        public static float SampleCurve(float[] xs, float[] ys, float x)
        {
            if (xs == null || ys == null || xs.Length == 0) return 0f;
            int n = System.Math.Min(xs.Length, ys.Length);
            if (n == 1 || x <= xs[0]) return ys[0];
            if (x >= xs[n - 1]) return ys[n - 1];
            int i = 1;
            while (i < n && xs[i] < x) i++;
            float t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
            return ys[i - 1] + (ys[i] - ys[i - 1]) * t;
        }

        /// <summary>
        /// Window score: 1 inside [lo, hi], linearly falling to 0 at lo-softLo and hi+softHi.
        /// Used for "target band, not more-is-better" quantities such as density.
        /// </summary>
        public static float BandScore(float v, float lo, float hi, float softLo, float softHi)
        {
            if (v >= lo && v <= hi) return 1f;
            if (v < lo) return softLo <= 0f ? 0f : Clamp01(1f - (lo - v) / softLo);
            return softHi <= 0f ? 0f : Clamp01(1f - (v - hi) / softHi);
        }

        public static bool Approximately(float a, float b, float eps = 1e-5f) => System.Math.Abs(a - b) <= eps;
    }
}
