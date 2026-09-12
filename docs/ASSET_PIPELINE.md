# Asset pipeline

Everything under `Assets/Art/Generated/` is build output. `tools/assetgen` produces it from
`Assets/StreamingAssets/Data/*.json` - the same files the simulation reads - so a machine's
model is a consequence of its record, not a separate authoring job. Change `MassKg` or
`TrackWidthM` in `vehicles.json` and the model changes on the next build.

`docs/ART_CONTRACT.md` is the interface: axes, pivots, LOD names, articulation transform
names, material slots, texture channels and triangle budgets. It is authoritative and this
document does not repeat it. What follows is how to run the pipeline, how to extend it, and
what to expect from it.

## Running it

The toolchain is Python. Blender is used as a library (`bpy`), not as an application, and
nothing here needs Unity or a Unity licence.

```
python3 -m pip install -r tools/assetgen/requirements.txt   # or: make assets-deps
make assets
```

**Python 3.11 is required**, not a preference: `bpy==4.2.0` publishes cp311 wheels and no
others. The wheel is around 500 MB, so the first install is slow and every later one is a
cache hit.

| Command | What it does |
| --- | --- |
| `make assets` | Everything: textures, meshes, icons, audio |
| `make assets-textures` | The trim sheet and the material sets |
| `make assets-meshes` | Machines, attachments, lifts, props |
| `make assets-icons` | UI icons, HUD glyphs, map markers, weather symbols |
| `make assets-audio` | Engine, machinery and ambience loops |
| `make assets-selftest` | The pipeline's own guarantees (below) |
| `make assets-validate` | Self-test, clean, full rebuild, warning summary - the CI gate |
| `make assets-clean` | Delete the output tree |
| `make help` | Every target |

`build_all.py` takes the same work in smaller pieces, which is what you want while iterating
on one generator:

```
python3 tools/assetgen/build_all.py --only meshes
python3 tools/assetgen/build_all.py --only meshes --machines groomer_heavy_winch
python3 tools/assetgen/build_all.py --clean
```

## What comes out

```
Assets/Art/Generated/
  manifest.json                 every asset with triangle counts, bounds, nodes, sockets
  validation.json               warnings the build tolerated
  Resources/AlpineSim/
    Models/Machines/<id>.fbx    one per record in vehicles.json
    Models/Attachments/<id>.fbx one per record in attachments.json
    Models/Lifts/<id>/          tower, tower_low, tower_high, terminal_drive,
                                terminal_return, carrier, barn
    Models/Props/               hydrants, pump houses, fencing, signage, trees, rocks
    Textures/                   the trim sheet and the material sets
    Icons/                      machine, lift, attachment, HUD, marker, weather, UI
    Audio/                      engine bands, machinery loops, ambience beds
```

The tree is gitignored. It is also optional: with none of it present every visual falls
through to the procedural primitives the game shipped with, and the game is fully playable.
See `Assets/Art/README.md` for how the generated tree sits next to a hand-authored one.

The pipeline writes its own `.meta` files as it goes (`lib/unitymeta.py`), with GUIDs from
the same derivation `tools/gen_meta.py` uses, which is why `gen_meta.py` skips this tree:
walking it would make `--check` pass or fail depending on whether someone had run a build.

## How long a build takes

Measured on one modern x86-64 core; the pipeline is single-threaded, so a build is about as
fast as one core is:

| Stage | Time | Why |
| --- | --- | --- |
| textures | 2 min | noise synthesis and normal derivation at 2048 squared |
| meshes | 52 s | every model constructed, decimated twice and convex-hulled |
| audio | 14 s | additive synthesis plus filtering |
| icons | 6 s | SVG rasterised at three scales |
| **full build** | **3 min 10 s** | 1017 assets, about 240 MB |

In CI the build itself is the same; the variable is the `bpy` download, which is minutes on a
cold pip cache and seconds on a warm one. `.github/workflows/assets.yml` caches on the pinned
requirements file for that reason.

## Determinism

Two builds of one commit produce byte-identical files. That is a property worth keeping,
because it is what lets anyone compare a published art pack against a rebuild and believe the
answer, and it is easy to lose:

- every random choice comes from `numpy.random.default_rng(datasrc.seed_for(...))`, seeded
  from `config.MASTER_SEED` plus an identifying string. No generator calls an unseeded RNG.
- records are iterated in file order, never over an unordered dict.
- `lib/export.py` overwrites the FBX creation timestamp, which Blender would otherwise stamp
  with the wall clock.

The self-test builds one model twice and compares the file hashes, so a regression here fails
the build rather than being discovered as churn in a diff.

## Adding a machine

A machine is a JSON record. In the ordinary case that is the entire job.

1. **Add the record to `Assets/StreamingAssets/Data/vehicles.json`.** The simulation needs it
   anyway: `Id`, `DisplayName`, `Category`, `Tier`, `ChassisType`, `MassKg`, `EnginePowerKw`,
   `Seats`, `AttachmentSlots[]` and the rest. The generator reads those same fields - it has
   no table of its own, and there is nothing per-machine to add anywhere in `tools/assetgen`.

2. **Fill in the `Visual` block.** It is the mesh recipe, and it is what the generator leans
   on hardest:

   ```json
   "Visual": {
     "Silhouette": "groomer",
     "BodyL": 7.0, "BodyW": 4.6, "BodyH": 1.8,
     "CabL": 2.0, "CabW": 2.3, "CabH": 1.4, "CabOffset": 1.2,
     "TrackH": 0.95, "WheelRadiusM": 0.5, "Axles": 0,
     "BoomLengthM": 0,
     "ColorHex": "#c0392b", "AccentHex": "#1c2833"
   }
   ```

   `lib/datasrc.visual()` supplies the same defaults `MeshRecipe.cs` uses, so a partial block
   is legal; a wrong one is worse than an absent one.

3. **Build just that machine and read the numbers back.**

   ```
   python3 tools/assetgen/build_all.py --only meshes --machines <id>
   python3 -c "import json; print([a for a in json.load(open('Assets/Art/Generated/manifest.json'))['assets'] if a['id']=='<id>'])"
   ```

   Check the triangle counts against the budget and `validation.json` for warnings. An
   under-budget warning on a hero machine usually means the record is missing something the
   generator needed and it quietly built a smaller machine.

4. **Only if the silhouette is genuinely new**, add a recipe to the module that owns the
   family. `build_all.module_for` routes on the chassis first and the silhouette second:

   | Record says | Generator |
   | --- | --- |
   | `ChassisType: "Artic"` | `meshes/chassis_artic.py` |
   | `Visual.Silhouette` in `snowmaking.SILHOUETTES`, or `ChassisType` `Stationary` / `Towed` | `meshes/snowmaking.py` |
   | `ChassisType` `Tracked` / `WalkBehind` | `meshes/chassis_tracked.py` |
   | anything else | `meshes/chassis_wheeled.py` |

   A new silhouette is a branch inside one of those modules and a new entry in its
   `SILHOUETTES` set. It is never a new class per machine: proportions come from the record,
   and a generator that needs a constant for one machine has stopped reading the data.

Icons and engine audio need no separate step. `ui/icons.py` emits `machine_<id>` for every
record in `vehicles.json`, and `audio/engines.py` emits a band set per engine class.

Attachments (`attachments.json`) and lifts (`lifts.json`) work the same way, through
`meshes/attachments.py` and the three lift modules. A lift record produces a folder, because
one line needs three tower height classes and two different terminals; `barn` appears only
when the type's `CabinBarnCapex` is above zero.

## Tuning a generator

The first question is always whether the number in JSON is wrong rather than the code: a
groomer that looks too tall is usually a `BodyH` that is too tall. When it really is the
generator:

- **Silhouette and proportion** live in the family module, keyed off the record. Read
  `Assets/Scripts/Unity/Vehicles/VehicleMeshBuilder.cs` for what each silhouette is meant to
  read as - it is the primitive fallback the generated art replaces, and it defines the
  shapes players already associate with each machine.
- **Density dials** are in `tools/assetgen/config.py`: `TRI_BUDGETS`, `BEVEL_WIDTH_M`,
  `BEVEL_SEGMENTS`, `LOD_RATIOS`, `MIN_FEATURE_M`. Cylinder and lathe segment counts are
  per-call and should scale with the size of the machine, not be a constant.
- **Form** comes from `m.prism` and `m.loft`, not from stacking boxes. A cab shell is a
  lofted profile; a blade mouldboard is an extruded curve. A stack of boxes reads as
  programmer art at any triangle count.
- **Anything random** goes through `model.rng`, which is already seeded per record.

After a change, run `make assets-selftest` and rebuild the affected family. The self-test is
seconds; it catches the mistakes a screenshot does not.

## Triangle budgets

`config.TRI_BUDGETS` holds them, one band per class, and `docs/ART_CONTRACT.md` section 2
publishes the same table. Over the ceiling fails the build. Under the floor warns, on the
theory that a hero machine that came out at 900 triangles did not get cheaper, it got broken.

A model picks its band with the `budget_key` it passes to `mk.Model`. For machines,
`datasrc.is_hero()` decides: anything driven or parked next to, from 1500 kg up, is
`machine_hero`; sleds, guns, walk-behinds and skids are `machine_small`.

## The self-test

`python3 tools/assetgen/selftest.py` (or `make assets-selftest`) checks the things that are
invisible until they have shipped:

| Check | Guarantee |
| --- | --- |
| axis contract | an exported FBX holds literal Unity coordinates - landmarks on +X, +Y and +Z are re-imported verbatim and must land where they were authored |
| pivot | a model's lowest vertex is at `y = 0`, so it stands on the ground it is placed on |
| LOD ordering | LOD1 and LOD2 are strictly lighter than LOD0 and neither is empty |
| collider cap | a convex hull stays at or under 250 polygons, however dense the input |
| determinism | one model built twice is identical, down to the file hash |
| budget enforcement | a deliberately over-budget model is rejected, and is not written |

The axis check is the one worth understanding. Two conversions cancel to produce the promise
in `docs/ART_CONTRACT.md` section 1 - `meshkit` stores Unity space in Blender's Z-up frame,
the FBX export maps it back - and a sign error in either one yields a file that still opens,
still looks like a machine, and puts the blade on the wrong side. So the test re-reads the
exported file with Blender's importer in manual-orientation mode, which takes the file
verbatim instead of converting it, and asserts the landmarks are where they were authored.

## How the game resolves a visual

Three tiers, in order, described fully in `docs/ART_CONTRACT.md` section 9: an authored
`modelOverride` on the record, then the generated asset under
`AlpineSim/Models/...`, then the procedural primitive (`VehicleMeshBuilder`, `LiftView`).
Each resolution is logged once at startup, so which tier a machine came from is always
visible. Deleting `Assets/Art/Generated/` is supported and drops everything to tier 3.

## What is final and what is placeholder

Models, textures, icons, machinery loops and ambience beds are finished work: they are what
the game ships with unless someone commissions hand-made replacements, and a replacement
drops in through tier 1 without a code change.

**The engine audio is placeholder by design.** It is a harmonic stack with a cylinder-count
firing interval and turbo whine - structurally correct, audibly synthetic. It exists so the
game is never silent and so the crossfade rig between `idle`, `low`, `mid` and `high` is
exercised end to end. A recorded library replaces it by dropping files at the same paths;
nothing in code changes. `docs/ART_CONTRACT.md` section 8 lists those paths.

## CI

| Workflow | Job | When |
| --- | --- | --- |
| `ci.yml` | `sim-test` | always; the gate that must stay green |
| `assets.yml` | `assets-build` | push, `v*` tags, manual dispatch; uploads the art pack, attaches it to a tagged release |
| `assets.yml` | `assets-validate` | pull requests touching `Data/**`, `tools/assetgen/**` or `ART_CONTRACT.md` |

`assets-validate` is why the paths filter exists: a data edit that breaks a generator should
fail on the pull request that makes it, not on somebody else's branch a week later. Neither
asset job needs a Unity licence.

## When the build fails

A contract violation stops the build and names the rule:

```
CONTRACT VIOLATION: groomer_heavy_winch: LOD0 is 18422 triangles, budget 'machine_hero' allows 15000
docs/ART_CONTRACT.md describes the rule that failed.
```

`lib/validate.py` holds the rules and section 10 of the contract lists them. The common ones:

- **over budget** - lower a segment count or a bevel, do not raise the ceiling.
- **not in metres** - a generator multiplied by something it should not have.
- **pivot off the ground** - geometry moved but the model's ground contact did not.
- **missing articulation transform** - the family requires a named node the generator did not
  publish. `validate.REQUIRED_NODES` lists what each family owes.
- **non-manifold edges** - usually two mirrored halves welded through each other.

Warnings do not stop the build; they land in `validation.json` and are worth reading after a
data change.
