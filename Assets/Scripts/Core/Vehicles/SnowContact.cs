using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Snow;

namespace AlpineSim.Core.Vehicles
{
    /// <summary>
    /// The single interface through which every machine writes to the snow grid: running gear
    /// compacts, blades push mass, tillers set density and clear roughness, blowers relocate mass,
    /// buckets scoop and dump, spreaders salt. All mass moves are conserving except deposits from
    /// cargo (which was removed elsewhere) and blower throw-off outside the allocated envelope
    /// (deposited back at the source when the target cell does not exist).
    /// </summary>
    public sealed class SnowContact
    {
        private readonly List<int> _cells = new List<int>(256);
        private readonly List<int> _cells2 = new List<int>(64);

        public struct ContactResult
        {
            public float ImplementDragN;
            public float ImplementPowerW;
            public float BladeLoadKg;
            public float LooseDepthM;
            public float ColumnDensity;
        }

        /// <summary>Applies one tick of contact for a machine that moved from prevPos/prevHeading to its current pose.</summary>
        public ContactResult Apply(SimContext ctx, VehicleState v, VehicleDef def, Vec2 prevPos, float dt)
        {
            var grid = ctx.World.Snow;
            var t = ctx.Tuning;
            var res = new ContactResult();
            Vec2 fwd = Vec2.FromAngle(v.Heading);
            Vec2 right = new Vec2(fwd.Y, -fwd.X);
            float bodyL = def.Visual != null ? def.Visual.BodyL : 6f;
            float bodyW = def.Visual != null ? def.Visual.BodyW : 2.5f;
            float moved = Vec2.Distance(v.Pos, prevPos);
            float cs = grid.CellSize;

            // ---- snow under the machine (for physics)
            int under = grid.CellIdAt(v.Pos);
            if (under >= 0) { res.LooseDepthM = grid.LooseMm[under] * 0.001f; res.ColumnDensity = grid.ColumnDensity(under); }
            else { res.LooseDepthM = grid.BackgroundLooseMm * 0.001f; res.ColumnDensity = grid.BackgroundDensity; }

            // ---- running gear compaction
            if (def.ChassisType != ChassisType.Stationary && moved > 1e-4f)
            {
                float trackLen = MathF.Max(1.5f, bodyL * 0.75f);
                float exposure = MathUtil.Clamp01(moved / trackLen);
                float pressure = def.GroundPressureKpa;
                if (def.ChassisType == ChassisType.Tracked || def.TrackWidthM > 0f)
                {
                    float tw = MathF.Max(0.3f, def.TrackWidthM > 0f ? def.TrackWidthM : 0.5f);
                    float offset = bodyW * 0.5f - tw * 0.5f;
                    CompactStrip(grid, t, v.Pos + right * offset, fwd, right, trackLen, tw, pressure, exposure);
                    CompactStrip(grid, t, v.Pos - right * offset, fwd, right, trackLen, tw, pressure, exposure);
                }
                else if (def.ChassisType == ChassisType.WalkBehind)
                {
                    CompactStrip(grid, t, v.Pos, fwd, right, 0.8f, 0.6f, pressure, exposure);
                }
                else
                {
                    float tireW = def.TireSpec != null ? def.TireSpec.WidthM : 0.4f;
                    int axles = def.Visual != null ? System.Math.Max(1, def.Visual.Axles) : 2;
                    float half = bodyW * 0.5f - tireW * 0.5f;
                    for (int a = 0; a < axles; a++)
                    {
                        float along = (axles == 1 ? 0f : -bodyL * 0.3f + a * (bodyL * 0.6f / (axles - 1)));
                        Vec2 c = v.Pos + fwd * along;
                        CompactStrip(grid, t, c + right * half, fwd, right, MathF.Max(0.5f, moved), tireW, pressure, 1f);
                        CompactStrip(grid, t, c - right * half, fwd, right, MathF.Max(0.5f, moved), tireW, pressure, 1f);
                    }
                }
            }

            // ---- implements
            for (int i = 0; i < v.Mounted.Count; i++)
            {
                var m = v.Mounted[i];
                var att = ctx.Data.Attachment(m.DefId);
                if (att == null) continue;
                switch (att.Kind)
                {
                    case AttachmentKind.Blade:
                    case AttachmentKind.Blade12Way:
                    case AttachmentKind.UBlade:
                    case AttachmentKind.VPlow:
                    case AttachmentKind.PlowStraight:
                    case AttachmentKind.BoxPusher:
                    case AttachmentKind.ParkBlade:
                        Blade(ctx, v, def, att, m, fwd, right, bodyL, moved, ref res);
                        break;
                    case AttachmentKind.Tiller:
                    case AttachmentKind.TrackSetter:
                    case AttachmentKind.PipeCutter:
                        Tiller(ctx, v, def, att, m, fwd, right, bodyL, moved, ref res);
                        break;
                    case AttachmentKind.BlowerHead:
                        Blower(ctx, v, def, att, m, fwd, right, bodyL, moved, dt, ref res);
                        break;
                    case AttachmentKind.SnowBucket:
                    case AttachmentKind.LightBucket:
                    case AttachmentKind.Grapple:
                        Bucket(ctx, v, def, att, m, fwd, right, bodyL, dt, ref res);
                        break;
                    case AttachmentKind.Spreader:
                        Spreader(ctx, v, att, m, fwd, right, bodyL, moved, false, ref res);
                        break;
                    case AttachmentKind.BrineTank:
                        Spreader(ctx, v, att, m, fwd, right, bodyL, moved, true, ref res);
                        break;
                    case AttachmentKind.Broom:
                        Broom(ctx, v, att, m, fwd, right, bodyL, moved, ref res);
                        break;
                    default:
                        break;
                }
            }
            // built-in blower on self-propelled rotary blowers (no attachment)
            if (def.Category == VehicleCategory.Blower && v.Input.Implement && def.HasSpec("blowerCapacityTph") && !HasKind(ctx, v, AttachmentKind.BlowerHead))
            {
                float width = def.SpecOr("intakeWidthM", bodyW);
                float rateKgs = def.Spec("blowerCapacityTph") * 1000f / 3600f;
                float throwM = def.SpecOr("throwDistanceM", 25f);
                DoBlow(ctx, v, fwd, right, bodyL * 0.5f + 0.6f, width, rateKgs, throwM, moved, dt, ref res);
                res.ImplementPowerW += def.EnginePowerKw * 1000f * 0.7f;
            }
            // truck/loader dumping
            if (v.Input.Work && v.CargoKg > 0f && (def.HasRole(VehicleRole.Haul) && !HasBucket(ctx, v)))
            {
                DumpCargo(ctx, v, fwd, right, bodyL, dt);
            }
            return res;
        }

        private bool HasKind(SimContext ctx, VehicleState v, AttachmentKind kind)
        {
            for (int i = 0; i < v.Mounted.Count; i++) { var a = ctx.Data.Attachment(v.Mounted[i].DefId); if (a != null && a.Kind == kind) return true; }
            return false;
        }

        private bool HasBucket(SimContext ctx, VehicleState v) => HasKind(ctx, v, AttachmentKind.SnowBucket) || HasKind(ctx, v, AttachmentKind.LightBucket);

        // ------------------------------------------------------------------ helpers
        /// <summary>Cells of a rectangle centred at c, extending ±len/2 along fwd and ±w/2 along right.</summary>
        private void RectCells(SnowGrid grid, Vec2 c, Vec2 fwd, Vec2 right, float len, float w, List<int> into, bool allocate)
        {
            into.Clear();
            float cs = grid.CellSize;
            int nl = System.Math.Max(1, (int)MathF.Ceiling(len / cs));
            int nw = System.Math.Max(1, (int)MathF.Ceiling(w / cs));
            for (int i = 0; i < nl; i++)
            {
                float a = -len * 0.5f + (i + 0.5f) * (len / nl);
                for (int j = 0; j < nw; j++)
                {
                    float b = -w * 0.5f + (j + 0.5f) * (w / nw);
                    Vec2 p = c + fwd * a + right * b;
                    int id = allocate ? grid.EnsureCellAt(p) : grid.CellIdAt(p);
                    if (id >= 0 && !into.Contains(id)) into.Add(id);
                }
            }
        }

        private void CompactStrip(SnowGrid grid, TuningData t, Vec2 c, Vec2 fwd, Vec2 right, float len, float w, float pressure, float exposure)
        {
            RectCells(grid, c, fwd, right, len, w, _cells, false);
            for (int i = 0; i < _cells.Count; i++) SnowOps.Compact(grid, _cells[i], pressure, exposure, t);
        }

        /// <summary>Strip swept since last tick: width w centred on the implement line at offset along fwd, length = distance moved (min one cell).</summary>
        private void SweptCells(SnowGrid grid, Vec2 pos, Vec2 fwd, Vec2 right, float alongOffset, float width, float moved, List<int> into, bool allocate)
        {
            float len = MathF.Max(grid.CellSize, moved);
            Vec2 c = pos + fwd * (alongOffset - len * 0.5f * MathF.Sign(moved));
            RectCells(grid, c, fwd, right, len, width, into, allocate);
        }

        private void Blade(SimContext ctx, VehicleState v, VehicleDef def, AttachmentDef att, MountedAttachment m, Vec2 fwd, Vec2 right, float bodyL, float moved, ref ContactResult res)
        {
            var grid = ctx.World.Snow;
            var t = ctx.Tuning;
            float g = t.F("vehicles.gravity");
            float cellArea = grid.CellAreaM2;
            float bladeOffset = bodyL * 0.5f + 0.6f;
            float loadDensity = t.F("vehicles.bladeLoadDensity");
            bool down = m.Lift < 0.35f && v.EngineOn;
            bool forward = v.Speed > 0.05f;
            res.BladeLoadKg += m.LoadKg;
            if (down && forward && moved > 1e-4f)
            {
                float depth = att.Effects.CutDepthMm * (1f - m.Lift / 0.35f);
                // angled blade: effective width shrinks, spill goes to the trailing side
                float angle = m.Angle;
                float width = att.WorkingWidthM * MathF.Cos(angle);
                Vec2 bladeCenter = v.Pos + fwd * bladeOffset;
                SweptCells(grid, v.Pos, fwd, right, bladeOffset, width, moved, _cells, false);
                float gained = 0f;
                for (int i = 0; i < _cells.Count; i++)
                {
                    float mass = SnowOps.CutDepth(grid, _cells[i], depth, out float dens);
                    gained += mass * cellArea;
                }
                m.LoadKg += gained;
                if (m.LoadKg > 0f) m.LoadDensity = loadDensity;
                v.PlowedM2Today += width * moved;
                // windrow spill: a fraction per metre plus everything over capacity
                float capacity = MathF.Max(200f, att.Effects.PushCapacityKg);
                float spill = m.LoadKg * MathUtil.Clamp01(t.F("vehicles.bladeSpillFracPerM") * moved);
                if (m.LoadKg > capacity) spill += m.LoadKg - capacity;
                if (spill > 0f)
                {
                    m.LoadKg -= spill;
                    // deposit to the sides just outside the blade ends (toward the trailing side when angled)
                    float leftShare = angle > 0.05f ? 0.15f : (angle < -0.05f ? 0.85f : 0.5f);
                    DepositLine(grid, bladeCenter + right * (width * 0.5f + 0.6f), fwd, spill * (1f - leftShare) / cellArea, loadDensity, 1.2f);
                    DepositLine(grid, bladeCenter - right * (width * 0.5f + 0.6f), fwd, spill * leftShare / cellArea, loadDensity, 1.2f);
                }
                res.ImplementDragN += width * depth * 0.001f * loadDensity * g * 2.5f + m.LoadKg * g * 0.1f;
                res.ImplementPowerW += 2000f * width;
            }
            else if (m.LoadKg > 0f && (!down || v.Speed < -0.05f || (!forward && v.Input.Throttle <= 0f)))
            {
                // raised, reversing or stopped: the pile stays where the blade is
                Vec2 bladeCenter = v.Pos + fwd * bladeOffset;
                float width = att.WorkingWidthM * MathF.Cos(m.Angle);
                DepositLine(grid, bladeCenter + fwd * 0.5f, right, m.LoadKg / cellArea, m.LoadDensity, width);
                m.LoadKg = 0f;
            }
            res.BladeLoadKg = m.LoadKg;
            if (m.LoadKg > 0f) m.Hours += 0f;
        }

        /// <summary>Deposits mass (kg per m² units already divided by cell area) along a short line of cells, allocating chunks so nothing is lost.</summary>
        private void DepositLine(SnowGrid grid, Vec2 center, Vec2 dir, float massPerCell, float density, float length)
        {
            if (massPerCell <= 0f) return;
            float cs = grid.CellSize;
            int n = System.Math.Max(1, (int)MathF.Ceiling(length / cs));
            _cells2.Clear();
            for (int i = 0; i < n; i++)
            {
                float a = -length * 0.5f + (i + 0.5f) * (length / n);
                int id = grid.EnsureCellAt(center + dir * a);
                if (id >= 0 && !_cells2.Contains(id)) _cells2.Add(id);
            }
            if (_cells2.Count == 0) return;
            float per = massPerCell / _cells2.Count;
            for (int i = 0; i < _cells2.Count; i++) SnowOps.DepositPacked(grid, _cells2[i], per, density);
        }

        private void Tiller(SimContext ctx, VehicleState v, VehicleDef def, AttachmentDef att, MountedAttachment m, Vec2 fwd, Vec2 right, float bodyL, float moved, ref ContactResult res)
        {
            var grid = ctx.World.Snow;
            var t = ctx.Tuning;
            bool engaged = v.Input.Tiller && v.EngineOn && m.Lift < 0.5f;
            m.Engaged = engaged;
            if (!engaged) return;
            float width = att.WorkingWidthM;
            float offset = -(bodyL * 0.5f + 0.9f);
            float fullDrag = t.F("vehicles.tillerDragNPerM") * width;
            res.ImplementDragN += fullDrag;
            res.ImplementPowerW += t.F("vehicles.tillerHydraulicLoadKw") * 1000f * (width / 4.3f) * (1f - v.Condition.WearHydraulics * 0.3f);
            if (MathF.Abs(v.Speed) < 0.05f || moved <= 1e-4f) return;
            SweptCells(grid, v.Pos, fwd, right, offset, width, moved, _cells, false);
            if (_cells.Count == 0) return;
            float depthRating = att.Effects.DepthRatingMm > 0f ? att.Effects.DepthRatingMm : 250f;
            float finish = att.Effects.FinishQuality > 0f ? att.Effects.FinishQuality : 0.9f;
            float eff = att.Effects.TillEfficiency > 0f ? att.Effects.TillEfficiency : 0.6f;
            float target = att.Effects.TillTargetDensity > 0f ? att.Effects.TillTargetDensity : t.F("snow.tillerTargetDensity");
            float comp = att.Effects.TillCompaction > 0f ? att.Effects.TillCompaction : t.F("snow.tillerCompactionPerPass");
            // speed penalty: above the working speed range the finish degrades
            float maxWork = def.WorkingSpeedKmh.Max / 3.6f;
            float speedPenalty = v.Speed > maxWork ? MathUtil.Clamp01(1f - (v.Speed - maxWork) / maxWork) : 1f;
            byte dir = (byte)(MathUtil.Repeat(v.Heading, MathUtil.TwoPi) / MathUtil.TwoPi * 255f);
            int tick = (int)ctx.Time.Tick;
            for (int i = 0; i < _cells.Count; i++)
            {
                int id = _cells[i];
                float loose = grid.LooseMm[id];
                float depthFactor = loose > depthRating ? MathUtil.Clamp01(depthRating / loose) : 1f;
                SnowOps.Till(grid, id, target, eff * depthFactor * speedPenalty, comp, finish * speedPenalty * depthFactor, dir, tick, t);
            }
            float area = width * moved; // geometric swath, independent of cell quantisation and tick rate
            v.TilledM2Today += area;
            v.TilledM2Total += area;
        }

        private void Blower(SimContext ctx, VehicleState v, VehicleDef def, AttachmentDef att, MountedAttachment m, Vec2 fwd, Vec2 right, float bodyL, float moved, float dt, ref ContactResult res)
        {
            bool on = v.Input.Implement && v.EngineOn && m.Lift < 0.5f;
            m.Engaged = on;
            if (!on) return;
            float rate = att.Effects.RemoveRateKgs > 0f ? att.Effects.RemoveRateKgs : 50f;
            float throwM = att.Effects.ThrowDistanceM > 0f ? att.Effects.ThrowDistanceM : 15f;
            res.ImplementPowerW += (att.PowerDrawKw > 0f ? att.PowerDrawKw : 40f) * 1000f;
            DoBlow(ctx, v, fwd, right, bodyL * 0.5f + 0.5f, att.WorkingWidthM, rate, throwM, moved, dt, ref res);
        }

        private void DoBlow(SimContext ctx, VehicleState v, Vec2 fwd, Vec2 right, float offset, float width, float rateKgs, float throwM, float moved, float dt, ref ContactResult res)
        {
            var grid = ctx.World.Snow;
            var t = ctx.Tuning;
            if (moved <= 1e-4f && v.Speed >= 0f && MathF.Abs(v.Speed) < 0.02f) { /* stationary blower still eats the pile in front */ moved = grid.CellSize; }
            SweptCells(grid, v.Pos, fwd, right, offset, width, MathF.Max(moved, grid.CellSize), _cells, false);
            if (_cells.Count == 0) return;
            float budget = rateKgs * dt;
            float depth = t.F("vehicles.blowerIntakeDepthMm");
            float removed = 0f, densSum = 0f;
            for (int i = 0; i < _cells.Count && budget > 0f; i++)
            {
                int id = _cells[i];
                float avail = MathF.Min(grid.MassKgPerM2(id), depth * 0.001f * grid.ColumnDensity(id));
                float take = MathF.Min(avail, budget / grid.CellAreaM2);
                if (take <= 1e-5f) continue;
                float dens = grid.ColumnDensity(id);
                float got = SnowOps.Remove(grid, id, take) * grid.CellAreaM2;
                removed += got; densSum += dens * got; budget -= got;
            }
            if (removed <= 0f) return;
            v.BlownKgToday += removed;
            float density = MathF.Max(SnowOps.MinDensity, densSum / removed) * 0.6f; // thrown snow lands fluffy
            // chute side: default right; blows outside the strip
            Vec2 target = v.Pos + fwd * offset + right * (width * 0.5f + throwM);
            int center = grid.EnsureCellAt(target);
            _cells2.Clear();
            grid.CellCoords(target, out int cx, out int cy);
            for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) { int id = grid.EnsureCell(cx + dx, cy + dy); if (id >= 0) _cells2.Add(id); }
            if (_cells2.Count == 0) { if (center >= 0) SnowOps.DepositLoose(grid, center, removed / grid.CellAreaM2); return; }
            float per = removed / _cells2.Count / grid.CellAreaM2;
            for (int i = 0; i < _cells2.Count; i++) SnowOps.DepositPacked(grid, _cells2[i], per, density);
            res.ImplementDragN += rateKgs * 2f;
        }

        private void Bucket(SimContext ctx, VehicleState v, VehicleDef def, AttachmentDef att, MountedAttachment m, Vec2 fwd, Vec2 right, float bodyL, float dt, ref ContactResult res)
        {
            var grid = ctx.World.Snow;
            var t = ctx.Tuning;
            if (!v.Input.Work || !v.EngineOn) return;
            float bucketM3 = att.Effects.BucketM3 > 0f ? att.Effects.BucketM3 : MathF.Max(0.3f, def.BucketM3);
            float offset = bodyL * 0.5f + 0.8f;
            Vec2 c = v.Pos + fwd * offset;
            if (m.Lift < 0.4f && v.CargoKg < bucketM3 * 300f)
            {
                // scoop: fill over bucketScoopSeconds
                float dens = MathF.Max(SnowOps.MinDensity, grid.DensityAt(c));
                float capacityKg = bucketM3 * dens;
                float wantKg = capacityKg * dt / t.F("vehicles.bucketScoopSeconds");
                RectCells(grid, c, fwd, right, 1.2f, att.WorkingWidthM, _cells, false);
                float got = 0f;
                for (int i = 0; i < _cells.Count && wantKg > 0f; i++)
                {
                    float take = MathF.Min(wantKg / grid.CellAreaM2, grid.MassKgPerM2(_cells[i]) * 0.5f);
                    float r = SnowOps.Remove(grid, _cells[i], take) * grid.CellAreaM2;
                    got += r; wantKg -= r;
                }
                if (got > 0f)
                {
                    v.CargoDensity = v.CargoKg + got > 0f ? (v.CargoDensity * v.CargoKg + dens * got) / (v.CargoKg + got) : dens;
                    v.CargoKg += got;
                    v.CargoKind = "snow";
                }
                res.ImplementPowerW += 15000f;
            }
            else if (m.Lift >= 0.4f && v.CargoKg > 0f)
            {
                // dump into a truck bed if one is right there, else on the ground
                float rateKg = v.CargoKg * dt / t.F("vehicles.bucketDumpSeconds") + 1f;
                float amount = MathF.Min(v.CargoKg, rateKg);
                var truck = FindTruck(ctx, v, c, t.F("vehicles.truckLoadTransferRadiusM"));
                if (truck != null && truck.CargoKg < TruckCapacityKg(ctx, truck))
                {
                    float room = TruckCapacityKg(ctx, truck) - truck.CargoKg;
                    float moved = MathF.Min(amount, room);
                    truck.CargoDensity = truck.CargoKg + moved > 0f ? (truck.CargoDensity * truck.CargoKg + v.CargoDensity * moved) / (truck.CargoKg + moved) : v.CargoDensity;
                    truck.CargoKg += moved;
                    truck.CargoKind = v.CargoKind;
                    v.CargoKg -= moved;
                }
                else
                {
                    DepositLine(grid, c + fwd * 0.8f, right, amount / grid.CellAreaM2, v.CargoDensity, att.WorkingWidthM);
                    v.CargoKg -= amount;
                }
                if (v.CargoKg < 0.5f) v.CargoKg = 0f;
                res.ImplementPowerW += 8000f;
            }
        }

        public static float TruckCapacityKg(SimContext ctx, VehicleState truck)
        {
            var d = truck.Def ?? ctx.Data.Vehicle(truck.DefId);
            if (d == null) return 0f;
            return d.CargoCapacityKg > 0f ? d.CargoCapacityKg : d.BucketM3 * 400f;
        }

        private static VehicleState FindTruck(SimContext ctx, VehicleState self, Vec2 at, float radius)
        {
            var list = ctx.World.Vehicles.List;
            VehicleState best = null; float bd = radius * radius;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == self) continue;
                var d = o.Def ?? ctx.Data.Vehicle(o.DefId);
                if (d == null || !d.HasRole(VehicleRole.Haul) || d.CargoCapacityKg <= 0f) continue;
                float dd = Vec2.SqrDistance(o.Pos, at);
                if (dd < bd) { bd = dd; best = o; }
            }
            return best;
        }

        private void DumpCargo(SimContext ctx, VehicleState v, Vec2 fwd, Vec2 right, float bodyL, float dt)
        {
            var grid = ctx.World.Snow;
            var t = ctx.Tuning;
            float amount = MathF.Min(v.CargoKg, v.CargoKg * dt / t.F("vehicles.bucketDumpSeconds") * 0.5f + 5f);
            if (v.CargoKind == "snow" || string.IsNullOrEmpty(v.CargoKind))
            {
                Vec2 c = v.Pos - fwd * (bodyL * 0.5f + 1.5f);
                RectCells(grid, c, fwd, right, 3f, 4f, _cells2, true);
                if (_cells2.Count > 0)
                {
                    float per = amount / _cells2.Count / grid.CellAreaM2;
                    for (int i = 0; i < _cells2.Count; i++) SnowOps.DepositPacked(grid, _cells2[i], per, v.CargoDensity);
                }
            }
            v.CargoKg -= amount;
            if (v.CargoKg < 0.5f) { v.CargoKg = 0f; }
        }

        private void Spreader(SimContext ctx, VehicleState v, AttachmentDef att, MountedAttachment m, Vec2 fwd, Vec2 right, float bodyL, float moved, bool brine, ref ContactResult res)
        {
            bool on = v.Input.Implement && v.EngineOn;
            m.Engaged = on;
            if (!on || moved <= 1e-4f) return;
            var grid = ctx.World.Snow;
            var t = ctx.Tuning;
            float width = att.Effects.SpreadWidthM > 0f ? att.Effects.SpreadWidthM : att.WorkingWidthM;
            float kgPerM2;
            if (brine)
            {
                float lPerM2 = att.Effects.BrineLPerM2 > 0f ? att.Effects.BrineLPerM2 : 0.05f;
                float litres = lPerM2 * width * moved;
                if (v.BrineL <= 0f) return;
                litres = MathF.Min(litres, v.BrineL);
                v.BrineL -= litres;
                kgPerM2 = litres / (width * moved) * t.F("roads.brineEquivalentKgPerL");
            }
            else
            {
                float rate = att.Effects.SpreadRateKgPerLaneKm > 0f ? att.Effects.SpreadRateKgPerLaneKm : 150f;
                kgPerM2 = rate / 1000f / t.F("vehicles.spreadLaneWidthM");
                float kg = kgPerM2 * width * moved;
                if (v.SaltKg <= 0f) return;
                kg = MathF.Min(kg, v.SaltKg);
                v.SaltKg -= kg;
                kgPerM2 = kg / (width * moved);
            }
            SweptCells(grid, v.Pos, fwd, right, -(bodyL * 0.5f + 0.5f), width, moved, _cells, false);
            for (int i = 0; i < _cells.Count; i++) SnowOps.AddSalt(grid, _cells[i], kgPerM2);
            res.ImplementPowerW += 3000f;
        }

        private void Broom(SimContext ctx, VehicleState v, AttachmentDef att, MountedAttachment m, Vec2 fwd, Vec2 right, float bodyL, float moved, ref ContactResult res)
        {
            bool on = v.Input.Implement && v.EngineOn && m.Lift < 0.5f;
            m.Engaged = on;
            if (!on || moved <= 1e-4f) return;
            var grid = ctx.World.Snow;
            SweptCells(grid, v.Pos, fwd, right, bodyL * 0.5f + 0.5f, att.WorkingWidthM, moved, _cells, false);
            float total = 0f;
            for (int i = 0; i < _cells.Count; i++) total += SnowOps.CutDepth(grid, _cells[i], 30f, out _) * grid.CellAreaM2;
            if (total > 0f) DepositLine(grid, v.Pos + fwd * (bodyL * 0.5f + 0.5f) + right * (att.WorkingWidthM * 0.5f + 0.5f), fwd, total / grid.CellAreaM2, 200f, MathF.Max(moved, 0.5f));
            res.ImplementPowerW += 5000f;
        }
    }
}
