# Tuning anchors

Every balance number lives in `Assets/StreamingAssets/Data/tuning.json` (plus the per-record
tables in `vehicles.json`, `attachments.json`, `lifts.json`, ...). This document lists each
anchor group, its real-world basis, and the knob that changes it. Keys are dotted paths; read in
code via `ctx.Tuning.F("group.key")`.

| Key | Value | Basis | Effect |
| --- | --- | --- | --- |
| `simulation.resortOpenHour` | 9 | Typical alpine resort 8:30–9:00 | Day mode begins; guests spawn |
| `simulation.resortCloseHour` | 16 | Lifts close 15:30–16:30 | Night mode; grooming may start after sweep |
| `simulation.nightShiftStartHour` | 17 | Patrol sweep takes ~1 h | First hour groomers may enter runs |
| `simulation.guestsPerAgent` | 4 | Performance | Cohort size of one simulated guest agent |
| `simulation.aiVehicleTickDivisor` | 1 | Performance | AI vehicles integrate every N ticks |
| `terrain.mapSizeM` | 2048 | 4.2 km² brief | Heightmap edge |
| `terrain.snowCellSizeM` | 0.5 | Brief | Snow grid cell edge |

Later milestones append their groups here (snow, vehicles, lifts, guests, weather, snowmaking, economy, fleet).
