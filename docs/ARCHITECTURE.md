# Architecture

## Assemblies

```
AlpineSim.Core   (netstandard 2.1, no UnityEngine)  ← the simulation
AlpineSim.Unity  (MonoBehaviours, rendering, input, uGUI) ← a thin view of Core
AlpineSim.Tests  (EditMode NUnit; also compiled by dotnet test)
AlpineSim.Editor (CI build script only)
```

`sim/AlpineSim.Core.csproj` and `sim/AlpineSim.Core.Tests.csproj` compile the exact same source
files via globs, so CI builds and tests the simulation with `dotnet test` and no Unity licence.

## Composition

- `GameData.Load(dir)` reads every `StreamingAssets/Data/*.json` table (tuning, render,
  scenarios; later milestones add vehicles, attachments, stations, lifts, climate, guests,
  economy, construction, tasks, operators).
- `Simulation.CreateNew(data, seed, scenario)` builds a `WorldState`, the derived
  `TerrainData`, the `SimEvents` bus and the ordered system list, then calls
  `ISimSystem.Initialize(ctx, newGame: true)` on each system so it populates its state slice from
  the scenario. `Simulation.FromState(data, world)` does the same with `newGame: false`.
- `SimulationClock.Advance(realSeconds)` accumulates real time × compression (1/4/16/60) and calls
  `Simulation.Step()` a whole number of times. `Step()` increments the tick and calls
  `Tick(ctx, 0.05f)` on each system in order. `dt` is a constant.

## Tick order

`Simulation.TickOrder` fixes the order regardless of which milestones are compiled:

```
Weather → Snowmaking → Snow → Roads → Lifts → Construction → Guests → Vehicles → Tasks → Fleet → Economy → Campaign → Scaffold
```

Hourly work runs inside `Tick` when `ctx.Time.IsHourStart`; `HourChangedEvent` /
`DayChangedEvent` are published after the systems ran.

## State

`WorldState` is a plain object graph of public fields, split into per-milestone partial files.
Rules: parameterless constructors, no references to systems or views, derived caches marked
`[JsonIgnore]` and rebuilt in `Initialize`. The PRNG (`XorShift128Plus`) lives inside
`WorldState`, so a save resumes the exact random sequence.

## Determinism

Same seed + same input sequence = same state. All randomness goes through `ctx.Rng`; there is no
`System.Random`, `UnityEngine.Random` or `DateTime.Now` in Core. `StateHasher.Hash(world)` is
FNV-1a 64 over the canonical sorted-key JSON of `WorldState`, used by the determinism test and
the debug HUD.

## Save format

```
{ "schemaVersion": 2, "gameVersion": "0.1.0", "savedAtTick": 1234, "world": { ...WorldState... } }
```

Saves live in `Application.persistentDataPath/saves/<slot>.arsave.json`. Loading parses the
document, runs `SaveMigrations.Migrate` on the DOM (one step per schema version), then maps to
`WorldState`. Old C# types are never needed.

## Unity layer

`Bootstrap` (the only scene object) loads data, builds the simulation and creates every view:
`TerrainView` (chunked meshes, two LODs), `DayNightLighting`, `CameraRig` (free / chase /
cockpit), `InputBindings` (Input System maps built in C#), `UiRoot` (canvas, HUD, panels).
Views read `WorldState` after ticks and subscribe to `SimEvents`; they never mutate state except
through Core APIs (vehicle input, purchases, placement) that the sim validates.

Coordinate convention: Core uses X east, Y north, Z up in metres. Unity uses X east, Y up,
Z north; `Bootstrap.ToUnity` converts.
