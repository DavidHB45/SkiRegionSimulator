# Decisions

Decisions taken where the design brief was silent or where two constraints had to be reconciled.
Each entry: the decision, the alternatives, and why.

## D-001 Own JSON implementation instead of Newtonsoft or JsonUtility
`AlpineSim.Core` cannot reference `UnityEngine.JsonUtility`, and depending on
`com.unity.nuget.newtonsoft-json` would put a package on the critical path of both the Unity build
and the `/sim` mirror. Core ships a ~600-line JSON DOM, parser, writer and reflection mapper
(`Serialization/`). Floats are written with round-trip formatting and parsed back exactly; integer
tokens keep their text so `ulong` PRNG state survives. The writer can sort keys for hashing.

## D-002 Deterministic GUIDs by UUIDv5 of the path
No editor is available to allocate GUIDs. `tools/gen_meta.py` derives each GUID once from the
project-relative path; existing metas are never rewritten, so renaming a file keeps the old meta
only if you move the meta with it (as the editor would). Cross-references in YAML are computed
with the same function.

## D-003 Milestone partial classes
"Ship milestones as sequential commits" and "each milestone independently verifiable" conflict
with a single composition root that references every system. Each milestone registers itself
through C# partial methods (`Simulation.Mn.cs`, `WorldState.Mn.cs`, `Bootstrap.Mn.cs`, ...); an
absent implementation compiles away, so each milestone commit builds and tests on its own.

## D-004 Unity API stubs for compile checking
The Unity layer can never be compiled here, which is where the highest defect risk sits.
`sim/UnityStubs` mirrors the signatures of the API subset the project uses and
`AlpineSim.Unity.CompileCheck.csproj` compiles `Assets/Scripts/Unity` against it in CI. The stubs
are compile-only and must never be referenced by Unity. A stub with a wrong signature is a bug.

## D-005 Editor assembly for the CI build script
The brief's layout has no `Editor` folder. `Assets/Scripts/Editor/BuildScript.cs`
(`AlpineSim.Editor`, editor-only asmdef) exists solely so game-ci can build macOS Universal
(x64 + ARM64) from one commit; game-ci's default build method does not set the macOS
architecture. It is plain text and needs no editor to author.

## D-006 Terrain is derived, not saved
The 2048×2048 heightmap is regenerated from (scenario id, seed) on load. Saving 16 MB of floats
would slow saves and could drift from the generator; determinism guarantees the same terrain.
Corridor smoothing for pistes and roads (M1+) is applied deterministically after generation.

## D-007 Time: 1x is real time
At 1x, one simulated second is one real second; the day is 24 real minutes at 60x. The night shift
is meant to be driven at 1x–4x and skipped with 16x/60x. Guests are simulated as cohorts
(`simulation.guestsPerAgent`) so 60x stays cheap; tests raise the cohort size for 30-day runs.

## D-008 Unity version pin
`ProjectVersion.txt` pins 6000.0.61f1 because game-ci publishes `windows-mono` and `mac-mono`
editor images for it (verified on Docker Hub at authoring time). Any later 6000.0.x opens the
project without changes.

## D-009 Shader inclusion
Code-built materials use `Shader.Find`, which only works for shaders included in the build. The
snow and overlay shaders are referenced by GUID from both `Boot.unity` (as `Bootstrap` fields)
and `GraphicsSettings.asset` (Always Included Shaders); the compute shader is referenced from
`Boot.unity`. `Shader.Find` remains as an in-editor fallback.

## D-010 Render settings are data too
`render.json` (`RenderData`) holds chunk size, LOD distances, snow-map resolution, camera speeds.
Not balance, but the "no numbers in code" rule is simpler to keep absolute.
