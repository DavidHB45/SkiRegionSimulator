// Compile-only stubs. See UnityStubs.csproj.
using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 up => new Vector2(0, 1);
        public static Vector2 right => new Vector2(1, 0);
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public Vector2 normalized { get { float m = magnitude; return m > 1e-6f ? new Vector2(x / m, y / m) : zero; } }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float s) => new Vector2(a.x * s, a.y * s);
        public static Vector2 operator *(float s, Vector2 a) => new Vector2(a.x * s, a.y * s);
        public static Vector2 operator /(Vector2 a, float s) => new Vector2(a.x / s, a.y / s);
        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => a + (b - a) * Mathf.Clamp01(t);
        public static Vector2 ClampMagnitude(Vector2 v, float max) => v;
        public static Vector2 Min(Vector2 a, Vector2 b) => new Vector2(Math.Min(a.x, b.x), Math.Min(a.y, b.y));
        public static Vector2 Max(Vector2 a, Vector2 b) => new Vector2(Math.Max(a.x, b.x), Math.Max(a.y, b.y));
        public override bool Equals(object o) => o is Vector2 v && v == this;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode();
        public override string ToString() => "(" + x + ", " + y + ")";
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; z = 0; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 left => new Vector3(-1, 0, 0);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float m = magnitude; return m > 1e-6f ? new Vector3(x / m, y / m, z / m) : zero; } }
        public float this[int i] { get => i == 0 ? x : (i == 1 ? y : z); set { if (i == 0) x = value; else if (i == 1) y = value; else z = value; } }
        public void Normalize() { float m = magnitude; if (m > 1e-6f) { x /= m; y /= m; z /= m; } }
        public void Set(float nx, float ny, float nz) { x = nx; y = ny; z = nz; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator *(float s, Vector3 a) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator /(Vector3 a, float s) => new Vector3(a.x / s, a.y / s, a.z / s);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * Mathf.Clamp01(t);
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t) => Lerp(a, b, t);
        public static Vector3 MoveTowards(Vector3 a, Vector3 b, float d) => a;
        public static Vector3 SmoothDamp(Vector3 c, Vector3 t, ref Vector3 v, float st) => c;
        public static Vector3 SmoothDamp(Vector3 c, Vector3 t, ref Vector3 v, float st, float ms, float dt) => c;
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) => v - n * Dot(v, n);
        public static Vector3 Project(Vector3 v, Vector3 n) => n * Dot(v, n);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 ClampMagnitude(Vector3 v, float max) => v;
        public static Vector3 Normalize(Vector3 v) => v.normalized;
        public static float Angle(Vector3 a, Vector3 b) => 0f;
        public static float SignedAngle(Vector3 a, Vector3 b, Vector3 axis) => 0f;
        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(Math.Min(a.x, b.x), Math.Min(a.y, b.y), Math.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(Math.Max(a.x, b.x), Math.Max(a.y, b.y), Math.Max(a.z, b.z));
        public static Vector3 Reflect(Vector3 d, Vector3 n) => d;
        public override bool Equals(object o) => o is Vector3 v && v == this;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode();
        public override string ToString() => "(" + x + ", " + y + ", " + z + ")";
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public Vector4(float x, float y, float z) { this.x = x; this.y = y; this.z = z; w = 0; }
        public static Vector4 zero => new Vector4(0, 0, 0, 0);
        public static Vector4 one => new Vector4(1, 1, 1, 1);
        public static implicit operator Vector4(Vector3 v) => new Vector4(v.x, v.y, v.z, 0);
        public static implicit operator Vector3(Vector4 v) => new Vector3(v.x, v.y, v.z);
        public static implicit operator Vector4(Vector2 v) => new Vector4(v.x, v.y, 0, 0);
        public static Vector4 operator *(Vector4 a, float s) => new Vector4(a.x * s, a.y * s, a.z * s, a.w * s);
        public static Vector4 operator +(Vector4 a, Vector4 b) => new Vector4(a.x + b.x, a.y + b.y, a.z + b.z, a.w + b.w);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0, 0, 0, 1);
        public Vector3 eulerAngles { get => Vector3.zero; set { } }
        public Quaternion normalized => this;
        public static Quaternion Euler(float x, float y, float z) => identity;
        public static Quaternion Euler(Vector3 e) => identity;
        public static Quaternion AngleAxis(float angle, Vector3 axis) => identity;
        public static Quaternion LookRotation(Vector3 forward) => identity;
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) => identity;
        public static Quaternion FromToRotation(Vector3 a, Vector3 b) => identity;
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => a;
        public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t) => a;
        public static Quaternion Lerp(Quaternion a, Quaternion b, float t) => a;
        public static Quaternion RotateTowards(Quaternion a, Quaternion b, float d) => a;
        public static Quaternion Inverse(Quaternion q) => q;
        public static float Angle(Quaternion a, Quaternion b) => 0f;
        public static float Dot(Quaternion a, Quaternion b) => 0f;
        public static Quaternion operator *(Quaternion a, Quaternion b) => a;
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
        public static bool operator ==(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);
        public override bool Equals(object o) => o is Quaternion q && q == this;
        public override int GetHashCode() => 0;
        public void ToAngleAxis(out float angle, out Vector3 axis) { angle = 0; axis = Vector3.up; }
    }

    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public const float Infinity = float.PositiveInfinity;
        public const float NegativeInfinity = float.NegativeInfinity;
        public const float Epsilon = 1.4e-45f;
        public static float Abs(float v) => Math.Abs(v);
        public static int Abs(int v) => Math.Abs(v);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Tan(float v) => (float)Math.Tan(v);
        public static float Asin(float v) => (float)Math.Asin(v);
        public static float Acos(float v) => (float)Math.Acos(v);
        public static float Atan(float v) => (float)Math.Atan(v);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Exp(float v) => (float)Math.Exp(v);
        public static float Log(float v) => (float)Math.Log(v);
        public static float Log(float v, float b) => (float)Math.Log(v, b);
        public static float Log10(float v) => (float)Math.Log10(v);
        public static float Floor(float v) => (float)Math.Floor(v);
        public static float Ceil(float v) => (float)Math.Ceiling(v);
        public static float Round(float v) => (float)Math.Round(v);
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static float Sign(float v) => v >= 0f ? 1f : -1f;
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Min(params float[] v) { float m = v[0]; foreach (var x in v) m = Math.Min(m, x); return m; }
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Max(params float[] v) { float m = v[0]; foreach (var x in v) m = Math.Max(m, x); return m; }
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        public static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);
        public static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float LerpAngle(float a, float b, float t) => a;
        public static float InverseLerp(float a, float b, float v) => a == b ? 0 : Clamp01((v - a) / (b - a));
        public static float SmoothStep(float a, float b, float t) => t;
        public static float MoveTowards(float c, float t, float d) => Math.Abs(t - c) <= d ? t : c + Sign(t - c) * d;
        public static float MoveTowardsAngle(float c, float t, float d) => c;
        public static float SmoothDamp(float c, float t, ref float v, float st) => c;
        public static float SmoothDamp(float c, float t, ref float v, float st, float ms, float dt) => c;
        public static float SmoothDampAngle(float c, float t, ref float v, float st) => c;
        public static float Repeat(float t, float l) => t - Floor(t / l) * l;
        public static float PingPong(float t, float l) => t;
        public static float DeltaAngle(float a, float b) => b - a;
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-6f;
        public static float PerlinNoise(float x, float y) => 0f;
        public static int NextPowerOfTwo(int v) => v;
        public static bool IsPowerOfTwo(int v) => true;
        public static float GammaToLinearSpace(float v) => v;
        public static float LinearToGammaSpace(float v) => v;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1, 1);
        public static Color black => new Color(0, 0, 0, 1);
        public static Color red => new Color(1, 0, 0, 1);
        public static Color green => new Color(0, 1, 0, 1);
        public static Color blue => new Color(0, 0, 1, 1);
        public static Color yellow => new Color(1, 0.92f, 0.016f, 1);
        public static Color cyan => new Color(0, 1, 1, 1);
        public static Color magenta => new Color(1, 0, 1, 1);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1);
        public static Color grey => gray;
        public static Color clear => new Color(0, 0, 0, 0);
        public float this[int i] { get => i == 0 ? r : (i == 1 ? g : (i == 2 ? b : a)); set { } }
        public static Color Lerp(Color x, Color y, float t) { t = Mathf.Clamp01(t); return new Color(x.r + (y.r - x.r) * t, x.g + (y.g - x.g) * t, x.b + (y.b - x.b) * t, x.a + (y.a - x.a) * t); }
        public static Color operator *(Color c, float s) => new Color(c.r * s, c.g * s, c.b * s, c.a * s);
        public static Color operator *(float s, Color c) => c * s;
        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        public static Color operator +(Color a, Color b) => new Color(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);
        public static Color operator -(Color a, Color b) => new Color(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a);
        public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);
        public static implicit operator Color(Vector4 v) => new Color(v.x, v.y, v.z, v.w);
        public static Color HSVToRGB(float h, float s, float v) => white;
        public static void RGBToHSV(Color c, out float h, out float s, out float v) { h = s = v = 0; }
        public Color linear => this;
        public Color gamma => this;
        public float grayscale => (r + g + b) / 3f;
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        public static implicit operator Color32(Color c) => new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255));
        public static Color32 Lerp(Color32 a, Color32 b, float t) => a;
    }

    public static class ColorUtility
    {
        public static string ToHtmlStringRGB(Color c) => "";
        public static string ToHtmlStringRGBA(Color c) => "";
        public static bool TryParseHtmlString(string s, out Color c) { c = Color.white; return true; }
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; }
        public Rect(Vector2 pos, Vector2 size) { x = pos.x; y = pos.y; width = size.x; height = size.y; }
        public float xMin { get => x; set { } }
        public float yMin { get => y; set { } }
        public float xMax { get => x + width; set { } }
        public float yMax { get => y + height; set { } }
        public Vector2 min => new Vector2(x, y);
        public Vector2 max => new Vector2(xMax, yMax);
        public Vector2 size => new Vector2(width, height);
        public Vector2 center => new Vector2(x + width / 2, y + height / 2);
        public Vector2 position => new Vector2(x, y);
        public bool Contains(Vector2 p) => p.x >= x && p.y >= y && p.x <= xMax && p.y <= yMax;
        public bool Contains(Vector3 p) => Contains(new Vector2(p.x, p.y));
        public static Rect zero => new Rect(0, 0, 0, 0);
    }

    public struct RectInt
    {
        public int x, y, width, height;
        public RectInt(int x, int y, int w, int h) { this.x = x; this.y = y; width = w; height = h; }
    }

    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 c, Vector3 s) { center = c; size = s; }
        public Vector3 extents { get => size * 0.5f; set => size = value * 2f; }
        public Vector3 min { get => center - extents; set { } }
        public Vector3 max { get => center + extents; set { } }
        public void Expand(float amount) { size += new Vector3(amount, amount, amount); }
        public void Expand(Vector3 amount) { size += amount; }
        public void Encapsulate(Vector3 p) { }
        public void Encapsulate(Bounds b) { }
        public void SetMinMax(Vector3 mn, Vector3 mx) { center = (mn + mx) * 0.5f; size = mx - mn; }
        public bool Contains(Vector3 p) => true;
        public bool Intersects(Bounds b) => true;
    }

    public struct Ray
    {
        public Vector3 origin, direction;
        public Ray(Vector3 o, Vector3 d) { origin = o; direction = d.normalized; }
        public Vector3 GetPoint(float t) => origin + direction * t;
    }

    public struct Plane
    {
        public Vector3 normal; public float distance;
        public Plane(Vector3 n, Vector3 p) { normal = n.normalized; distance = -Vector3.Dot(normal, p); }
        public Plane(Vector3 n, float d) { normal = n; distance = d; }
        public bool Raycast(Ray ray, out float enter) { enter = 0; return false; }
        public float GetDistanceToPoint(Vector3 p) => Vector3.Dot(normal, p) + distance;
    }

    public struct Matrix4x4
    {
        public float m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;
        public static Matrix4x4 identity => new Matrix4x4();
        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s) => identity;
        public static Matrix4x4 Translate(Vector3 v) => identity;
        public static Matrix4x4 Scale(Vector3 v) => identity;
        public static Matrix4x4 Rotate(Quaternion q) => identity;
        public Vector3 MultiplyPoint(Vector3 v) => v;
        public Vector3 MultiplyPoint3x4(Vector3 v) => v;
        public Vector3 MultiplyVector(Vector3 v) => v;
        public Matrix4x4 inverse => this;
        public Vector4 GetColumn(int i) => Vector4.zero;
        public void SetTRS(Vector3 pos, Quaternion q, Vector3 s) { }
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => a;
    }

    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public static Vector2Int zero => new Vector2Int(0, 0);
    }

    public struct Vector3Int
    {
        public int x, y, z;
        public Vector3Int(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
    }

    public static class Random
    {
        // Intentionally absent members: Core must not use UnityEngine.Random, and the view layer should not either.
    }
}
