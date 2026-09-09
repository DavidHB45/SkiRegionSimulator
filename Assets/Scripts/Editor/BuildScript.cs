using System;
using System.Collections.Generic;
using System.IO;
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
                UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.x64ARM64;
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
