using System;
using System.Collections.Generic;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Save
{
    public sealed class SaveMigrationException : Exception
    {
        public SaveMigrationException(string message) : base(message) { }
    }

    /// <summary>
    /// Ordered schema migrations operating on the JSON DOM before deserialization, so old saves
    /// never need old C# types. Each step upgrades exactly one version.
    /// </summary>
    public static class SaveMigrations
    {
        private static readonly Dictionary<int, Action<JsonNode>> Steps = new Dictionary<int, Action<JsonNode>>
        {
            { 1, MigrateV1ToV2 },
            { 2, MigrateV2ToV3 },
            { 3, MigrateV3ToV4 },
        };

        /// <summary>Returns the migrated document (mutated in place) and the resulting version.</summary>
        public static int Migrate(JsonNode saveRoot)
        {
            if (saveRoot == null || !saveRoot.IsObject) throw new SaveMigrationException("Save root is not an object");
            int version = saveRoot.GetInt("schemaVersion", 1);
            if (version > SaveSchema.CurrentVersion)
                throw new SaveMigrationException("Save schema v" + version + " is newer than this build (v" + SaveSchema.CurrentVersion + ")");
            while (version < SaveSchema.CurrentVersion)
            {
                if (!Steps.TryGetValue(version, out var step))
                    throw new SaveMigrationException("No migration from save schema v" + version);
                step(saveRoot);
                version++;
                saveRoot.Set("schemaVersion", version);
                var world = saveRoot["world"];
                if (world.IsObject) world.Set("SchemaVersion", version);
            }
            return version;
        }

        // v1 -> v2: RngState array -> Rng object; add Log; default ScenarioId.
        /// <summary>v3: LiftState.PlayerClosed and EconomyState.LastCloseTick were added. Absent members keep their defaults
        /// (false / -1), which is the correct pre-v3 behaviour, so the step only stamps the version.</summary>
        private static void MigrateV2ToV3(JsonNode root) { }

        /// <summary>v4: VehicleAiState gained LanesSkipped, RefuseTimer, StuckCount, FuelDeniedTimer, JobMode, LaneAbandoned and ParkedStuckTick, WorkTask gained BlockedTick, SurfaceZone gained SnowScore, FuelDepotState gained AutoOrder (true). All default to their pre-v4 meaning (zero, Idle, false, -1, a full clearance score),
        /// which is the state of an AI that has not refused, stuck or been turned away from the depot, so the step only stamps.</summary>
        private static void MigrateV3ToV4(JsonNode root) { }

        private static void MigrateV1ToV2(JsonNode root)
        {
            var world = root["world"];
            if (!world.IsObject) throw new SaveMigrationException("v1 save has no world object");
            if (!world.Has("Rng"))
            {
                var rng = JsonNode.NewObject();
                var arr = world["RngState"];
                if (arr.IsArray && arr.Count >= 2)
                {
                    rng.Set("S0", JsonNode.FromULong(arr[0].AsULong));
                    rng.Set("S1", JsonNode.FromULong(arr[1].AsULong));
                }
                else
                {
                    var seeded = new Random.XorShift128Plus(world.GetInt("Seed", 1));
                    rng.Set("S0", JsonNode.FromULong(seeded.S0));
                    rng.Set("S1", JsonNode.FromULong(seeded.S1));
                }
                world.Set("Rng", rng);
                world.Remove("RngState");
            }
            if (!world.Has("Log")) world.Set("Log", JsonNode.NewArray());
            if (!world.Has("ScenarioId") || !world["ScenarioId"].IsString || string.IsNullOrEmpty(world["ScenarioId"].AsString))
                world.Set("ScenarioId", "default");
        }
    }
}
