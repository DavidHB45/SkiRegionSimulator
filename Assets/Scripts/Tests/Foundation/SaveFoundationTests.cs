using System.IO;
using AlpineSim.Core.Save;
using AlpineSim.Core.Serialization;
using AlpineSim.Core.Sim;
using NUnit.Framework;

namespace AlpineSim.Tests.Foundation
{
    public class SaveFoundationTests
    {
        [Test]
        public void RoundTripThroughFileIsExact()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 77, "test_small");
            sim.StepMinutes(3);
            sim.Log("hello");
            ulong before = sim.ComputeStateHash();
            string dir = TestEnv.TempDir("save");
            string path = SaveSystem.SavePath(dir, "slot 1");
            SaveSystem.SaveToFile(sim.World, path);
            Assert.IsTrue(File.Exists(path));
            var loaded = SaveSystem.LoadFromFile(path);
            Assert.AreEqual(before, StateHasher.Hash(loaded));
            Assert.AreEqual(sim.World.Time.Tick, loaded.Time.Tick);
            Assert.AreEqual(sim.World.Rng.S0, loaded.Rng.S0);
            Assert.AreEqual(1, SaveSystem.ListSaves(dir).Length);
        }

        [Test]
        public void V1SaveMigratesToCurrent()
        {
            const string v1 = "{ \"schemaVersion\": 1, \"world\": { \"Seed\": 5, \"RngState\": [123, 456], \"Time\": { \"Tick\": 40, \"StartHour\": 6 } } }";
            var world = SaveSystem.Deserialize(v1);
            Assert.AreEqual(SaveSchema.CurrentVersion, world.SchemaVersion);
            Assert.AreEqual(123UL, world.Rng.S0);
            Assert.AreEqual(456UL, world.Rng.S1);
            Assert.AreEqual(40, world.Time.Tick);
            Assert.AreEqual("default", world.ScenarioId);
            Assert.IsNotNull(world.Log);
        }

        [Test]
        public void NewerSchemaIsRejected()
        {
            string doc = "{ \"schemaVersion\": " + (SaveSchema.CurrentVersion + 1) + ", \"world\": {} }";
            Assert.Throws<SaveMigrationException>(() => SaveSystem.Deserialize(doc));
        }

        [Test]
        public void DocumentCarriesVersionAndWorld()
        {
            var sim = Simulation.CreateNew(TestEnv.Data, 1, "test_small");
            var doc = JsonParser.Parse(SaveSystem.Serialize(sim.World));
            Assert.AreEqual(SaveSchema.CurrentVersion, doc.GetInt("schemaVersion"));
            Assert.IsTrue(doc["world"].IsObject);
            Assert.AreEqual("test_small", doc["world"].GetString("ScenarioId"));
        }
    }
}
