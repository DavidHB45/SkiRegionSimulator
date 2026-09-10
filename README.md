# Alpine Resort Simulator

A ski-resort management and vehicle simulation for Windows and macOS, built with Unity 6 LTS
(6000.0.x), C#, and the Built-in Render Pipeline. You own a small alpine resort: at night you
drive the groomers, blowers, plows and haulers that shape the snow; by day you watch guests ride
the lifts, ski the runs, and degrade the surface you prepared, while the ledger tells you whether
last night's work paid.

The whole project is authored as text: one hand-written scene, code-built scenes and UI,
procedural terrain and meshes, and every balance number in JSON.

## Quick start

```
git clone <this repo>
cd SkiRegionSimulator
dotnet test sim/AlpineSim.Core.Tests.csproj        # simulation core, no Unity needed
```

Then open the folder in **Unity 6 LTS (6000.0.61f1 or later 6000.0.x)** via Unity Hub, open
`Assets/Scenes/Boot.unity`, and press Play. The `Bootstrap` component builds the world.

Requirements: .NET SDK 8.0 (for the `dotnet` mirror build), Unity 6000.0.x with the
Windows and macOS build support modules, Python 3 (for `tools/gen_meta.py`).

## Controls

| Key | Action |
| --- | --- |
| W A S D / Q E | Fly camera (hold Shift for fast; scroll wheel changes speed) |
| Right mouse (hold) | Look |
| C | Cycle camera (free / chase / cockpit when in a vehicle) |
| P or Space | Pause |
| 1 2 3 4 / Tab | Time compression 1x / 4x / 16x / 60x (cycle) |
| F1 Runs & Snow | Every run with PQI, traffic, open/close; post grooming jobs |
| F2 Job Board | Tasks with status and progress; dispatch machines with operators |
| F3 Lifts | Status, queue and wait, loads, condition; open/close, singles line |
| F4 Books | Cash, net worth, act, reputation, ticket price, loans, daily close, ledger |
| F6 Weather | Base/summit conditions, wet-bulb, next 24 h, five-day outlook |
| F7 Snowmaking | Reservoir, pumps, air, guns, hydrants, stations; place guns and hydrants |
| F8 Fleet & Garage | Machines, market, attachments, workshop, parts, fuel depot |
| F10 Staff | Operators, licences, training, assignment; candidates to hire |
| F11 Construction | Stake lifts and runs, commit, follow the driven stage chain |
| O | Cycle the snow debug overlay (depth / density / roughness / PQI / groom age / surface) |
| F5 / F9 | Quick save / quick load |
| Enter | Enter or leave the nearest vehicle |
| Left / right mouse, Backspace, Enter, scroll | While staking: place point / cancel / undo / finish / aim |

Vehicle controls (in a machine): W/S throttle, A/D steer, Space brake, R/F blade up/down,
Q/E blade angle, Z/X blade tilt, T tiller, V implement (blower / spreader / PTO), G work at a
site (hold), L lights, I engine, C camera (chase / cockpit), Enter exit.

## Repository layout

```
Assets/Scenes/Boot.unity            single hand-authored scene: one Bootstrap GameObject
Assets/Shaders/                     SnowSurface.shader, SnowDeform.compute, Ground.shader, overlay
Assets/StreamingAssets/Data/*.json  all balance data, hot-editable
Assets/Scripts/Core                 AlpineSim.Core  (the simulation; no UnityEngine)
Assets/Scripts/Unity                AlpineSim.Unity (views, rendering, input, uGUI)
Assets/Scripts/Tests                AlpineSim.Tests (EditMode NUnit; also run by dotnet test)
Assets/Scripts/Editor               AlpineSim.Editor (CI build script only)
ProjectSettings/                    hand-authored YAML
Packages/manifest.json              Input System, uGUI, Test Framework
sim/                                netstandard2.1 mirror csproj of Core + tests + Unity API stubs
tools/gen_meta.py                   deterministic .meta generator (run after adding assets)
docs/                               DESIGN, ARCHITECTURE, BUILD_ORDER, TUNING, DECISIONS
.github/workflows/ci.yml            sim-test (always), unity-test and build (with secrets)
```

## Building players

Locally: File > Build Settings, target Windows x86-64 or macOS, or run
`AlpineSim.Editor.BuildScript.Build` from the command line:

```
Unity -batchmode -quit -projectPath . -executeMethod AlpineSim.Editor.BuildScript.Build \
      -buildTarget StandaloneOSX -customBuildPath build/StandaloneOSX/AlpineResortSimulator
```

The macOS build is forced to Universal (Apple Silicon + Intel) by the build script.

## CI

`.github/workflows/ci.yml` has three jobs:

1. **sim-test** — always runs. `dotnet test sim/AlpineSim.Core.Tests.csproj`, the meta-file
   check, the "Core references no UnityEngine" check, and a compile check of the Unity layer
   against API stubs. This is the gate that must stay green.
2. **unity-test** — `game-ci/unity-test-runner@v4`, EditMode and PlayMode. Needs the secrets
   below; logs a notice and skips otherwise.
3. **build** — `game-ci/unity-builder@v4` for `StandaloneWindows64` and `StandaloneOSX`,
   uploads an artifact per target. Runs on `v*` tags and manual dispatch; skips without secrets.

### CI secrets (Unity licence activation)

Add these repository secrets (Settings > Secrets and variables > Actions):

| Secret | Value |
| --- | --- |
| `UNITY_EMAIL` | The Unity account e-mail. |
| `UNITY_PASSWORD` | Its password. |
| `UNITY_LICENSE` | Contents of a `Unity_v20XX.x.ulf` licence file (Personal or Plus/Pro). |

To obtain `UNITY_LICENSE` for a Personal licence:

1. Run the manual activation once on any machine with the same Unity version:
   `Unity -batchmode -createManualActivationFile -logfile` produces `Unity_v6000.x.alf`.
2. Upload the `.alf` at <https://license.unity3d.com/manual>, choose Personal, download
   `Unity_v6000.x.ulf`.
3. Paste the whole `.ulf` file contents into the `UNITY_LICENSE` secret.

For Plus/Pro licences you can instead set `UNITY_SERIAL` and pass it to the game-ci actions; see
<https://game.ci/docs/github/activation>.

## Tests

`dotnet test sim/AlpineSim.Core.Tests.csproj` runs the whole suite (about ten minutes; the two
thirty-day economy fixtures take most of it). Filter with `--filter FullyQualifiedName~Snow` for a
subsystem. The suite pins the design pillars, not just the code:

| Fixture | What it proves |
| --- | --- |
| `Snow/SnowGridTests` | mass conservation under blade, tiller and skier traffic; compaction curve (corduroy, then ice); PQI never rises under traffic without grooming; hourly PQI publish is cheap; grid survives save/load exactly |
| `Sim/DeterminismTests` | identical hash after 10,000 ticks; different seeds differ; save/load mid-run stays on trajectory; canonical tick order |
| `Foundation/*` | JSON round trips, RNG, calendar, clock consumes ticks not dt, v1 → v4 save migration |
| `Weather/WetBulbTests` | psychrometric wet-bulb against tables, monotonicity, altitude, snowmaking window edges |
| `Data/FleetDataTests`, `Data/LiftDataTests`, `Data/EconomyDataTests` | 58 machines across 10 categories and 5 tiers, no two within 5 % on every key figure; 26 lift types; every enum has data |
| `Vehicles/AttachmentEffectsTests` | a 6.0 m tiller covers 40 % more run per metre than a 4.3 m one; the heavy tiller needs a heavy cat |
| `Fleet/FuelLogisticsTests` | a dry tank halts the machine and raises a service call; the service truck refuels it; an empty depot blocks until the delivery lands; AI machines refuel at the reserve |
| `Lifts/LiftThroughputTests`, `Lifts/WindHoldTests` | every lift type moves its rated capacity through a saturated queue within 5 %; holds come in wind-limit order |
| `Construction/TerrainGatingTests` | T-bar length, fixed-grip span, surface-lift grade; only the 3S and trams cross the valley; runs must descend |
| `Guests/LapRateTests` | starving uphill capacity cuts laps and lengthens queues |
| `Economy/ThirtyDayTests`, `Economy/ActFourTrapTests` | thirty closed books reconcile to the cent; a detachable without added grooming nets less by day 30 |

## Documentation

- `docs/DESIGN.md` — pillars, systems, act structure.
- `docs/ARCHITECTURE.md` — tick order, system boundaries, Core/Unity separation, save format.
- `docs/BUILD_ORDER.md` — milestones M0–M8 with kill criteria and status.
- `docs/TUNING.md` — every anchor with its basis and the knob that changes it.
- `docs/DECISIONS.md` — decisions taken where the brief was silent.
- `CLAUDE.md` — how to work in this repository.
