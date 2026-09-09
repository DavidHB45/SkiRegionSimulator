// Compile-only stubs for the editor API used by Assets/Scripts/Editor. See UnityStubs.csproj.
using System;

namespace UnityEditor
{
    public enum BuildTarget { NoTarget = -2, StandaloneOSX = 2, StandaloneWindows = 5, iOS = 9, Android = 13, StandaloneWindows64 = 19, WebGL = 20, StandaloneLinux64 = 24 }
    public enum BuildTargetGroup { Unknown = 0, Standalone = 1 }
    [Flags] public enum BuildOptions { None = 0, Development = 1, AutoRunPlayer = 4, ShowBuiltPlayer = 8, BuildAdditionalStreamedScenes = 16, AcceptExternalModificationsToPlayer = 32, CleanBuildCache = 128, ConnectWithProfiler = 256, AllowDebugging = 512, SymlinkSources = 1024, UncompressedAssetBundle = 2048, StrictMode = 8192, CompressWithLz4 = 262144, CompressWithLz4HC = 524288, DetailedBuildReport = 4194304 }
    public struct BuildPlayerOptions
    {
        public string[] scenes;
        public string locationPathName;
        public string assetBundleManifestPath;
        public BuildTargetGroup targetGroup;
        public BuildTarget target;
        public int subtarget;
        public BuildOptions options;
        public string[] extraScriptingDefines;
    }
    public static class BuildPipeline
    {
        public static UnityEditor.Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions options) => null;
        public static bool isBuildingPlayer => false;
    }
    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene() { }
        public EditorBuildSettingsScene(string path, bool enabled) { this.path = path; this.enabled = enabled; }
        public string path { get; set; }
        public bool enabled { get; set; }
    }
    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes { get; set; } = new EditorBuildSettingsScene[0];
    }
    public static class EditorApplication
    {
        public static void Exit(int code) { }
        public static bool isPlaying { get; set; }
        public static bool isCompiling => false;
        public static event Action update;
    }
    public static class PlayerSettings
    {
        public static string productName { get; set; }
        public static string companyName { get; set; }
        public static string bundleVersion { get; set; }
        public static void SetScriptingBackend(BuildTargetGroup g, ScriptingImplementation s) { }
    }
    public enum ScriptingImplementation { Mono2x = 0, IL2CPP = 1 }
    public static class EditorUserBuildSettings
    {
        public static BuildTarget activeBuildTarget => BuildTarget.StandaloneWindows64;
        public static bool development { get; set; }
        public static bool SwitchActiveBuildTarget(BuildTargetGroup g, BuildTarget t) => true;
    }
    public static class AssetDatabase
    {
        public static void Refresh() { }
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object => null;
        public static string AssetPathToGUID(string path) => "";
    }
    [AttributeUsage(AttributeTargets.Method)] public sealed class MenuItem : Attribute { public MenuItem(string path) { } public MenuItem(string path, bool validate) { } public MenuItem(string path, bool validate, int priority) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class InitializeOnLoadAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class InitializeOnLoadMethodAttribute : Attribute { }
    public static class EditorUtility { public static void DisplayDialog(string a, string b, string c) { } public static bool DisplayDialog(string a, string b, string c, string d) => true; }
}

namespace UnityEditor.Build
{
    public enum OSArchitecture { x64 = 0, ARM64 = 1, x64ARM64 = 2 }
}

namespace UnityEditor.OSXStandalone
{
    public static class UserBuildSettings
    {
        public static UnityEditor.Build.OSArchitecture architecture { get; set; }
        public static bool createXcodeProject { get; set; }
    }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown, Succeeded, Failed, Cancelled }
    public struct BuildSummary
    {
        public BuildResult result;
        public ulong totalSize;
        public int totalErrors;
        public int totalWarnings;
        public TimeSpan totalTime;
        public string outputPath;
        public BuildTarget platform;
    }
    public class BuildReport : UnityEngine.Object
    {
        public BuildSummary summary => new BuildSummary();
    }
}
