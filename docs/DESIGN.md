# Design

Alpine Resort Simulator models the gameplay of a small alpine resort: drive the machines at
night, run the resort by day, and live with the consequences on the snow and in the ledger.

## Pillars

1. **The snow surface is the shared state machine.** Every system reads and writes the same
   0.5 m snow grid. Grooming, skier traffic, snowfall, sun, wind and snowmaking mutate it. The
   Piste Quality Index (PQI) derived from it is the single number guests and the economy consume.
2. **Driving is the verb, managing is the consequence.** Vehicle operation is hands-on; management
   is the layer that makes driving matter.
3. **Capacity upgrades can make things worse.** A detachable quad raises lap rate, which degrades
   the piste faster than the extra ticket revenue covers grooming and repair. Reachable in normal
   play, and a regression test.
4. **Machines wear and specialise.** No universal machine. Wear accumulates per subsystem and
   changes performance before it causes failure.
5. **Night and day are different modes.** Night: vehicles and preparation. Day: guests,
   observation, management, consequences.

Real-world figures are tuning anchors that live in `Data/tuning.json`, never in code.

## Systems (see docs/ARCHITECTURE.md for tick order)

- **Terrain** — 2048×2048 m heightmap at 1 m, generated from the scenario and seed.
- **Snow** (M1) — struct-of-arrays grid over the skiable envelope; PQI per piste segment.
- **Vehicles** (M2, M6) — tracked / wheeled / articulated / walk-behind driving models; every
  machine writes to the snow grid through the same attachment interface.
- **Lifts** (M3, M4) — 26 types from magic carpet to 3S; queue simulation; wind and cold holds;
  construction as a driven job chain.
- **Guests** (M3) — archetype cohorts that path the piste graph, queue, ski (writing wear),
  spend, and leave a satisfaction score feeding a lagging reputation.
- **Weather and snowmaking** (M5) — hourly autocorrelated timeline, degrading forecast,
  wet-bulb gated guns, water and power budgets.
- **Economy** (M3, M6) — daily ledger, weekly P&L, seasonal report, loans and leases, five acts
  gated by capital and reputation.
- **Fleet** (M6) — 50 machine classes in 10 categories, 25+ attachments, market, condition,
  PM vs failure, parts, workshop tiers, licensing, fuel logistics.

## Act structure

| Act | Unlock | Shape |
| --- | --- | --- |
| I | start | One T-bar, one run, a tired groomer. Learn the night shift. |
| II | capital + reputation | Second lift (fixed-grip), second run, first hires. |
| III | capital + reputation | Snowmaking, road and lot maintenance, a real fleet. |
| IV | capital + reputation | The detachable upgrade trap. |
| V | capital + reputation | Gondolas, funitel, 3S across the gorge, tram. |
