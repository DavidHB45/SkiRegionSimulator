using System;
using System.IO;
using AlpineSim.Core.Data;
using AlpineSim.Core.Save;
using AlpineSim.Core.Sim;
using AlpineSim.Unity.Cameras;
using AlpineSim.Unity.Input;
using AlpineSim.Unity.Terrain;
using AlpineSim.Unity.UI;
using UnityEngine;

namespace AlpineSim.Unity
{
    /// <summary>
    /// The only hand-authored component in the project (Boot.unity). Loads data, builds the
    /// simulation, and constructs every view, camera, light and UI element in code.
    /// Milestone partial files (Bootstrap.M1.cs ...) hook in through the Start/Rebuild/Teardown hooks.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed partial class Bootstrap : MonoBehaviour
    {
        [Header("Assets referenced from Boot.unity (GUIDs are fixed by tools/gen_meta.py)")]
        public Shader snowSurfaceShader;
        public ComputeShader snowDeformCompute;
        public Shader debugOverlayShader;

        [Header("Startup")]
        public int seed = 20260909;
        public string dataFolderOverride = "";
        public string scenarioId = "";

        public static Bootstrap Instance { get; private set; }

        public GameData Data { get; private set; }
        public Simulation Sim { get; private set; }
        public SimulationClock Clock { get; private set; }
        public SimRunner Runner { get; private set; }
        public InputBindings Input { get; private set; }
        public CameraRig Cameras { get; private set; }
        public DayNightLighting Lighting { get; private set; }
        public TerrainView TerrainView { get; private set; }
        public UiRoot Ui { get; private set; }

        /// <summary>Root for everything that belongs to the current world (destroyed on new game / load).</summary>
        public Transform WorldRoot { get; private set; }

        public event Action WorldRebuilt;

        public string DataDirectory => string.IsNullOrEmpty(dataFolderOverride)
            ? Path.Combine(Application.streamingAssetsPath, "Data")
            : dataFolderOverride;

        public string SavesDirectory => SaveSystem.SavesDirectory(Application.persistentDataPath);

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;

            try
            {
                Data = GameData.Load(DataDirectory);
                foreach (var w in Data.Warnings) Debug.LogWarning("[Data] " + w);
            }
            catch (Exception e)
            {
                Debug.LogError("Alpine Resort Simulator failed to load data from " + DataDirectory + ": " + e.Message);
                throw;
            }

            Input = new InputBindings();
            Input.Enable();

            var camGo = new GameObject("CameraRig");
            camGo.transform.SetParent(transform, false);
            Cameras = camGo.AddComponent<CameraRig>();
            Cameras.Construct(this);

            var lightGo = new GameObject("DayNightLighting");
            lightGo.transform.SetParent(transform, false);
            Lighting = lightGo.AddComponent<DayNightLighting>();

            Ui = UiRoot.Create(this);

            var runnerGo = new GameObject("SimRunner");
            runnerGo.transform.SetParent(transform, false);
            Runner = runnerGo.AddComponent<SimRunner>();

            StartNewGame(seed, scenarioId);
        }

        // ------------------------------------------------------------------ world lifecycle
        public void StartNewGame(int newSeed, string scenario = null)
        {
            var sim = Simulation.CreateNew(Data, newSeed, string.IsNullOrEmpty(scenario) ? null : scenario);
            BindSimulation(sim);
            Sim.Log("Welcome to " + Sim.Scenario.DisplayName + ". It is " + Sim.World.Time + ". The night shift is yours.");
        }

        public bool LoadGame(string slot)
        {
            string path = SaveSystem.SavePath(Application.persistentDataPath, slot);
            if (!File.Exists(path)) { Debug.LogWarning("No save at " + path); return false; }
            try
            {
                var world = SaveSystem.LoadFromFile(path);
                var sim = Simulation.FromState(Data, world);
                BindSimulation(sim);
                Sim.Log("Loaded '" + slot + "'.");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("Load failed: " + e);
                return false;
            }
        }

        public void SaveGame(string slot)
        {
            string path = SaveSystem.SavePath(Application.persistentDataPath, slot);
            SaveSystem.SaveToFile(Sim.World, path);
            Sim.Log("Saved to '" + slot + "'.");
        }

        private void BindSimulation(Simulation sim)
        {
            TeardownWorld();
            Sim = sim;
            Clock = new SimulationClock(sim);
            Runner.Bind(this);

            WorldRoot = new GameObject("World").transform;

            TerrainView = new GameObject("Terrain").AddComponent<TerrainView>();
            TerrainView.transform.SetParent(WorldRoot, false);
            TerrainView.Build(this);

            Lighting.Bind(this);
            Cameras.OnWorldBuilt();

            StartM1();
            StartM2();
            StartM3();
            StartM4();
            StartM5();
            StartM6();
            StartM7();
            StartM8();

            Ui.OnWorldBuilt();
            WorldRebuilt?.Invoke();
        }

        private void TeardownWorld()
        {
            if (Sim == null) return;
            TeardownM8(); TeardownM7(); TeardownM6(); TeardownM5(); TeardownM4(); TeardownM3(); TeardownM2(); TeardownM1();
            Ui.OnWorldTeardown();
            Sim.Events.Clear();
            if (WorldRoot != null) Destroy(WorldRoot.gameObject);
            WorldRoot = null;
            TerrainView = null;
            Sim = null;
            Clock = null;
        }

        // Milestone hooks (implemented in Bootstrap.Mn.cs; absent implementations compile away).
        partial void StartM1(); partial void TeardownM1();
        partial void StartM2(); partial void TeardownM2();
        partial void StartM3(); partial void TeardownM3();
        partial void StartM4(); partial void TeardownM4();
        partial void StartM5(); partial void TeardownM5();
        partial void StartM6(); partial void TeardownM6();
        partial void StartM7(); partial void TeardownM7();
        partial void StartM8(); partial void TeardownM8();

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Input?.Disable();
                Instance = null;
            }
        }

        // ------------------------------------------------------------------ helpers shared by views
        /// <summary>Map (X east, Y north, Z up) to Unity (X east, Y up, Z north).</summary>
        public static Vector3 ToUnity(AlpineSim.Core.Math.Vec3 v) => new Vector3(v.X, v.Z, v.Y);
        public static Vector3 ToUnity(AlpineSim.Core.Math.Vec2 v, float height) => new Vector3(v.X, height, v.Y);
        public static AlpineSim.Core.Math.Vec2 ToMap(Vector3 v) => new AlpineSim.Core.Math.Vec2(v.x, v.z);

        /// <summary>World-space position on the bare terrain surface for a map point.</summary>
        public Vector3 SurfacePoint(float mapX, float mapY) => new Vector3(mapX, Sim.Terrain.SampleHeight(mapX, mapY), mapY);
    }
}
