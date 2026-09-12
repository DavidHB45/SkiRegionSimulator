# Build order

Each milestone is playable and independently verifiable, and lands as its own commit. A
milestone's wiring lives in `*.Mn.cs` partial files (see `CLAUDE.md`) so earlier commits never
reference later systems.

| Milestone | Scope | Kill criterion | Status |
| --- | --- | --- | --- |
| M0 Skeleton | Repo, asmdefs, `/sim` csproj, CI, `Boot.unity`, procedural terrain, free camera, `dotnet test` | Cannot build headlessly | **Done** |
| M1 Snow substrate | Snow grid, deformation, compute + CPU fallback, PQI, debug overlay | 0.5 m grid over the envelope can't hold 60 fps on a mid-range machine | **Done** |
| M2 One vehicle | Tier-2 tracked groomer, blade + tiller, fuel, driving model, snow mutation, cockpit/chase cameras | Grooming a run doesn't feel physically legible | **Done** |
| M3 Smallest loop | Pre-built fixed-grip lift, guests, PQI → satisfaction → revenue → costs → cash | The loop doesn't create a reason to groom | **Done** |
| M4 Construction | Lift building as driven tasks; second lift and run buildable | — | **Done** |
| M5 Weather & snowmaking | Wet-bulb gating, guns, water/power, forecast UI | — | **Done** |
| M6 Fleet, garage, wear | 50 machines, attachments, market, condition, PM vs failure, parts, workshop, licensing, fuel logistics, fleet screen | Any two machines in a category play identically | **Done** |
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

## M1 — Snow substrate (done)

- `SnowGrid`: sparse 32×32-cell chunks over the piste/zone envelope, SoA fields, deflate+base64 in saves.
- `SnowOps`: mass-conserving blade push, tiller re-lay with work hardening, skier scrape/chop, snowfall, melt, refreeze, wind scour.
- `PqiCalculator`: coverage, density band, roughness, freshness → PQI 0..100; published hourly per segment, run and resort.
- `SnowView`: snow map upload by compute shader or CPU fallback; corduroy micro-relief; debug overlay modes.
- Kill criterion addressed: only dirty chunks are uploaded; the hourly weather pass is amortised over the hour.

## M2 — One vehicle (done)

- Driving models for tracked, wheeled, articulated and walk-behind chassis with torque curves, traction by snow density, sinkage, side-slope limits.
- Snow contact: blade (cut, carry, spill), tiller (target density, efficiency, hardening), blower, bucket, spreader.
- Fuel, warm-up and cold start, wear per subsystem, failures with parts; AI operator that grooms lanes, plows zones, hauls and works sites.
- Kill criterion addressed: tests pin the compaction curve (corduroy then ice), mass conservation and the attachment width effect.

## M3 — Smallest loop (done)

- Prebuilt lifts from the scenario; queue model at data throughput; holds; breakdowns; inspections.
- Guest cohorts from seven archetypes: demand → arrival → lifts → runs → satisfaction → reputation → tomorrow's demand.
- Ledger with daily and weekly close, loans, acts; Books panel.
- Kill criterion addressed: the 30-day closed-book test and the PQI monotonicity test show traffic without grooming loses guests and money.

## M4 — Construction (done)

- Staking with terrain gating (length, vertical, span between buildable tower sites, grade, gorge crossing); only 3S and trams cross the valley.
- Stage chains per lift family as driven tasks: survey, corridor, footings, concrete haul, towers (crane class or helicopter), terminals, rope, carriers, barn, commissioning, load test, inspection.
- Run staking and grading; Construction panel and plan preview.

## M5 — Weather & snowmaking (done)

- Climate periods → daily anomalies with autocorrelation → hourly samples; forecast degrades with lead time; lapse rate and wind exposure by elevation.
- Guns with wet-bulb output curves, lances needing air, hydrants and hoses, pump/compressor stations, reservoir, water and energy costs; automatic gun operation.
- Weather and Snowmaking panels.

## M6 — Fleet, garage, wear (done)

- 58 machine classes in 10 categories and 5 tiers; 30+ attachments; market with new, used (hidden defects, inspections), lease and rental listings.
- Workshop tiers, service jobs (PM, repair, rebuild, mount, dismount), parts stock and orders, send-out repairs, garage shelter, wash bay.
- Operators with licences, training, fatigue and accidents; fuel depot with spot and contract deliveries; service truck calls; roads and lots.
- Kill criterion addressed: the category differentiation test rejects any two machines within 5 % on every key figure.

## M7 — Deferred scaffold

Winch cats (anchors, rope tension, pull-assisted climbs), terrain park shaping (feature catalogue, snow budget, wear and rebuild) and avalanche control (paths, hazard rating, closures, control missions). `Assets/Scripts/Core/Scaffold/M7Scaffold.cs` carries the interfaces and JSON schemas (`winch.json`, `park.json`, `avalanche.json`); `M7ScaffoldSystem` ticks as a no-op and throws on use. Kill criterion when built: a winch climb that does not read as tension on the rope (audio, HUD, speed) is not worth shipping.

## M8 — Deferred scaffold

Five-act campaign beats (`campaign.json`), region expansion tiles beyond the 4.2 km² envelope (`regions.json`) and a mod loader (`mod.json` manifests overlaying data by id). `M8Scaffold.cs` holds the interfaces and schemas; `M8ScaffoldSystem` ticks as a no-op in the Campaign slot. Kill criterion when built: expansion that does not change the guest mix or the fleet needs is scope creep.

## Art pipeline (cross-cutting)

Not a milestone: it has no gameplay of its own, no milestone waits on it, and the game stays
fully playable with none of its output present. It is tracked here because it is work with a
status, and because that last sentence is its kill criterion - generated art that the game
cannot run without has replaced a fallback path that was worth keeping.

`tools/assetgen` builds every mesh, texture, icon and sound from the same
`Assets/StreamingAssets/Data/*.json` the simulation reads, into the gitignored
`Assets/Art/Generated/`. `docs/ART_CONTRACT.md` is the interface it is held to and
`docs/ASSET_PIPELINE.md` is how to run and extend it.

| Piece | Where | Status |
| --- | --- | --- |
| Contract and validator | `docs/ART_CONTRACT.md`, `tools/assetgen/lib/validate.py` | **Done** |
| Mesh construction and export | `tools/assetgen/lib/meshkit.py`, `lib/export.py` | **Done** |
| Generators: chassis, attachments, lifts, props | `tools/assetgen/meshes/` | **Done** |
| Generators: trim sheet, material sets, terrain and snow | `tools/assetgen/textures/` | **Done** |
| Generators: icons, HUD glyphs, markers | `tools/assetgen/ui/` | **Done** |
| Generators: engines, machinery, ambience | `tools/assetgen/audio/` | **Done** (engine set is placeholder by design) |
| Build harness, self-test, CI | `Makefile`, `tools/assetgen/selftest.py`, `.github/workflows/assets.yml` | **Done** |
| Runtime resolution: three-tier lookup, articulation, livery, wear | `Assets/Scripts/Unity/Art/` | In progress |
