using System;
using AlpineSim.Core.Math;

namespace AlpineSim.Core.Terrain
{
    [Flags]
    public enum TerrainFlags : byte
    {
        None = 0,
        /// <summary>No lift tower or terminal foundation can be placed here (cliff, gorge floor, water).</summary>
        NoFoundation = 1,
        /// <summary>Gorge / canyon floor: impassable for vehicles and guests.</summary>
        Gorge = 2,
        /// <summary>Engineered flat: base area, parking lot, road bench.</summary>
        Flat = 4,
    }

    /// <summary>
    /// Bare-ground heightmap (metres above sea level) at 1 m spacing plus derived slope/aspect
    /// helpers. Deterministically generated from (scenario, seed); never saved.
    /// Coordinates: X east, Y north, both in metres from the south-west corner.
    /// </summary>
    public sealed class TerrainData
    {
        public readonly int Res;          // samples per axis
        public readonly float Spacing;    // metres between samples
        public readonly float[] Heights;  // row-major, index = y * Res + x
        public readonly byte[] Flags;
        public float MinHeight { get; internal set; }
        public float MaxHeight { get; internal set; }
        public float SizeM => (Res - 1) * Spacing;

        public TerrainData(int res, float spacing)
        {
            Res = res;
            Spacing = spacing;
            Heights = new float[res * res];
            Flags = new byte[res * res];
        }

        public float HeightAt(int ix, int iy)
        {
            ix = ix < 0 ? 0 : (ix >= Res ? Res - 1 : ix);
            iy = iy < 0 ? 0 : (iy >= Res ? Res - 1 : iy);
            return Heights[iy * Res + ix];
        }

        public TerrainFlags FlagsAt(int ix, int iy)
        {
            ix = ix < 0 ? 0 : (ix >= Res ? Res - 1 : ix);
            iy = iy < 0 ? 0 : (iy >= Res ? Res - 1 : iy);
            return (TerrainFlags)Flags[iy * Res + ix];
        }

        public TerrainFlags FlagsAt(float x, float y) => FlagsAt((int)MathF.Round(x / Spacing), (int)MathF.Round(y / Spacing));
        public bool HasFlag(float x, float y, TerrainFlags f) => (FlagsAt(x, y) & f) != 0;

        public bool InBounds(float x, float y) => x >= 0f && y >= 0f && x <= SizeM && y <= SizeM;

        /// <summary>Bilinear height sample, clamped to the map.</summary>
        public float SampleHeight(float x, float y)
        {
            float fx = MathUtil.Clamp(x / Spacing, 0f, Res - 1.0001f);
            float fy = MathUtil.Clamp(y / Spacing, 0f, Res - 1.0001f);
            int x0 = (int)fx, y0 = (int)fy;
            float tx = fx - x0, ty = fy - y0;
            int i = y0 * Res + x0;
            float h00 = Heights[i], h10 = Heights[i + 1], h01 = Heights[i + Res], h11 = Heights[i + Res + 1];
            float a = h00 + (h10 - h00) * tx;
            float b = h01 + (h11 - h01) * tx;
            return a + (b - a) * ty;
        }

        public float SampleHeight(Vec2 p) => SampleHeight(p.X, p.Y);

        /// <summary>Gradient (dh/dx, dh/dy) by central differences over one sample spacing.</summary>
        public Vec2 Gradient(float x, float y)
        {
            float d = Spacing;
            float gx = (SampleHeight(x + d, y) - SampleHeight(x - d, y)) / (2f * d);
            float gy = (SampleHeight(x, y + d) - SampleHeight(x, y - d)) / (2f * d);
            return new Vec2(gx, gy);
        }

        public float SlopeDeg(float x, float y)
        {
            var g = Gradient(x, y);
            return MathF.Atan(g.Length) * MathUtil.Rad2Deg;
        }

        /// <summary>Downhill direction (unit vector in the map plane), or Zero on flat ground.</summary>
        public Vec2 Fallline(float x, float y)
        {
            var g = Gradient(x, y);
            return (-g).Normalized;
        }

        /// <summary>Aspect in radians: direction the slope faces, 0 = east, pi/2 = north (counter-clockwise).</summary>
        public float AspectRad(float x, float y)
        {
            var g = Gradient(x, y);
            if (g.SqrLength < 1e-8f) return 0f;
            return MathF.Atan2(-g.Y, -g.X);
        }

        /// <summary>Unit surface normal (X east, Y north, Z up).</summary>
        public Vec3 Normal(float x, float y)
        {
            var g = Gradient(x, y);
            return new Vec3(-g.X, -g.Y, 1f).Normalized;
        }

        /// <summary>Slope along a given horizontal direction, in degrees (positive = uphill).</summary>
        public float GradeAlongDeg(float x, float y, Vec2 dir)
        {
            var g = Gradient(x, y);
            var d = dir.Normalized;
            return MathF.Atan(Vec2.Dot(g, d)) * MathUtil.Rad2Deg;
        }

        internal void RecomputeBounds()
        {
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < Heights.Length; i++)
            {
                float h = Heights[i];
                if (h < mn) mn = h;
                if (h > mx) mx = h;
            }
            MinHeight = mn;
            MaxHeight = mx;
        }
    }
}
