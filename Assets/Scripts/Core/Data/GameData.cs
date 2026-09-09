using System;
using System.Collections.Generic;
using System.IO;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Data
{
    public sealed class DataLoadException : Exception
    {
        public DataLoadException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// All static, designer-editable content, loaded from StreamingAssets/Data/*.json.
    /// Balance never lives in C#. Milestone partial files add their tables and loaders.
    /// </summary>
    public sealed partial class GameData
    {
        public string SourceDirectory { get; private set; } = "";
        public TuningData Tuning { get; private set; } = TuningData.Empty();
        public RenderData Render { get; private set; } = new RenderData();
        public List<ScenarioData> Scenarios { get; private set; } = new List<ScenarioData>();
        public string DefaultScenarioId { get; private set; } = "default";

        /// <summary>Non-fatal validation messages collected while loading.</summary>
        public List<string> Warnings { get; } = new List<string>();

        public static GameData Load(string dataDirectory)
        {
            if (string.IsNullOrEmpty(dataDirectory) || !Directory.Exists(dataDirectory))
                throw new DataLoadException("Data directory not found: " + dataDirectory);
            var d = new GameData { SourceDirectory = dataDirectory };
            d.LoadFoundation();
            d.LoadM1();
            d.LoadM2();
            d.LoadM3();
            d.LoadM4();
            d.LoadM5();
            d.LoadM6();
            d.LoadM7();
            d.LoadM8();
            d.Validate();
            return d;
        }

        partial void LoadM1();
        partial void LoadM2();
        partial void LoadM3();
        partial void LoadM4();
        partial void LoadM5();
        partial void LoadM6();
        partial void LoadM7();
        partial void LoadM8();

        partial void ValidateM1();
        partial void ValidateM2();
        partial void ValidateM3();
        partial void ValidateM4();
        partial void ValidateM5();
        partial void ValidateM6();

        private void Validate()
        {
            ValidateM1(); ValidateM2(); ValidateM3(); ValidateM4(); ValidateM5(); ValidateM6();
        }

        private void LoadFoundation()
        {
            Tuning = TuningData.FromJson(ReadJson("tuning.json"));
            var render = ReadJsonOptional("render.json");
            if (render != null) Render = JsonMapper.FromJson<RenderData>(render);
            var scen = ReadJson("scenarios.json");
            Scenarios = JsonMapper.FromJson<List<ScenarioData>>(scen["scenarios"]);
            DefaultScenarioId = scen.GetString("default", Scenarios.Count > 0 ? Scenarios[0].Id : "default");
            if (Scenarios.Count == 0) throw new DataLoadException("scenarios.json defines no scenarios");
        }

        public ScenarioData GetScenario(string id)
        {
            if (string.IsNullOrEmpty(id)) id = DefaultScenarioId;
            foreach (var s in Scenarios) if (s.Id == id) return s;
            throw new DataLoadException("Unknown scenario '" + id + "'");
        }

        // ------------------------------------------------------------------ file helpers
        public string PathFor(string fileName) => Path.Combine(SourceDirectory, fileName);

        public JsonNode ReadJson(string fileName)
        {
            var node = ReadJsonOptional(fileName);
            if (node == null) throw new DataLoadException("Missing data file: " + PathFor(fileName));
            return node;
        }

        public JsonNode ReadJsonOptional(string fileName)
        {
            string path = PathFor(fileName);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonParser.Parse(File.ReadAllText(path));
            }
            catch (JsonParseException e)
            {
                throw new DataLoadException("Failed to parse " + path + ": " + e.Message, e);
            }
        }

        /// <summary>Walks up from a start directory to find Assets/StreamingAssets/Data (used by tests and tools).</summary>
        public static string FindDataDirectory(string startDirectory)
        {
            string dir = startDirectory;
            for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
            {
                string candidate = Path.Combine(dir, "Assets", "StreamingAssets", "Data");
                if (Directory.Exists(candidate)) return candidate;
                var parent = Directory.GetParent(dir);
                dir = parent?.FullName;
            }
            return null;
        }
    }
}
