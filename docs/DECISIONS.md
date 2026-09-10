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

## D-011 Fifty-eight machine classes across ten categories
The brief asks for 50 machines and enumerates more; the fleet table carries 58 records so every
category has a full tier ladder (groomers T1–T5 incl. winch, hybrid and electric cats). Adding a
machine remains a JSON record plus a mesh recipe; the integrity test only asserts "at least 50,
all ten categories, all five tiers".

## D-012 The default scenario starts in Act II
Silberhorn starts with a fixed-grip quad, a T-bar, three runs and a tired fleet: the smallest
loop that makes grooming matter. Act I (the village T-bar) is the `act1` scenario; a new
player who wants the full ladder starts there. Act gates read `economy.json` acts only.

## D-013 Runs built at runtime are not terrain-smoothed
Scenario pistes get deterministic corridor smoothing at terrain generation (D-006). A run staked
during play keeps the raw terrain under it; grading it is the `GradeRun` construction stage, which
only flags the piste groomable. Re-smoothing the derived heightmap mid-game would either change
the saved terrain (breaking D-006) or require saving it. Revisit in M7 if park shaping needs it.

## D-014 Seed-derived streams for weather
Weather days and forecasts draw from `SimContext.SeededRng(salt)` (seed × golden-ratio mix ×
salt) rather than the world RNG. A save/load therefore cannot re-roll tomorrow's weather, and
systems that consume randomness in a different order after a load still see the same sky.
The world RNG remains the single stream for everything that happens inside the day.

## D-015 Wet-bulb by psychrometric equation, not the Stull regression
Snowmaking hangs on the wet-bulb, so it is solved from es(Tw) − A·P·(T − Tw) = e (Magnus
saturation curve, ventilated-bulb constant 0.000662, ICAO pressure for the elevation). The Stull
(2011) regression is 0.5–1 °C off in the −2…−8 °C window that decides whether guns run, and it
crosses the dry-bulb below about −20 °C. Altitude lowers the wet-bulb by a few tenths, which is
real and free.

## D-016 Lift throughput is data, carriers are derived
`CapacityPph` in `lifts.json` is the throughput the queue model uses directly (options and
singles line scale it). Carrier count and spacing are derived from it and the line speed for the
visuals and the capex, not the other way round, so a designer tunes the one number resorts quote.

## D-017 Groom jobs are posted at close, completed by coverage
The task system posts one grooming job per open groomable run whose PQI is under
`tasks.groomJobPqiBelow` when the resort closes. A job completes when 97 % of the run's cells
carry a groom stamp newer than the job (player or AI alike), so hand-groomed runs count and a
cat that quits half-way leaves the job open on the board.

## D-018 Terrain picking without colliders
Staking and placement pick against the simulation heightfield by marching the camera ray and
bisecting the crossing (`TerrainPicker`). Terrain chunks carry no colliders: no physics setup, no
drift between what the sim thinks the ground is and what the mouse hits.

## D-019 Instanced markers for guests and carriers
Guests (cohorts) and lift carriers are drawn with `Graphics.DrawMeshInstanced` in batches of
1023 through the `AlpineSim/Instanced` shader. Thousands of GameObjects would dominate frame
time at 60x; a marker per cohort costs one matrix.

## D-020 Scaffold systems tick as no-ops
M7 and M8 register real `ISimSystem`s in their tick slots ("Scaffold", "Campaign") whose `Tick`
does nothing and whose operations throw `NotImplementedException`. The tick order and the save
shape are therefore final now; the schemas in `winch.json`, `park.json`, `avalanche.json`,
`campaign.json`, `regions.json` are loaded if present so content can be authored ahead of code.

## D-021 TUNING.md is generated
`tools/gen_tuning_doc.py` renders `tuning.json` into `docs/TUNING.md`; CI checks it is current.
A hand-maintained table of 230+ anchors would drift within a milestone.

## D-022 Tractive effort and traction loss are anchors, not constants
The driving model capped drive force at a hard-coded 0.36 g, which made any grade over about
17° unclimbable and hid behind the traction curve. The cap is now `vehicles.maxTractiveEffortG`
(0.75 g, the stall thrust of a hydrostatic snowcat) and traction loss follows track sinkage
(`vehicles.sinkageTractionLoss`) instead of the ground-pressure ratio, so packed snow keeps the
full density coefficient. Groomers now climb about 30° unassisted, which matches practice; winch
cats (M7) will extend that.

## D-023 Tilled and plowed areas are geometric
Work accounting uses width × distance rather than counting swept cells: cell counting double
counted rows at tick boundaries and made a 6.0 m tiller look 60 % wider than a 4.3 m one. The
snow mutation still happens per cell; only the bookkeeping changed.

## D-024 Roads and cat tracks are graded, not just benched
Corridor smoothing removed cross-slope but kept the natural profile, so an access road across a
rib carried a 31° pitch that no wheeled machine and only a winch cat could take. Corridors now
carry `MaxGradeDeg` (roads 9°, cat tracks 13°, pistes by colour 14/24/32/42°) and the generator
clamps the centreline rise between vertices, forward and backward, before stamping: cut into the
rib, fill the gully, the way a road is built. Vertices that tie into the base flat or a run are
pinned so the road meets the surface it serves, and a corridor fades out past its end vertices
instead of stamping the end height into the ground around a terminal (which had built a step at
every run bottom). Each cell is stamped once, by the segment it is nearest to: stamping every
segment over its own box had flattened the last metres before each interior vertex and left a
27° step after it on a 20° run, which is where every AI groomer was getting stuck.

## D-025 The route graph is grade aware
`RouteGraph` links road, cat-track and run nodes within `vehicles.routeLinkRadiusM` and sets an
edge cost of length × (1 + steepFactor × excess) once the grade sampled along the edge over a
machine-length baseline exceeds `vehicles.routeMaxGradeDeg`; edges over the limit by a wide margin
are dropped. A dispatched machine therefore takes the long way around a rib rather than the
straight line up it, and the same sampling (`TerrainData.GradeAlongDeg` with a baseline) feeds
the driving model and the AI's refusal check so a 1 m ripple never reads as a wall.

## D-026 A night foreman dispatches idle machines
`TaskSystem.AutoDispatch` (`tasks.autoDispatch`) walks the open board each minute in priority
order and sends the nearest idle, fuelled, unassigned AI machine that can do the job, staffing it
from the roster with the right licence. Without it the resort could only be run by hand, which
made every thirty-day balance test a test of the test author. The player can turn it off; a
machine the player is driving is never taken.

## D-027 Satisfaction is a moving average of experiences
Guest satisfaction blends each lap's experience score into the running value
(`guests.satBlend`) around a neutral point (`guests.satNeutral`) instead of accumulating a
signed sum. A sum could not distinguish a poor day from a long one; the moving average means
the last few laps dominate what a guest tells the reputation model on leaving. PQI carries the
largest weight (`guests.satPqiWeight`) because snow quality is the thing the player controls.

## D-028 Corridors follow the ground within an earthwork budget
Grading a run to its class limit had been cutting forty-metre trenches into a hillside steeper
than the class, with cliffs for banks. Each corridor now carries an earthwork budget
(`PisteEarthworkM` 8, `TrackEarthworkM` 8, `RoadEarthworkM` 12 in the scenario's terrain block):
the profile is clamped to the grade limit and then back to within the budget of the natural
ground, alternately, so it is smoothed as far as that much cut and fill allows and then follows
the mountain. Polylines are resampled to 20 m before grading so the profile can follow the ground
between authored vertices; the inside of a bend blends the two cross-sections instead of
choosing one (which had left a step along the bisector); and every bank, on corridors and on the
base pad alike, is bounded at `CorridorBankDeg` (26°) so a machine can always drive off the edge.
The base pad meets the hill as an engineered platform does, a cut face or fill bank at the bank
angle rather than an 80 m smoothstep, and keeps a third of the valley floor's gradient so it is
a village, not a billiard table. Scenario terrain noise was halved to match: a beginner run on a
mountain with 35 m bumps every 300 m is a contradiction.

## D-029 Tiller and blade effects are per traverse, not per tick
A cell sits under the tiller for several ticks as the machine crosses it, and each tick had
applied a full pass: at walking pace a cat work-hardened its own tracks into ice within seconds
and lost traction on them, and a lowered blade stripped a run to the ground in two passes. Both
now apply the fraction of a pass the machine actually covered that tick (`moved / cellSize`), so
one traverse is one pass whatever the speed or tick rate. The AI floats the blade on its
downhill lanes (`vehicles.aiGroomBladeFloat`) instead of dropping it, and holds its working
speed by easing the throttle as it reaches it rather than capping the throttle, which had
stalled the tiller on the first real pitch.

## D-030 A groomer that cannot make a pitch skips the lane, not the run
On a stuck give-up or a slope refusal mid-lane the AI marks the lane skipped and continues on
the next lane from the height it reached, so the part of the run below the pitch still gets
groomed and the part above waits for a winch cat (M7). Abandoning the whole run had left a
Blue with one steep pitch ungroomed end to end. Stuck detection now looks for a machine that
neither moves nor turns, so a wheeled machine that cannot turn on a bank is stuck while a tracked
one pivoting in place is not; the slope refusal honours the machine's own gradeability as well
as the operator's rating.

## D-031 A dry depot does not park the machine
An AI operator who reaches the depot on reserve and finds it dry carries on with the job on what
is in the tank (`vehicles.aiDryDepotRetrySeconds` before it tries again) and eventually strands
where it is, which raises the service call. Parking with the engine off would hide the problem:
no service call, no fuel order, a job that silently never happens. After any depot stop the AI
resumes the job it left (through the board when it holds a task, else from its own record), so a
refuel run is a detour, not the end of the shift.

## D-032 Plow jobs are posted on snow depth; the foreman prefers the right machine class
A road's clearance score carries an ice penalty that only salt removes, so posting plow jobs
against it had a pickup plowing the same clear road all day. Plow, clear-lot and blow-out jobs
now trigger on the depth-only `SnowScore`; the salt job is the one that answers ice. The plowing
AI angles the blade away from the zone's centreline and works lane strips from the centre
outward so every windrow lands on ground still to be plowed, never back on a cleared strip. The
night foreman gives a job to the nearest machine of the class built for it (groomers groom,
trucks and light machines plow, blowers blow) and falls back to any capable machine only when
none of those is free; before, the cat parked nearest the garage plowed the lots while the runs
waited. A blocked auto job reopens after `tasks.blockRetryMinutes`, and a machine stuck on the
way home hands its job back rather than holding it while parked.

## D-033 The AI drives like an operator, not like a script
Three things the thirty-day probes showed. A pickup arrived at a plow lane at 60 km/h, swung
ten metres wide and plowed the meadow beside the road while the road stayed buried: the AI now
brakes for the end of a route and for sharp corners from a braking-distance speed limit and never
exceeds `vehicles.aiTransitMaxKmh` between jobs. A machine refused every pitch above its rating
in both directions, so one parked on a slope could never leave: only climbs are refused, and a
route search leaves out graph edges above the machine's own gradeability, so a pickup is never
sent up a link the graph allows a snowcat. A pass with the blade down dumped its load at the lane
end, on the road being cleared: the load now drops beyond the trailing end of an angled blade,
plow lanes overrun the end of a road so the blade clears it before lifting, and lot roads end at
the lot's rim instead of its centre so two zones never plow each other's windrows back and
forth. The fuel office reorders diesel on its own (`fuel.autoOrderBelowFrac`): with the foreman
working two machines every night, the depot ran dry in a fortnight and the fleet stood stranded.
A route's ends snap to the graph vertex with the least climb on the straight leg, not the nearest
one, so a cat parked under a run vertex no longer tries to go up it to go home; a machine that
parks itself stuck is left alone by the foreman for an hour; a stranded machine hands its job
back; and the foreman keeps specialists for the machines that need their licence (the only truck
driver had been put in the pickup, leaving the service truck unstaffable). Lots get a long smooth
apron rather than a clamped cone, road zones stop at the road's edge so a plow's own windrow is
not counted against it, an angled blade discharges entirely off its trailing end, a blade's
`CutDepthMm` is its effective moldboard height (the deepest snow taken in one pass), and the
route follower tracks the path itself rather than the next waypoint so a plow that swung wide
comes back onto its lane within a couple of lookaheads, entering the first lane from a lead-in
point behind its start.

## D-034 Per-cell work reads its anchors once a tick
Two machines working every night made the snow contact the most expensive thing in the
simulation: every cell under a track, tiller or blade read five to seven tuning anchors through
a locked dictionary, every tick. `SnowParams` gathers those anchors once per tick for the contact
and guest systems, `TuningData.F` is a single unlocked lookup (the simulation ticks on one
thread), and the running gear compacts only the strip it covered this tick with the fraction of
a pass that distance represents, which is the same total as compacting the whole footprint every
tick for a ninth of the work. Guest cohorts step every fourth tick with four times the step
(`simulation.guestTickDivisor`): walking, queueing and skiing at 5 Hz are indistinguishable from
20 Hz. Two simulated days went from 76 s to 43 s; the thirty-day fixtures dominate the suite.
