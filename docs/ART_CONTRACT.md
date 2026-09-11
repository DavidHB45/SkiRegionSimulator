# Art contract

Everything under `Assets/Art/Generated/` is produced by `tools/assetgen` from the same
JSON the simulation reads. This file is the interface between that pipeline and the
game: the names gameplay binds to, the axes and pivots it assumes, and the channels the
shaders sample. Both sides are checked against it — `tools/assetgen/lib/validate.py`
fails the build on a violation, and `ArticulationBinder` throws at startup on a missing
transform rather than silently animating nothing.

If you change a name here, change it in both places in the same commit.

---

## 1. Axes, units, pivots

| | |
|---|---|
| Units | metres, always. A 12 500 kg groomer is ~6 m long, not 600. |
| Axes (Unity) | +X right, +Y up, +Z forward |
| Axes (authoring) | generators write **Unity space**; `meshkit` stores it in Blender's Z-up frame with Unity forward on Blender −Y |
| Export | FBX, `axis_forward='-Z'`, `axis_up='Y'`, `global_scale=1`, `use_file_scale` on |
| Result | the FBX holds **literal Unity coordinates**. Nothing is rotated or rescaled on import. |
| Root pivot | ground contact centre: X centred on the machine's centreline, Y at the contact patch (`y_min ≈ 0`), Z at the chassis centre |
| Root rotation | identity. A machine's rest pose faces +Z. |

`tools/assetgen/selftest.py` exports a landmark model, re-reads the FBX with Blender's
importer in manual-orientation mode, and asserts the landmarks land where Unity will
read them. That test is the guarantee behind the table above.

## 2. Hierarchy and LODs

```
<id>                     root, identity transform, pivot at ground contact centre
  <id>_LOD0              the articulated hierarchy — everything gameplay binds to
    body                 static chassis geometry
    track_L, track_R     named articulation transforms (see §3)
    blade_lift
      blade_angle_L …
  <id>_LOD1              merged static proxy, ~40 % of LOD0 triangles
  <id>_LOD2              merged static proxy, ~15 %
  <id>_col               convex hull collider, ≤ 250 polygons (Unity refuses 256+)
  cab_col, att_col       box colliders, when the generator declares them
  mount_Front, …         sockets (empties)
```

The `_LOD0/_LOD1/_LOD2` suffixes are what Unity's model importer looks for when it
builds an LOD Group. `ModelRegistry` does not rely on that: if the import produced no
`LODGroup` it builds one in code from the three children, at screen heights
`0.35 / 0.12 / 0.02`. Either way LOD1 and LOD2 are **static** — nothing articulates at
60 m, so they are one merged mesh each, which is both cheaper to draw and cheaper to
cull.

**Triangle budgets** (LOD0; the build fails above the ceiling and warns below the floor):

| Class | Floor | Ceiling |
|---|---|---|
| `machine_hero` — groomers, loaders, trucks, big construction | 8 000 | 15 000 |
| `machine_small` — sleds, UTVs, guns, skids, walk-behinds | 1 200 | 8 000 |
| `attachment` | 600 | 6 000 |
| `lift_tower` | 900 | 6 000 |
| `lift_terminal` | 2 500 | 12 000 |
| `lift_carrier` | 700 | 9 000 |
| `prop` | 200 | 6 000 |
| `tree`, `rock` | 120 | 1 500 |

## 3. Articulation transform names

Gameplay drives these by name. A generator that emits a family **must** publish that
family's required transforms; anything marked optional is bound if present and ignored
if not. Names are unique within a model and are matched exactly, case included.

### Machines — chassis

| Name | Required for | Driven by |
|---|---|---|
| `track_L`, `track_R` | every `Tracked` chassis | belt scroll rate from ground speed |
| `wheel_FL FR RL RR`, `wheel_ML MR` | wheeled chassis with that axle | roll angle from wheel speed |
| `steer_FL`, `steer_FR` | wheeled chassis with steered front axle | steering angle |
| `pivot_center` | every `Artic` chassis | articulation angle between front and rear frames |
| `cab` | optional | tilt cabs, suspended cabs |
| `boom_01 … boom_0n` | excavators, cranes, telehandlers | boom joint angles, base first |
| `bucket` | excavators, loaders | curl angle |
| `turret` | excavators, crawler cranes | slew angle |
| `exhaust` | optional | smoke emitter anchor |

### Machines — implements

| Name | Meaning |
|---|---|
| `blade_lift` | pivot at the lift cylinder's frame anchor; rotates about X to raise/lower |
| `blade_angle_L`, `blade_angle_R` | the two outer wings of a 12-way blade; rotate about Y |
| `blade_tilt` | roll about Z for the whole mouldboard |
| `tiller_arm` | rear tiller lift arm; rotates about X |
| `tiller_rotor` | the rotor itself; spins about X while tilling |
| `finisher` | the trailing comb; follows `tiller_arm` |
| `blower_impeller` | spins about Z |
| `blower_chute` | yaws about Y |
| `spreader_disc` | spins about Y |
| `winch_drum` | spins about X; `winch_boom` yaws about Y |
| `fork_L`, `fork_R` | slide along X |

### Lifts

| Name | Meaning |
|---|---|
| `bullwheel` | required on every terminal; spins about Y |
| `sheave_01 … sheave_0n` | required on every tower; spin about X, numbered uphill-first |
| `crossarm` | optional; the sheave train assembly |
| `cabin_hanger` | carrier hanger arm; swings about Z in wind |
| `grip_arm` | detachable grip; opens about X in the terminal |
| `door_L`, `door_R` | gondola cabin doors; slide along X |
| `bar` | chair restraint bar; rotates about X |
| `carpet_belt` | magic carpet belt; scrolls along Z |

### Snowmaking

| Name | Meaning |
|---|---|
| `fan_rotor` | spins about Z |
| `gun_yaw` | yaws about Y; `gun_pitch` pitches about X |
| `oscillator` | the oscillating head, when the machine has one |

## 4. Sockets

Empties, not meshes, named for what mounts there. `ModelRegistry` parents the
attachment model to the matching socket.

| Name | Meaning |
|---|---|
| `mount_Front`, `mount_Rear`, `mount_Mid`, `mount_Roof`, `mount_Tow` | the five `SlotPosition` values in `vehicles.json` |
| `light_L`, `light_R`, `light_work_*` | headlight and work-light anchors |
| `seat_01 … seat_0n` | lift carrier seats |
| `hitch` | tow point |

## 5. Materials

**Three slots per model, in this order, and no more.** More than three means more than
three draw calls per machine, which defeats the shared trim sheet.

| Slot | Name | Content |
|---|---|---|
| 0 | `body` | painted bodywork — tinted per machine by `LiveryTint` from `Visual.ColorHex` |
| 1 | `metal` | dark metal, tracks, rubber, frames, unpainted steel |
| 2 | `glass` | glazing and bubble canopies |

Livery colour is a **tint on a shared texture set**, never a separate texture: a whole
category shares one material instance and differs only in `_LiveryColor`.

## 6. Texture channel packing

Suffix decides the importer settings (`tools/assetgen/lib/unitymeta.py`), so the suffix
is part of the contract.

| Suffix | Shader property | Channels | Colour space |
|---|---|---|---|
| `_albedo.png` | `_MainTex` | RGB albedo | sRGB |
| `_normal.png` | `_BumpMap` | tangent-space normal | linear, normal-map type |
| `_orm.png` | `_ORM` | R = ambient occlusion, G = roughness, B = metallic | linear |
| `_wear.png` | `_WearMask` | R = edge wear, G = rust, B = salt/dirt, A = paint chipping | linear |
| `_height.png` | `_ParallaxMap` | height field | linear |
| `_detail.png` | `_DetailMap` | detail normal / breakup | linear |

Normals are derived from a real height field by gradient, never faked.

**Wear is driven by the sim.** `ConditionVisuals` reads the machine's condition
percentage and feeds `_WearAmount` (0 = factory fresh, 1 = worn out). The four masks
blend in at different thresholds so a machine at 40 % condition visibly looks it. That
link — a sim variable moving an art parameter — is the whole point of generating art
from the same data.

Sizes: 2048² for hero machines, 1024² for props, 512² for small parts, 2048² for the
shared industrial trim sheet.

## 7. Trim sheet

One 2048² sheet carries panel lines, bolt rows, grip plate, vents, hydraulic line, weld
beads, tread plate, rubber track and plain steel/paint fields. Cell coordinates live in
`tools/assetgen/lib/materials.py:TRIM_CELLS` and are painted by
`tools/assetgen/textures/trimsheets.py`. Everything UVs to it.

## 8. Icons and audio

| Kind | Path | Naming |
|---|---|---|
| Machine icon | `Icons/machine_<vehicleId>@<1,2,4>x.png` | one per class in `vehicles.json` |
| Lift icon | `Icons/lift_<liftId>@Nx.png` | one per type in `lifts.json` |
| Attachment icon | `Icons/attachment_<attachmentId>@Nx.png` | one per record in `attachments.json` |
| HUD glyph | `Icons/hud_<name>@Nx.png` | fuel, wear, condition, pqi, wind, temperature, wetbulb, queue, alert … |
| Map marker | `Icons/marker_<name>@Nx.png` | |
| Weather symbol | `Icons/weather_<name>@Nx.png` | |
| UI panel | `Icons/ui_panel_<state>.png` (9-slice), `Icons/ui_button_<state>.png` | |
| Engine loop | `Audio/engine_<class>_<band>.wav` | bands: `idle`, `low`, `mid`, `high` |
| Machinery | `Audio/machinery_<name>.wav` | |
| Ambience | `Audio/ambience_<name>.wav` | |
| UI | `Audio/ui_<name>.wav` | |

All audio is 48 kHz, 16-bit, mono, and seamlessly loopable.

> **The generated engine set is placeholder.** It is a harmonic stack with a
> cylinder-count firing interval and turbo whine — correct in structure, recognisably
> synthetic in timbre. It exists so the game is never silent and so the crossfade rig is
> exercised end to end. Recorded libraries replace it later by dropping files at the
> same paths; nothing in code changes.

## 9. ModelRegistry override format

Every machine, attachment and lift component resolves its visual through three tiers,
in order:

1. **Authored** — `"modelOverride": "Art/Authored/Groomers/flagship"` on the record in
   `vehicles.json` / `attachments.json` / `lifts.json`. A `Resources` path to a hand-made
   or purchased model. Takes precedence over everything.
2. **Generated** — `AlpineSim/Models/Machines/<id>` and friends, produced by this pipeline.
3. **Primitive** — the procedural `VehicleMeshBuilder` / `LiftView` geometry the game
   shipped with.

Resolution for every visual is logged once at startup, so it is always visible which
tier a machine came from. **The game is fully playable with zero generated assets
present**: delete `Assets/Art/Generated/` and every model falls through to tier 3.

For lifts the override may be a folder; the registry looks for `tower`, `terminal_drive`,
`terminal_return` and `carrier` inside it.

## 10. Validation rules

`tools/assetgen/lib/validate.py` fails the build on:

- LOD0 over its triangle ceiling, or an LOD heavier than the one above it
- a model that is not in metres (under 5 cm or over 400 m across)
- a pivot more than 25 cm off the ground
- non-manifold edges or zero-area faces
- a convex collider over 250 polygons
- a missing required articulation transform
- a texture that is not a power of two, or the wrong size for its class
- audio that clips, is silent, or has a discontinuous loop seam

and warns (build continues, reported in `Assets/Art/Generated/validation.json`) on
under-budget meshes, collapsed UV unwraps, off-centre pivots and loop-seam clicks.
