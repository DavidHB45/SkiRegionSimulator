using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AlpineSim.Editor
{
    /// <summary>
    /// Headless player build entry point used by CI (game-ci/unity-builder buildMethod).
    /// Parses -buildTarget and -customBuildPath like the default game-ci script, and forces the
    /// macOS build to Universal (Apple Silicon + Intel) so both targets come from one commit.
    /// </summary>
    public static class BuildScript
    {
        public static void Build()
        {
            var args = ParseArgs();
            string targetName = args.TryGetValue("buildTarget", out var t) ? t : "StandaloneWindows64";
            string outPath = args.TryGetValue("customBuildPath", out var p) ? p : Path.Combine("build", targetName, "AlpineResortSimulator");
            var target = (BuildTarget)Enum.Parse(typeof(BuildTarget), targetName);

            if (target == BuildTarget.StandaloneOSX)
            {
                SetMacArchitectureUniversal();
                if (!outPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) outPath += ".app";
            }
            else if (target == BuildTarget.StandaloneWindows64)
            {
                if (!outPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) outPath += ".exe";
            }

            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) scenes.Add(s.path);
            if (scenes.Count == 0) scenes.Add("Assets/Scenes/Boot.unity");

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = outPath,
                target = target,
                options = BuildOptions.None,
            };
            Debug.Log("[BuildScript] Building " + target + " -> " + outPath);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log("[BuildScript] Result: " + summary.result + " size=" + summary.totalSize + " errors=" + summary.totalErrors);
            if (summary.result != BuildResult.Succeeded)
            {
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw new Exception("Build failed: " + summary.result);
            }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Sets <c>UnityEditor.OSXStandalone.UserBuildSettings.architecture</c> to Universal (x64 + ARM64).
        /// That type lives in the macOS Build Support module's editor assembly, which is only present
        /// when the module is installed, so it is resolved by reflection: an Editor without the module
        /// (a Windows-only install) still compiles this script, and CI's macOS image still gets the
        /// Universal setting.
        /// </summary>
        private static void SetMacArchitectureUniversal()
        {
            Type settings = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                settings = asm.GetType("UnityEditor.OSXStandalone.UserBuildSettings", false);
                if (settings != null) break;
            }
            var prop = settings != null ? settings.GetProperty("architecture", BindingFlags.Public | BindingFlags.Static) : null;
            if (prop == null)
            {
                Debug.LogWarning("[BuildScript] macOS Build Support module not found; cannot force Universal architecture.");
                return;
            }
            prop.SetValue(null, Enum.Parse(prop.PropertyType, "x64ARM64"));
        }

        private static Dictionary<string, string> ParseArgs()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var argv = Environment.GetCommandLineArgs();
            for (int i = 0; i < argv.Length; i++)
            {
                if (!argv[i].StartsWith("-", StringComparison.Ordinal)) continue;
                string key = argv[i].TrimStart('-');
                string value = i + 1 < argv.Length && !argv[i + 1].StartsWith("-", StringComparison.Ordinal) ? argv[i + 1] : "";
                dict[key] = value;
            }
            return dict;
        }
    }
}
