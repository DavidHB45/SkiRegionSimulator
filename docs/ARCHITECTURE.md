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

## Systems (M1-M6)

| System | Slot | State slice | What it owns |
| --- | --- | --- | --- |
| `WeatherSystem` | Weather | `Weather` | Hourly timeline generated per season day from `climate.json` (seed-derived streams), degrading 120 h forecast, lapse-rate sampling, lightning, storms |
| `SnowmakingSystem` | Snowmaking | `Snowmaking` | Guns (machines with `MakeSnow`), hydrants, pump/compressor stations, reservoir, wet-bulb output curves, water/energy accounting, cone deposition |
| `SnowSystem` | Snow | `Snow`, `Pistes` | 0.5 m sparse grid; hourly weather pass amortised over the hour by chunk window; PQI per segment/piste/resort published hourly |
| `RoadSystem` | Roads | `Pistes.Zones` | Access road, lots and lift ramps: clearance scores, salt/brine, auto plow/sand jobs, access and parking factors |
| `LiftSystem` | Lifts | `Lifts` | Queue simulation at data throughput, ride time, wind/lightning/cold holds, breakdowns and inspections, condition, staff wages, energy |
| `ConstructionSystem` | Construction | `Construction` | Staking with terrain gating (length, vertical, span, grade, gorge), stage chains from `construction.json` as driven tasks, weather windows, helicopter mode, run grading |
| `GuestSystem` | Guests | `Guests` | Daily demand from acts, price, reputation, weather and the snow report; cohorts arriving, queueing, riding, skiing runs cell by cell (traffic into the snow grid), eating, leaving; satisfaction and reputation |
| `VehicleSystem` | Vehicles | `Vehicles` | Driving models (tracked, wheeled, articulated, walk-behind), snow contact (blade, tiller, blower, bucket, spreader), fuel and cold start, wear per subsystem, failures, AI operator (`VehicleAi`), routes |
| `TaskSystem` | Tasks | `TaskBoard` | The job board: candidates by role and licence, dispatch, accrual by AI or player, groom jobs posted at close and completed by coverage, deadlines |
| `FleetSystem` | Fleet | `Fleet` | Operators (hire, licences, training, fatigue, accidents), market (new/used/lease/rent, hidden defects, inspections), workshop tiers and service jobs, parts, fuel depot and deliveries, service calls, insurance and depreciation |
| `EconomySystem` | Economy | `Economy` | Ledger, daily and weekly close, loans, net worth, act promotion, ticket pricing |
| `M8ScaffoldSystem` | Campaign | - | No-op (D-020) |
| `M7ScaffoldSystem` | Scaffold | - | No-op (D-020) |

Cross-system calls go through `SimContext.System<T>()` / `TryGetSystem<T>()` and hooks
(`VehicleSystem.OperatorLookup`, `DepotArrival`, `ExtraWearFactor`, `TaskSystem.LicenceCheck`,
`EconomySystem.FleetValuation`), which the later milestone installs in its `Initialize`; an earlier
milestone built alone leaves the hook null and behaves as before.

## Unity layer

`Bootstrap` (the only scene object) loads data, builds the simulation and creates every view:
`TerrainView` (chunked meshes, two LODs), `DayNightLighting`, `CameraRig` (free / chase /
cockpit), `InputBindings` (Input System maps built in C#), `UiRoot` (canvas, HUD, panels).
Views read `WorldState` after ticks and subscribe to `SimEvents`; they never mutate state except
through Core APIs (vehicle input, purchases, placement) that the sim validates.

Coordinate convention: Core uses X east, Y north, Z up in metres. Unity uses X east, Y up,
Z north; `Bootstrap.ToUnity` converts. Heading is radians counter-clockwise from +X in Core and
`yaw = 90 - heading` degrees in Unity.

Per milestone (`Bootstrap.Mn.cs`):

| Milestone | Views | Panels (hotkey) |
| --- | --- | --- |
| M1 | `SnowView` + `SnowMapUploader` (compute or CPU upload of dirty chunks into the snow map), debug overlay | - |
| M2 | `VehicleViewManager` / `VehicleView` (procedural chassis, implements, headlights, 20 Hz interpolation), chase and cockpit cameras | Job Board (F2) |
| M3 | `LiftViewManager` / `LiftView` (towers, terminals, sagging rope, instanced carriers), `GuestView` (instanced cohorts) | Runs & Snow (F1), Lifts (F3), Books (F4) |
| M4 | `PlacementController` (modal click staking on the heightfield), `ConstructionView` (plan preview, corridors) | Construction (F11) |
| M5 | `SnowmakingView` (hydrants, plumes) | Weather (F6), Snowmaking (F7) |
| M6 | - | Fleet & Garage (F8), Staff (F10) |

Panels derive from `UiPanel` (title bar, close, vertical body) and refresh themselves while
visible; the HUD takes status and legend providers from views instead of knowing them.
