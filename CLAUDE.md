# CLAUDE.md — working in this repository

Alpine Resort Simulator is a Unity 6 LTS (6000.0.x) ski-resort management and vehicle
simulation. **Everything is authored as text.** There is no Unity Editor in the loop when
code is written, so the repository must always be openable cold.

## Non-negotiable constraints

1. **Code-first scene.** `Assets/Scenes/Boot.unity` contains one GameObject with the
   `Bootstrap` MonoBehaviour and nothing else. Terrain, cameras, lights, vehicles, lifts, UI:
   all created at runtime by `Bootstrap` and the systems it starts. Never add scene objects,
   prefabs, ScriptableObject `.asset` files, `.inputactions`, `PanelSettings`, materials or any
   binary asset.
2. **Every asset has a deterministic `.meta`.** Run `python3 tools/gen_meta.py` after adding
   any file or folder under `Assets/`. GUIDs are UUIDv5 of the project-relative path; they are
   generated once and never regenerated. `python3 tools/gen_meta.py --check` must pass (CI runs it).
   Cross-references (scene -> script, EditorBuildSettings -> scene, GraphicsSettings -> shader)
   use the same derivation: `python3 tools/gen_meta.py --guid Assets/Path/File.ext`.
3. **Assembly boundaries.**
   - `Assets/Scripts/Core` → `AlpineSim.Core` (asmdef has `noEngineReferences: true`). The
     entire simulation lives here. It must never reference `UnityEngine` or `UnityEditor`,
     `System.Random`, `UnityEngine.Random`, `DateTime.Now`, or platform APIs. It uses its own
     `Vec2`/`Vec3`, the seeded `XorShift128Plus` in `SimContext.Rng`, and its own JSON code.
   - `Assets/Scripts/Unity` → `AlpineSim.Unity`. MonoBehaviours, rendering, input, uGUI. A thin
     view: it reads `WorldState`, subscribes to `SimEvents`, and calls Core APIs. Core never
     knows a GameObject exists.
   - `Assets/Scripts/Tests` → `AlpineSim.Tests`. EditMode NUnit tests that reference only Core and
     use the NUnit 3 subset shared by Unity's test framework and NUnit 3.13 (`[Test]`,
     `[TestCase]`, `Assert.*`, `CollectionAssert`). No `Assert.Multiple`, no `UnityEngine`.
   - `Assets/Scripts/Editor` → `AlpineSim.Editor`. Only the CI build script.
4. **`dotnet test` before every commit.** `sim/AlpineSim.Core.csproj` and
   `sim/AlpineSim.Core.Tests.csproj` compile the exact same Core and Tests sources through a
   `<Compile Include="../Assets/Scripts/Core/**/*.cs" />` glob. Run
   `dotnet test sim/AlpineSim.Core.Tests.csproj` and
   `dotnet build sim/AlpineSim.Unity.CompileCheck.csproj` (compile-checks the Unity layer against
   the API stubs in `sim/UnityStubs`). If a Unity API you need is missing from the stubs, add it
   with the **exact** real signature; never bend a stub to make wrong code compile.
5. **Data in JSON, never in code.** Every balance number lives in
   `Assets/StreamingAssets/Data/*.json` and is read through `TuningData` (dotted keys, e.g.
   `ctx.Tuning.F("snow.freshDensityKgM3")`) or the typed tables in `GameData`. A missing tuning
   key throws; do not add fallbacks. Every anchor in `tuning.json` carries `value`, `min`, `max`,
   `unit`, `comment` (its real-world basis). Machines, attachments, lifts, guests, climate, economy
   and construction stages are records in their JSON files; adding a machine is a JSON record plus
   a procedural mesh recipe, never a class.
6. **Determinism.** Same seed + same inputs = same `WorldState` hash. All randomness goes through
   `ctx.Rng`. Systems are ticked at a fixed 20 Hz in the order listed in `Simulation.TickOrder`
   (see `docs/ARCHITECTURE.md`). Time compression changes ticks per frame, never `dt`. Iteration
   over dictionaries must be order-stable (use lists or sort keys).
7. **Save format.** Save = JSON of `WorldState` (public fields only) plus `schemaVersion`.
   Changing the shape of `WorldState` means bumping `SaveSchema.CurrentVersion` and adding a
   migration step in `SaveMigrations` that operates on the JSON DOM.
8. **Cross-platform.** Windows x86-64 and macOS Universal are equal targets. No `DllImport`,
   no platform-conditional gameplay code, no Windows path separators; file IO through
   `Path.Combine` and `Application.persistentDataPath`. Every `using`, path and asset reference
   matches file casing exactly (case-sensitive filesystems).
9. **Rendering.** Built-in Render Pipeline only. Shaders are hand-written `.shader` /
   `.compute` files in `Assets/Shaders`. Meshes and textures are generated at runtime.
   Fonts: `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`. No TextMeshPro, URP/HDRP,
   Cinemachine, DOTS, or Asset Store packages.
10. **No real manufacturer names or trade dress.** Original machine and lift names; real-world
    performance figures only.

## Milestone partial-class convention

Each milestone (M1..M8) wires itself in through partial-class files so every milestone commit
builds on its own:

- `Simulation.Mn.cs` implements `partial void RegisterMnSystems(List<ISimSystem>)`.
- `WorldState.Mn.cs` adds that milestone's state slice as public fields.
- `GameData.Mn.cs` implements `partial void LoadMn()` / `ValidateMn()`.
- `ScenarioData.Mn.cs` adds scenario fields the milestone consumes.
- `Bootstrap.Mn.cs` implements `partial void StartMn()` / `TeardownMn()` to create views and UI.
- `InputBindings.Mn.cs` adds input maps.

Absent partial implementations compile to nothing, so earlier milestones never reference later ones.

## Workflow

```
python3 tools/gen_meta.py                      # after adding files under Assets/
dotnet test sim/AlpineSim.Core.Tests.csproj    # must be green
dotnet build sim/AlpineSim.Unity.CompileCheck.csproj
python3 tools/gen_meta.py --check
```

Record any decision the design brief left open in `docs/DECISIONS.md` with its rationale.
Keep `docs/BUILD_ORDER.md` status current. Keep `docs/TUNING.md` in sync with `tuning.json`.
