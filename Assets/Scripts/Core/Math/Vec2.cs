using System;
using System.Globalization;

namespace AlpineSim.Core.Math
{
    /// <summary>2D vector in metres (map plane). X east, Y north.</summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y) { X = x; Y = y; }

        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 One = new Vec2(1f, 1f);
        public static readonly Vec2 UnitX = new Vec2(1f, 0f);
        public static readonly Vec2 UnitY = new Vec2(0f, 1f);

        public float Length => MathF.Sqrt(X * X + Y * Y);
        public float SqrLength => X * X + Y * Y;

        public Vec2 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-8f ? new Vec2(X / l, Y / l) : Zero;
            }
        }

        /// <summary>Perpendicular rotated +90 degrees (counter-clockwise).</summary>
        public Vec2 Perp => new Vec2(-Y, X);

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator *(float s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, float s) => new Vec2(a.X / s, a.Y / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2 a, Vec2 b) => !(a == b);

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        /// <summary>2D cross product (z component).</summary>
        public static float Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static float SqrDistance(Vec2 a, Vec2 b) => (a - b).SqrLength;
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        public static Vec2 FromAngle(float radians) => new Vec2(MathF.Cos(radians), MathF.Sin(radians));
        /// <summary>Angle of the vector in radians, counter-clockwise from +X.</summary>
        public float Angle => MathF.Atan2(Y, X);
        public static Vec2 Min(Vec2 a, Vec2 b) => new Vec2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));
        public static Vec2 Max(Vec2 a, Vec2 b) => new Vec2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));

        public Vec2 Rotate(float radians)
        {
            float c = MathF.Cos(radians), s = MathF.Sin(radians);
            return new Vec2(X * c - Y * s, X * s + Y * c);
        }

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##})", X, Y);
    }
}
