using System;
using System.IO;
using AlpineSim.Core.Data;
using NUnit.Framework;

#if !UNITY_INCLUDE_TESTS
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]
#endif

namespace AlpineSim.Tests
{
    /// <summary>
    /// Shared test environment. Locates Assets/StreamingAssets/Data whether the tests run under
    /// `dotnet test` (bin/... several levels below the repo) or inside the Unity editor (cwd = project root).
    /// GameData is loaded once and tuning is cloned per test so overrides never leak.
    /// </summary>
    public static class TestEnv
    {
        private static GameData _data;
        private static readonly object Lock = new object();

        public static string DataDirectory
        {
            get
            {
                string dir = GameData.FindDataDirectory(AppContext.BaseDirectory)
                             ?? GameData.FindDataDirectory(Directory.GetCurrentDirectory());
                if (dir == null) throw new DirectoryNotFoundException("Could not locate Assets/StreamingAssets/Data from " + AppContext.BaseDirectory);
                return dir;
            }
        }

        /// <summary>Shared, read-only GameData. Do not call Tuning.Override on it; use <see cref="FreshData"/>.</summary>
        public static GameData Data
        {
            get
            {
                lock (Lock)
                {
                    if (_data == null) _data = GameData.Load(DataDirectory);
                    return _data;
                }
            }
        }

        /// <summary>A private GameData instance whose tuning can be overridden safely.</summary>
        public static GameData FreshData() => GameData.Load(DataDirectory);

        public static string TempDir(string name)
        {
            string dir = Path.Combine(Path.GetTempPath(), "alpinesim-tests", name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
