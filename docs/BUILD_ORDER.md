# Build order

Each milestone is playable and independently verifiable, and lands as its own commit. A
milestone's wiring lives in `*.Mn.cs` partial files (see `CLAUDE.md`) so earlier commits never
reference later systems.

| Milestone | Scope | Kill criterion | Status |
| --- | --- | --- | --- |
| M0 Skeleton | Repo, asmdefs, `/sim` csproj, CI, `Boot.unity`, procedural terrain, free camera, `dotnet test` | Cannot build headlessly | **Done** |
| M1 Snow substrate | Snow grid, deformation, compute + CPU fallback, PQI, debug overlay | 0.5 m grid over the envelope can't hold 60 fps on a mid-range machine | Pending |
| M2 One vehicle | Tier-2 tracked groomer, blade + tiller, fuel, driving model, snow mutation, cockpit/chase cameras | Grooming a run doesn't feel physically legible | Pending |
| M3 Smallest loop | Pre-built fixed-grip lift, guests, PQI → satisfaction → revenue → costs → cash | The loop doesn't create a reason to groom | Pending |
| M4 Construction | Lift building as driven tasks; second lift and run buildable | — | Pending |
| M5 Weather & snowmaking | Wet-bulb gating, guns, water/power, forecast UI | — | Pending |
| M6 Fleet, garage, wear | 50 machines, attachments, market, condition, PM vs failure, parts, workshop, licensing, fuel logistics, fleet screen | Any two machines in a category play identically | Pending |
| M7 Deferred scaffold | Winch cats, terrain park, avalanche control: interfaces + schemas only | — | Scaffold |
| M8 Deferred scaffold | Five-act tuning, region expansion beyond 4.2 km², mod loading: interfaces + schemas only | — | Scaffold |

## M0 — Skeleton (done)

- `AlpineSim.Core` (no engine references), `AlpineSim.Unity`, `AlpineSim.Tests`, `AlpineSim.Editor` asmdefs.
- `sim/AlpineSim.Core.csproj` + `sim/AlpineSim.Core.Tests.csproj` glob the same sources; `dotnet test` green.
- `sim/AlpineSim.Unity.CompileCheck.csproj` compiles the Unity layer against `sim/UnityStubs`.
- `Boot.unity` + `Bootstrap`: loads `StreamingAssets/Data`, builds `Simulation`, chunked LOD terrain, sun/moon lighting, free camera, code-built Input System maps, uGUI HUD.
- Deterministic terrain from (scenario, seed): ridge, bowls, gorge, engineered flats.
- Save/load with schema migrations; determinism hash; CI with the three jobs.
- Kill criterion addressed: everything builds and tests with `dotnet` alone; Unity jobs are optional.
