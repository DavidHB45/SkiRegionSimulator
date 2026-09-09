// Compile-only stubs. See UnityStubs.csproj.
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public enum HideFlags { None = 0, HideInHierarchy = 1, HideAndDontSave = 61, DontSave = 52 }
    public enum RuntimePlatform { OSXEditor, OSXPlayer, WindowsPlayer, WindowsEditor, LinuxPlayer, LinuxEditor, Other }
    public enum KeyCode { None, Escape, Space, Return }
    public enum CursorLockMode { None, Locked, Confined }
    public enum SendMessageOptions { RequireReceiver, DontRequireReceiver }

    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string h) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string t) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TextAreaAttribute : Attribute { public TextAreaAttribute() { } public TextAreaAttribute(int a, int b) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int o) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class RequireComponent : Attribute { public RequireComponent(Type t) { } public RequireComponent(Type a, Type b) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class ExecuteInEditMode : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class ExecuteAlways : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string s) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Field)] public sealed class HideInInspector : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute() { } public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) { } }
    public enum RuntimeInitializeLoadType { AfterSceneLoad, BeforeSceneLoad, AfterAssembliesLoaded, BeforeSplashScreen, SubsystemRegistration }

    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public int GetInstanceID() => 0;
        public static void Destroy(Object o) { }
        public static void Destroy(Object o, float t) { }
        public static void DestroyImmediate(Object o) { }
        public static void DestroyImmediate(Object o, bool allowAssets) { }
        public static void DontDestroyOnLoad(Object o) { }
        public static T Instantiate<T>(T o) where T : Object => o;
        public static T Instantiate<T>(T o, Transform parent) where T : Object => o;
        public static T FindFirstObjectByType<T>() where T : Object => null;
        public static T FindAnyObjectByType<T>() where T : Object => null;
        public static T[] FindObjectsByType<T>(FindObjectsSortMode m) where T : Object => new T[0];
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public static implicit operator bool(Object o) => !ReferenceEquals(o, null);
        public override bool Equals(object o) => ReferenceEquals(this, o);
        public override int GetHashCode() => base.GetHashCode();
        public override string ToString() => name;
    }
    public enum FindObjectsSortMode { None, InstanceID }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { this.name = name; }
        public GameObject(string name, params Type[] components) { this.name = name; }
        public Transform transform => null;
        public string tag { get; set; }
        public int layer { get; set; }
        public bool activeSelf => true;
        public bool activeInHierarchy => true;
        public bool isStatic { get; set; }
        public void SetActive(bool v) { }
        public T AddComponent<T>() where T : Component => null;
        public Component AddComponent(Type t) => null;
        public T GetComponent<T>() => default(T);
        public Component GetComponent(Type t) => null;
        public T GetComponentInChildren<T>() => default(T);
        public T GetComponentInChildren<T>(bool includeInactive) => default(T);
        public T GetComponentInParent<T>() => default(T);
        public T[] GetComponents<T>() => new T[0];
        public T[] GetComponentsInChildren<T>() => new T[0];
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
        public bool TryGetComponent<T>(out T c) { c = default(T); return false; }
        public bool CompareTag(string t) => false;
        public void SendMessage(string m) { }
        public void SendMessage(string m, object v, SendMessageOptions o) { }
        public static GameObject CreatePrimitive(PrimitiveType t) => new GameObject();
        public static GameObject Find(string name) => null;
        public static GameObject FindWithTag(string tag) => null;
    }
    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }

    public class Component : Object
    {
        public GameObject gameObject => null;
        public Transform transform => null;
        public string tag { get => ""; set { } }
        public T GetComponent<T>() => default(T);
        public Component GetComponent(Type t) => null;
        public T GetComponentInChildren<T>() => default(T);
        public T GetComponentInChildren<T>(bool includeInactive) => default(T);
        public T GetComponentInParent<T>() => default(T);
        public T[] GetComponents<T>() => new T[0];
        public T[] GetComponentsInChildren<T>() => new T[0];
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
        public bool TryGetComponent<T>(out T c) { c = default(T); return false; }
        public bool CompareTag(string t) => false;
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled => enabled;
    }

    public class Coroutine { }
    public class YieldInstruction { }
    public class WaitForSeconds : YieldInstruction { public WaitForSeconds(float s) { } }
    public class WaitForEndOfFrame : YieldInstruction { }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator e) => null;
        public void StopCoroutine(Coroutine c) { }
        public void StopAllCoroutines() { }
        public void Invoke(string m, float t) { }
        public void CancelInvoke() { }
        public bool useGUILayout { get; set; }
        public static void print(object o) { }
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => null;
        public static ScriptableObject CreateInstance(Type t) => null;
    }

    public class Transform : Component, IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 lossyScale => Vector3.one;
        public Vector3 eulerAngles { get; set; }
        public Vector3 localEulerAngles { get; set; }
        public Vector3 forward { get; set; }
        public Vector3 right { get; set; }
        public Vector3 up { get; set; }
        public Transform parent { get; set; }
        public Transform root => this;
        public int childCount => 0;
        public bool hasChanged { get; set; }
        public Matrix4x4 localToWorldMatrix => Matrix4x4.identity;
        public Matrix4x4 worldToLocalMatrix => Matrix4x4.identity;
        public void SetParent(Transform p) { }
        public void SetParent(Transform p, bool worldPositionStays) { }
        public Transform GetChild(int i) => null;
        public Transform Find(string n) => null;
        public void SetAsLastSibling() { }
        public void SetAsFirstSibling() { }
        public void SetSiblingIndex(int i) { }
        public int GetSiblingIndex() => 0;
        public void LookAt(Vector3 p) { }
        public void LookAt(Vector3 p, Vector3 up) { }
        public void LookAt(Transform t) { }
        public void Rotate(Vector3 e) { }
        public void Rotate(Vector3 e, Space s) { }
        public void Rotate(Vector3 axis, float angle) { }
        public void Rotate(float x, float y, float z) { }
        public void RotateAround(Vector3 p, Vector3 axis, float angle) { }
        public void Translate(Vector3 v) { }
        public void Translate(Vector3 v, Space s) { }
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { }
        public void SetLocalPositionAndRotation(Vector3 p, Quaternion q) { }
        public Vector3 TransformPoint(Vector3 p) => p;
        public Vector3 InverseTransformPoint(Vector3 p) => p;
        public Vector3 TransformDirection(Vector3 d) => d;
        public Vector3 InverseTransformDirection(Vector3 d) => d;
        public Vector3 TransformVector(Vector3 d) => d;
        public void DetachChildren() { }
        public bool IsChildOf(Transform t) => false;
        public IEnumerator GetEnumerator() => new List<Transform>().GetEnumerator();
    }
    public enum Space { World, Self }

    public static class Debug
    {
        public static void Log(object o) { }
        public static void Log(object o, Object ctx) { }
        public static void LogWarning(object o) { }
        public static void LogWarning(object o, Object ctx) { }
        public static void LogError(object o) { }
        public static void LogError(object o, Object ctx) { }
        public static void LogException(Exception e) { }
        public static void LogException(Exception e, Object ctx) { }
        public static void LogFormat(string f, params object[] a) { }
        public static void LogWarningFormat(string f, params object[] a) { }
        public static void LogErrorFormat(string f, params object[] a) { }
        public static void Assert(bool c) { }
        public static void Assert(bool c, string m) { }
        public static void DrawLine(Vector3 a, Vector3 b) { }
        public static void DrawLine(Vector3 a, Vector3 b, Color c) { }
        public static void DrawLine(Vector3 a, Vector3 b, Color c, float d) { }
        public static void DrawRay(Vector3 a, Vector3 d, Color c) { }
        public static bool isDebugBuild => true;
    }

    public static class Time
    {
        public static float deltaTime => 0.016f;
        public static float unscaledDeltaTime => 0.016f;
        public static float smoothDeltaTime => 0.016f;
        public static float fixedDeltaTime { get; set; }
        public static float time => 0f;
        public static float unscaledTime => 0f;
        public static float realtimeSinceStartup => 0f;
        public static double realtimeSinceStartupAsDouble => 0;
        public static double timeAsDouble => 0;
        public static float timeScale { get; set; }
        public static int frameCount => 0;
        public static float timeSinceLevelLoad => 0f;
    }

    public static class Application
    {
        public static string persistentDataPath => "";
        public static string streamingAssetsPath => "";
        public static string dataPath => "";
        public static string temporaryCachePath => "";
        public static string version => "";
        public static string productName => "";
        public static string companyName => "";
        public static string unityVersion => "";
        public static bool isPlaying => true;
        public static bool isEditor => false;
        public static bool isBatchMode => false;
        public static bool isFocused => true;
        public static RuntimePlatform platform => RuntimePlatform.Other;
        public static int targetFrameRate { get; set; }
        public static bool runInBackground { get; set; }
        public static event Action quitting;
        public static event Func<bool> wantsToQuit;
        public static void Quit() { }
        public static void Quit(int code) { }
        public static void OpenURL(string url) { }
    }

    public static class Screen
    {
        public static int width => 1600;
        public static int height => 900;
        public static float dpi => 96f;
        public static bool fullScreen { get; set; }
        public static FullScreenMode fullScreenMode { get; set; }
        public static Rect safeArea => new Rect(0, 0, 1600, 900);
        public static void SetResolution(int w, int h, bool fs) { }
        public static void SetResolution(int w, int h, FullScreenMode m) { }
    }
    public enum FullScreenMode { ExclusiveFullScreen, FullScreenWindow, MaximizedWindow, Windowed }

    public static class Cursor
    {
        public static CursorLockMode lockState { get; set; }
        public static bool visible { get; set; }
    }

    public static class QualitySettings
    {
        public static int vSyncCount { get; set; }
        public static float shadowDistance { get; set; }
        public static int antiAliasing { get; set; }
        public static float lodBias { get; set; }
        public static int GetQualityLevel() => 1;
        public static void SetQualityLevel(int i) { }
        public static void SetQualityLevel(int i, bool apply) { }
        public static string[] names => new string[0];
    }

    public static class SystemInfo
    {
        public static bool supportsComputeShaders => true;
        public static bool supportsInstancing => true;
        public static bool supportsAsyncGPUReadback => true;
        public static string graphicsDeviceName => "";
        public static string graphicsDeviceVersion => "";
        public static UnityEngine.Rendering.GraphicsDeviceType graphicsDeviceType => UnityEngine.Rendering.GraphicsDeviceType.Null;
        public static int graphicsMemorySize => 0;
        public static int maxTextureSize => 16384;
        public static int maxComputeBufferInputsCompute => 8;
        public static string processorType => "";
        public static int processorCount => 4;
        public static int systemMemorySize => 0;
        public static string operatingSystem => "";
        public static string deviceModel => "";
        public static bool SupportsRenderTextureFormat(RenderTextureFormat f) => true;
        public static bool SupportsTextureFormat(TextureFormat f) => true;
        public static bool IsFormatSupported(UnityEngine.Experimental.Rendering.GraphicsFormat f, UnityEngine.Experimental.Rendering.FormatUsage u) => true;
    }

    public static class Resources
    {
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
        public static T Load<T>(string path) where T : Object => null;
        public static Object Load(string path) => null;
        public static void UnloadUnusedAssets() { }
    }

    public struct LayerMask
    {
        public int value;
        public static int NameToLayer(string n) => 0;
        public static string LayerToName(int l) => "";
        public static int GetMask(params string[] names) => 0;
        public static implicit operator int(LayerMask m) => m.value;
        public static implicit operator LayerMask(int v) => new LayerMask { value = v };
    }

    public class Font : Object
    {
        public int fontSize => 14;
        public bool dynamic => true;
        public static Font CreateDynamicFontFromOSFont(string name, int size) => null;
        public static string[] GetOSInstalledFontNames() => new string[0];
    }

    public class AudioListener : Behaviour { public static float volume { get; set; } }
    public class AudioSource : Behaviour { public float volume { get; set; } public bool loop { get; set; } public void Play() { } public void Stop() { } }

    public static class JsonUtility
    {
        public static string ToJson(object o) => "";
        public static string ToJson(object o, bool pretty) => "";
        public static T FromJson<T>(string s) => default(T);
    }

    public static class PlayerPrefs
    {
        public static void SetString(string k, string v) { }
        public static string GetString(string k, string d = "") => d;
        public static void SetInt(string k, int v) { }
        public static int GetInt(string k, int d = 0) => d;
        public static void SetFloat(string k, float v) { }
        public static float GetFloat(string k, float d = 0f) => d;
        public static bool HasKey(string k) => false;
        public static void Save() { }
    }

    public static class Gizmos
    {
        public static Color color { get; set; }
        public static void DrawLine(Vector3 a, Vector3 b) { }
        public static void DrawSphere(Vector3 c, float r) { }
        public static void DrawWireCube(Vector3 c, Vector3 s) { }
    }

    public static class GUI { public static void Label(Rect r, string s) { } }
    public static class GUILayout { public static void Label(string s) { } }
}
