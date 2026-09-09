using System;
using System.IO;
using AlpineSim.Core.Serialization;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Save
{
    /// <summary>
    /// Save = JSON document { schemaVersion, gameVersion, world: WorldState }.
    /// Load = parse, migrate the DOM to the current schema, then map to WorldState.
    /// All file IO uses Path.Combine; the Unity layer supplies Application.persistentDataPath.
    /// </summary>
    public static class SaveSystem
    {
        public const string GameVersion = "0.1.0";
        public const string SaveExtension = ".arsave.json";

        public static string SavesDirectory(string persistentDataPath) => Path.Combine(persistentDataPath, "saves");

        public static string SavePath(string persistentDataPath, string slotName) =>
            Path.Combine(SavesDirectory(persistentDataPath), SanitizeSlot(slotName) + SaveExtension);

        public static string SanitizeSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot)) return "autosave";
            var chars = slot.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            return new string(chars);
        }

        public static JsonNode ToDocument(WorldState world)
        {
            world.SchemaVersion = SaveSchema.CurrentVersion;
            var root = JsonNode.NewObject();
            root.Set("schemaVersion", SaveSchema.CurrentVersion);
            root.Set("gameVersion", GameVersion);
            root.Set("savedAtTick", world.Time.Tick);
            root.Set("world", JsonMapper.ToJson(world));
            return root;
        }

        public static string Serialize(WorldState world, bool pretty = false) => JsonWriter.Write(ToDocument(world), pretty);

        public static WorldState Deserialize(string json)
        {
            var root = JsonParser.Parse(json);
            SaveMigrations.Migrate(root);
            var world = JsonMapper.FromJson<WorldState>(root["world"]);
            world.SchemaVersion = SaveSchema.CurrentVersion;
            if (world.Rng == null) world.Rng = new Random.XorShift128Plus(world.Seed);
            if (world.Time == null) world.Time = new SimTime();
            return world;
        }

        public static void SaveToFile(WorldState world, string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize(world));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static WorldState LoadFromFile(string path) => Deserialize(File.ReadAllText(path));

        public static string[] ListSaves(string persistentDataPath)
        {
            string dir = SavesDirectory(persistentDataPath);
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            var files = Directory.GetFiles(dir, "*" + SaveExtension);
            Array.Sort(files, StringComparer.Ordinal);
            return files;
        }
    }
}
