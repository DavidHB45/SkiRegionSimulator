// Compile-only stubs. See UnityStubs.csproj.
using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16, UInt32 }
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
    public enum AmbientMode { Skybox = 0, Trilight = 1, Flat = 3, Custom = 4 }
    public enum GraphicsDeviceType { Null, Direct3D11, Direct3D12, OpenGLCore, Vulkan, Metal }
    public enum LightProbeUsage { Off, BlendProbes, UseProxyVolume, CustomProvided }
    public enum ReflectionProbeUsage { Off, BlendProbes, BlendProbesAndSkybox, Simple }
    public enum MotionVectorGenerationMode { Camera, Object, ForceNoMotion }
    public enum CullMode { Off, Front, Back }
    public class CommandBuffer : IDisposable { public string name { get; set; } public void Dispose() { } public void Clear() { } }
    public struct VertexAttributeDescriptor { public VertexAttributeDescriptor(VertexAttribute a, VertexAttributeFormat f, int d) { } }
    public enum VertexAttribute { Position, Normal, Tangent, Color, TexCoord0, TexCoord1 }
    public enum VertexAttributeFormat { Float32, Float16, UNorm8, SNorm8 }
    public enum MeshUpdateFlags { Default = 0, DontValidateIndices = 1, DontResetBoneBounds = 2, DontNotifyMeshUsers = 4, DontRecalculateBounds = 8 }
    public struct SubMeshDescriptor { public SubMeshDescriptor(int start, int count, MeshTopology t = MeshTopology.Triangles) { } }
}

namespace UnityEngine.Experimental.Rendering
{
    public enum GraphicsFormat { None, R8G8B8A8_UNorm, R16G16B16A16_SFloat, R32_SFloat, R32G32B32A32_SFloat, R8G8B8A8_SRGB }
    public enum FormatUsage { Sample, Render, Blend, Linear, LoadStore }
    public enum TextureCreationFlags { None = 0, MipChain = 1 }
}

namespace UnityEngine
{
    public enum MeshTopology { Triangles = 0, Quads = 2, Lines = 3, LineStrip = 4, Points = 5 }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp, Mirror, MirrorOnce }
    public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4, ARGB32 = 5, R16 = 9, RGBAHalf = 17, RFloat = 18, RGFloat = 19, RGBAFloat = 20, RHalf = 15, RGHalf = 16, R8 = 63 }
    public enum RenderTextureFormat { ARGB32 = 0, Depth = 1, ARGBHalf = 2, RGB565 = 4, ARGB4444 = 5, ARGB1555 = 6, Default = 7, ARGB2101010 = 8, DefaultHDR = 9, ARGB64 = 10, ARGBFloat = 11, RGFloat = 12, RGHalf = 13, RFloat = 14, RHalf = 15, R8 = 16, ARGBInt = 17, RGInt = 18, RInt = 19, BGRA32 = 20, RGB111110Float = 22, RG32 = 23, RGBAUShort = 24, RG16 = 25, R16 = 27 }
    public enum RenderTextureReadWrite { Default, Linear, sRGB }
    public enum LightType { Spot, Directional, Point, Area, Rectangle = 3, Disc = 4 }
    public enum LightShadows { None, Hard, Soft }
    public enum FogMode { Linear = 1, Exponential = 2, ExponentialSquared = 3 }
    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }
    public enum LineAlignment { View, TransformZ }
    public enum LineTextureMode { Stretch, Tile, DistributePerSegment, RepeatPerSegment }
    public enum DepthTextureMode { None = 0, Depth = 1, DepthNormals = 2, MotionVectors = 4 }
    public enum ColorSpace { Uninitialized = -1, Gamma = 0, Linear = 1 }
    public enum HideFlagsDummy { }

    public class Texture : Object
    {
        public virtual int width { get; set; }
        public virtual int height { get; set; }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public int anisoLevel { get; set; }
        public float mipMapBias { get; set; }
        public Vector2 texelSize => Vector2.zero;
        public UnityEngine.Experimental.Rendering.GraphicsFormat graphicsFormat => 0;
    }

    public class Texture2D : Texture
    {
        public Texture2D(int w, int h) { }
        public Texture2D(int w, int h, TextureFormat f, bool mipChain) { }
        public Texture2D(int w, int h, TextureFormat f, bool mipChain, bool linear) { }
        public Texture2D(int w, int h, UnityEngine.Experimental.Rendering.GraphicsFormat f, UnityEngine.Experimental.Rendering.TextureCreationFlags flags) { }
        public TextureFormat format => TextureFormat.RGBA32;
        public int mipmapCount => 1;
        public bool isReadable => true;
        public static Texture2D whiteTexture => null;
        public static Texture2D blackTexture => null;
        public void SetPixel(int x, int y, Color c) { }
        public void SetPixel(int x, int y, Color c, int mip) { }
        public Color GetPixel(int x, int y) => Color.white;
        public void SetPixels(Color[] c) { }
        public void SetPixels(int x, int y, int w, int h, Color[] c) { }
        public void SetPixels32(Color32[] c) { }
        public void SetPixels32(int x, int y, int w, int h, Color32[] c) { }
        public Color[] GetPixels() => new Color[0];
        public Color32[] GetPixels32() => new Color32[0];
        public void SetPixelData<T>(T[] data, int mipLevel, int sourceDataStartIndex = 0) where T : struct { }
        public void SetPixelData<T>(List<T> data, int mipLevel, int sourceDataStartIndex = 0) where T : struct { }
        public void Apply() { }
        public void Apply(bool updateMipmaps) { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
        public void Reinitialize(int w, int h) { }
        public byte[] GetRawTextureData() => new byte[0];
        public void LoadRawTextureData(byte[] d) { }
        public byte[] EncodeToPNG() => new byte[0];
    }

    public class RenderTexture : Texture
    {
        public RenderTexture(int w, int h, int depth) { }
        public RenderTexture(int w, int h, int depth, RenderTextureFormat f) { }
        public RenderTexture(int w, int h, int depth, RenderTextureFormat f, RenderTextureReadWrite rw) { }
        public RenderTexture(int w, int h, int depth, UnityEngine.Experimental.Rendering.GraphicsFormat f) { }
        public RenderTexture(RenderTextureDescriptor d) { }
        public bool enableRandomWrite { get; set; }
        public bool useMipMap { get; set; }
        public bool autoGenerateMips { get; set; }
        public RenderTextureFormat format { get; set; }
        public int depth { get; set; }
        public int antiAliasing { get; set; }
        public int volumeDepth { get; set; }
        public static RenderTexture active { get; set; }
        public bool IsCreated() => true;
        public bool Create() => true;
        public void Release() { }
        public void DiscardContents() { }
        public static RenderTexture GetTemporary(int w, int h, int d = 0, RenderTextureFormat f = RenderTextureFormat.Default) => null;
        public static void ReleaseTemporary(RenderTexture t) { }
    }
    public struct RenderTextureDescriptor
    {
        public int width, height, depthBufferBits, volumeDepth, msaaSamples;
        public bool enableRandomWrite, useMipMap, sRGB;
        public RenderTextureFormat colorFormat;
        public RenderTextureDescriptor(int w, int h, RenderTextureFormat f, int depth) { width = w; height = h; colorFormat = f; depthBufferBits = depth; volumeDepth = 1; msaaSamples = 1; enableRandomWrite = false; useMipMap = false; sRGB = false; }
    }

    public class Shader : Object
    {
        public static Shader Find(string name) => null;
        public static int PropertyToID(string name) => 0;
        public bool isSupported => true;
        public int passCount => 1;
        public static void SetGlobalFloat(string n, float v) { }
        public static void SetGlobalFloat(int id, float v) { }
        public static void SetGlobalInt(string n, int v) { }
        public static void SetGlobalInteger(int id, int v) { }
        public static void SetGlobalVector(string n, Vector4 v) { }
        public static void SetGlobalVector(int id, Vector4 v) { }
        public static void SetGlobalColor(string n, Color v) { }
        public static void SetGlobalColor(int id, Color v) { }
        public static void SetGlobalTexture(string n, Texture t) { }
        public static void SetGlobalTexture(int id, Texture t) { }
        public static void SetGlobalMatrix(string n, Matrix4x4 m) { }
        public static void EnableKeyword(string k) { }
        public static void DisableKeyword(string k) { }
        public static void WarmupAllShaders() { }
    }

    public class Material : Object
    {
        public Material(Shader s) { }
        public Material(Material m) { }
        public Shader shader { get; set; }
        public Color color { get; set; }
        public Texture mainTexture { get; set; }
        public Vector2 mainTextureScale { get; set; }
        public Vector2 mainTextureOffset { get; set; }
        public int renderQueue { get; set; }
        public bool enableInstancing { get; set; }
        public bool doubleSidedGI { get; set; }
        public string[] shaderKeywords { get; set; }
        public void SetFloat(string n, float v) { }
        public void SetFloat(int id, float v) { }
        public void SetInt(string n, int v) { }
        public void SetInt(int id, int v) { }
        public void SetInteger(string n, int v) { }
        public void SetInteger(int id, int v) { }
        public void SetVector(string n, Vector4 v) { }
        public void SetVector(int id, Vector4 v) { }
        public void SetColor(string n, Color c) { }
        public void SetColor(int id, Color c) { }
        public void SetTexture(string n, Texture t) { }
        public void SetTexture(int id, Texture t) { }
        public void SetMatrix(string n, Matrix4x4 m) { }
        public void SetMatrix(int id, Matrix4x4 m) { }
        public void SetBuffer(string n, ComputeBuffer b) { }
        public void SetBuffer(int id, ComputeBuffer b) { }
        public void SetFloatArray(string n, float[] v) { }
        public void SetVectorArray(string n, Vector4[] v) { }
        public void SetTextureScale(string n, Vector2 s) { }
        public void SetTextureOffset(string n, Vector2 o) { }
        public float GetFloat(string n) => 0f;
        public float GetFloat(int id) => 0f;
        public int GetInt(string n) => 0;
        public Color GetColor(string n) => Color.white;
        public Vector4 GetVector(string n) => Vector4.zero;
        public Texture GetTexture(string n) => null;
        public bool HasProperty(string n) => true;
        public bool HasProperty(int id) => true;
        public void EnableKeyword(string k) { }
        public void DisableKeyword(string k) { }
        public bool IsKeywordEnabled(string k) => false;
        public bool SetPass(int p) => true;
        public void SetOverrideTag(string t, string v) { }
        public void CopyPropertiesFromMaterial(Material m) { }
    }

    public class MaterialPropertyBlock
    {
        public bool isEmpty => true;
        public void Clear() { }
        public void SetFloat(string n, float v) { }
        public void SetFloat(int id, float v) { }
        public void SetInt(string n, int v) { }
        public void SetInteger(int id, int v) { }
        public void SetVector(string n, Vector4 v) { }
        public void SetVector(int id, Vector4 v) { }
        public void SetColor(string n, Color c) { }
        public void SetColor(int id, Color c) { }
        public void SetTexture(string n, Texture t) { }
        public void SetTexture(int id, Texture t) { }
        public void SetMatrix(string n, Matrix4x4 m) { }
        public void SetFloatArray(string n, float[] v) { }
        public void SetFloatArray(int id, float[] v) { }
        public void SetVectorArray(string n, Vector4[] v) { }
        public void SetVectorArray(int id, Vector4[] v) { }
        public void SetMatrixArray(string n, Matrix4x4[] v) { }
        public void SetBuffer(string n, ComputeBuffer b) { }
    }

    public class ComputeShader : Object
    {
        public int FindKernel(string name) => 0;
        public bool HasKernel(string name) => true;
        public void SetTexture(int kernel, string name, Texture t) { }
        public void SetTexture(int kernel, int id, Texture t) { }
        public void SetBuffer(int kernel, string name, ComputeBuffer b) { }
        public void SetBuffer(int kernel, int id, ComputeBuffer b) { }
        public void SetInt(string name, int v) { }
        public void SetInt(int id, int v) { }
        public void SetInts(string name, params int[] v) { }
        public void SetFloat(string name, float v) { }
        public void SetFloat(int id, float v) { }
        public void SetFloats(string name, params float[] v) { }
        public void SetVector(string name, Vector4 v) { }
        public void SetVector(int id, Vector4 v) { }
        public void SetBool(string name, bool v) { }
        public void SetMatrix(string name, Matrix4x4 m) { }
        public void Dispatch(int kernel, int x, int y, int z) { }
        public void GetKernelThreadGroupSizes(int kernel, out uint x, out uint y, out uint z) { x = y = z = 8; }
        public void EnableKeyword(string k) { }
        public void DisableKeyword(string k) { }
    }

    public enum ComputeBufferType { Default = 0, Raw = 1, Append = 2, Counter = 4, Structured = 16, IndirectArguments = 256 }
    public enum ComputeBufferMode { Immutable, Dynamic, SubUpdates }

    public class ComputeBuffer : IDisposable
    {
        public ComputeBuffer(int count, int stride) { this.count = count; this.stride = stride; }
        public ComputeBuffer(int count, int stride, ComputeBufferType type) { this.count = count; this.stride = stride; }
        public ComputeBuffer(int count, int stride, ComputeBufferType type, ComputeBufferMode mode) { this.count = count; this.stride = stride; }
        public int count { get; }
        public int stride { get; }
        public bool IsValid() => true;
        public void SetData(Array data) { }
        public void SetData<T>(T[] data) where T : struct { }
        public void SetData<T>(List<T> data) where T : struct { }
        public void SetData<T>(T[] data, int managedStart, int bufferStart, int count) where T : struct { }
        public void SetData<T>(List<T> data, int managedStart, int bufferStart, int count) where T : struct { }
        public void GetData(Array data) { }
        public void GetData(Array data, int managedStart, int bufferStart, int count) { }
        public void Release() { }
        public void Dispose() { }
        public void SetCounterValue(uint v) { }
    }

    public class GraphicsBuffer : IDisposable
    {
        public enum Target { Vertex = 1, Index = 2, Structured = 16, Raw = 32, IndirectArguments = 256 }
        public GraphicsBuffer(Target t, int count, int stride) { }
        public void SetData<T>(T[] d) where T : struct { }
        public void Release() { }
        public void Dispose() { }
    }

    public class Mesh : Object
    {
        public Vector3[] vertices { get; set; }
        public Vector3[] normals { get; set; }
        public Vector4[] tangents { get; set; }
        public Vector2[] uv { get; set; }
        public Vector2[] uv2 { get; set; }
        public Vector2[] uv3 { get; set; }
        public Color[] colors { get; set; }
        public Color32[] colors32 { get; set; }
        public int[] triangles { get; set; }
        public Bounds bounds { get; set; }
        public int vertexCount => vertices?.Length ?? 0;
        public int subMeshCount { get; set; }
        public UnityEngine.Rendering.IndexFormat indexFormat { get; set; }
        public bool isReadable => true;
        public void Clear() { }
        public void Clear(bool keepLayout) { }
        public void SetVertices(Vector3[] v) { }
        public void SetVertices(List<Vector3> v) { }
        public void SetVertices(Vector3[] v, int start, int length) { }
        public void SetNormals(Vector3[] v) { }
        public void SetNormals(List<Vector3> v) { }
        public void SetTangents(Vector4[] v) { }
        public void SetUVs(int channel, Vector2[] v) { }
        public void SetUVs(int channel, List<Vector2> v) { }
        public void SetUVs(int channel, Vector3[] v) { }
        public void SetUVs(int channel, Vector4[] v) { }
        public void SetColors(Color[] c) { }
        public void SetColors(Color32[] c) { }
        public void SetColors(List<Color> c) { }
        public void SetColors(List<Color32> c) { }
        public void SetTriangles(int[] t, int submesh) { }
        public void SetTriangles(int[] t, int submesh, bool calcBounds) { }
        public void SetTriangles(List<int> t, int submesh) { }
        public void SetIndices(int[] i, MeshTopology topo, int submesh) { }
        public void SetIndices(int[] i, MeshTopology topo, int submesh, bool calcBounds) { }
        public void SetIndices(List<int> i, MeshTopology topo, int submesh) { }
        public int[] GetTriangles(int submesh) => new int[0];
        public int[] GetIndices(int submesh) => new int[0];
        public void GetVertices(List<Vector3> v) { }
        public void GetNormals(List<Vector3> v) { }
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
        public void RecalculateTangents() { }
        public void MarkDynamic() { }
        public void UploadMeshData(bool markNoLongerReadable) { }
        public void Optimize() { }
        public void MarkModified() { }
        public void CombineMeshes(CombineInstance[] c) { }
        public void CombineMeshes(CombineInstance[] c, bool mergeSubMeshes) { }
        public void CombineMeshes(CombineInstance[] c, bool mergeSubMeshes, bool useMatrices) { }
        public void SetSubMesh(int i, UnityEngine.Rendering.SubMeshDescriptor d, UnityEngine.Rendering.MeshUpdateFlags f = 0) { }
        public void SetVertexBufferParams(int count, params UnityEngine.Rendering.VertexAttributeDescriptor[] attrs) { }
        public void SetVertexBufferData<T>(T[] data, int dataStart, int meshStart, int count, int stream = 0, UnityEngine.Rendering.MeshUpdateFlags f = 0) where T : struct { }
        public void SetIndexBufferParams(int count, UnityEngine.Rendering.IndexFormat f) { }
        public void SetIndexBufferData<T>(T[] data, int dataStart, int meshStart, int count, UnityEngine.Rendering.MeshUpdateFlags f = 0) where T : struct { }
    }
    public struct CombineInstance { public Mesh mesh; public int subMeshIndex; public Matrix4x4 transform; }

    public class Renderer : Component
    {
        public Material material { get; set; }
        public Material sharedMaterial { get; set; }
        public Material[] materials { get; set; }
        public Material[] sharedMaterials { get; set; }
        public bool enabled { get; set; }
        public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
        public Bounds bounds => new Bounds();
        public bool isVisible => true;
        public int sortingOrder { get; set; }
        public UnityEngine.Rendering.LightProbeUsage lightProbeUsage { get; set; }
        public UnityEngine.Rendering.ReflectionProbeUsage reflectionProbeUsage { get; set; }
        public UnityEngine.Rendering.MotionVectorGenerationMode motionVectorGenerationMode { get; set; }
        public uint renderingLayerMask { get; set; }
        public void SetPropertyBlock(MaterialPropertyBlock b) { }
        public void GetPropertyBlock(MaterialPropertyBlock b) { }
        public bool HasPropertyBlock() => false;
    }
    public class MeshRenderer : Renderer { public Mesh additionalVertexStreams { get; set; } }
    public class SkinnedMeshRenderer : Renderer { }
    public class MeshFilter : Component { public Mesh mesh { get; set; } public Mesh sharedMesh { get; set; } }

    public class LineRenderer : Renderer
    {
        public int positionCount { get; set; }
        public float startWidth { get; set; }
        public float endWidth { get; set; }
        public float widthMultiplier { get; set; }
        public Color startColor { get; set; }
        public Color endColor { get; set; }
        public bool useWorldSpace { get; set; }
        public bool loop { get; set; }
        public int numCapVertices { get; set; }
        public int numCornerVertices { get; set; }
        public LineAlignment alignment { get; set; }
        public LineTextureMode textureMode { get; set; }
        public void SetPosition(int i, Vector3 p) { }
        public void SetPositions(Vector3[] p) { }
        public Vector3 GetPosition(int i) => Vector3.zero;
    }

    public class TrailRenderer : Renderer { public float time { get; set; } }

    public class Camera : Behaviour
    {
        public static Camera main => null;
        public static Camera current => null;
        public static Camera[] allCameras => new Camera[0];
        public float fieldOfView { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public float depth { get; set; }
        public float aspect { get; set; }
        public int cullingMask { get; set; }
        public bool orthographic { get; set; }
        public float orthographicSize { get; set; }
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public RenderTexture targetTexture { get; set; }
        public Rect rect { get; set; }
        public Rect pixelRect { get; set; }
        public int pixelWidth => 1600;
        public int pixelHeight => 900;
        public bool allowHDR { get; set; }
        public bool allowMSAA { get; set; }
        public bool useOcclusionCulling { get; set; }
        public DepthTextureMode depthTextureMode { get; set; }
        public Matrix4x4 projectionMatrix { get; set; }
        public Matrix4x4 worldToCameraMatrix { get; set; }
        public Ray ScreenPointToRay(Vector3 p) => new Ray(Vector3.zero, Vector3.forward);
        public Ray ScreenPointToRay(Vector2 p) => new Ray(Vector3.zero, Vector3.forward);
        public Ray ViewportPointToRay(Vector3 p) => new Ray(Vector3.zero, Vector3.forward);
        public Vector3 WorldToScreenPoint(Vector3 p) => p;
        public Vector3 WorldToViewportPoint(Vector3 p) => p;
        public Vector3 ScreenToWorldPoint(Vector3 p) => p;
        public Vector3 ScreenToViewportPoint(Vector3 p) => p;
        public Vector3 ViewportToWorldPoint(Vector3 p) => p;
        public void CopyFrom(Camera c) { }
        public void Render() { }
        public void ResetProjectionMatrix() { }
        public void AddCommandBuffer(CameraEvent e, UnityEngine.Rendering.CommandBuffer b) { }
    }
    public enum CameraEvent { BeforeDepthTexture, AfterDepthTexture, BeforeForwardOpaque, AfterForwardOpaque, BeforeImageEffects, AfterImageEffects, AfterEverything }

    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public Color color { get; set; }
        public float intensity { get; set; }
        public float range { get; set; }
        public float spotAngle { get; set; }
        public float innerSpotAngle { get; set; }
        public LightShadows shadows { get; set; }
        public float shadowStrength { get; set; }
        public float shadowBias { get; set; }
        public float shadowNormalBias { get; set; }
        public int cullingMask { get; set; }
        public float bounceIntensity { get; set; }
        public LightRenderMode renderMode { get; set; }
        public Texture cookie { get; set; }
        public bool useColorTemperature { get; set; }
        public float colorTemperature { get; set; }
    }
    public enum LightRenderMode { Auto, ForcePixel, ForceVertex }

    public static class RenderSettings
    {
        public static Light sun { get; set; }
        public static bool fog { get; set; }
        public static Color fogColor { get; set; }
        public static FogMode fogMode { get; set; }
        public static float fogDensity { get; set; }
        public static float fogStartDistance { get; set; }
        public static float fogEndDistance { get; set; }
        public static UnityEngine.Rendering.AmbientMode ambientMode { get; set; }
        public static Color ambientLight { get; set; }
        public static Color ambientSkyColor { get; set; }
        public static Color ambientEquatorColor { get; set; }
        public static Color ambientGroundColor { get; set; }
        public static float ambientIntensity { get; set; }
        public static Material skybox { get; set; }
        public static float reflectionIntensity { get; set; }
        public static Color subtractiveShadowColor { get; set; }
    }

    public static class Graphics
    {
        public static void DrawMesh(Mesh mesh, Vector3 pos, Quaternion rot, Material mat, int layer) { }
        public static void DrawMesh(Mesh mesh, Matrix4x4 m, Material mat, int layer) { }
        public static void DrawMesh(Mesh mesh, Matrix4x4 m, Material mat, int layer, Camera cam) { }
        public static void DrawMesh(Mesh mesh, Matrix4x4 m, Material mat, int layer, Camera cam, int submesh, MaterialPropertyBlock props) { }
        public static void DrawMesh(Mesh mesh, Matrix4x4 m, Material mat, int layer, Camera cam, int submesh, MaterialPropertyBlock props, UnityEngine.Rendering.ShadowCastingMode cast, bool receive) { }
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices, int count, MaterialPropertyBlock properties = null, UnityEngine.Rendering.ShadowCastingMode castShadows = UnityEngine.Rendering.ShadowCastingMode.On, bool receiveShadows = true, int layer = 0, Camera camera = null) { }
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, List<Matrix4x4> matrices, MaterialPropertyBlock properties = null, UnityEngine.Rendering.ShadowCastingMode castShadows = UnityEngine.Rendering.ShadowCastingMode.On, bool receiveShadows = true, int layer = 0, Camera camera = null) { }
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices) { }
        public static void Blit(Texture src, RenderTexture dst) { }
        public static void Blit(Texture src, RenderTexture dst, Material mat) { }
        public static void Blit(Texture src, RenderTexture dst, Material mat, int pass) { }
        public static void CopyTexture(Texture src, Texture dst) { }
        public static void SetRenderTarget(RenderTexture rt) { }
        public static void ExecuteCommandBuffer(UnityEngine.Rendering.CommandBuffer b) { }
    }

    public static class GL
    {
        public static void Clear(bool depth, bool color, Color c) { }
        public static void PushMatrix() { }
        public static void PopMatrix() { }
        public static void LoadOrtho() { }
    }
}
