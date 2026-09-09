using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Snow;
using AlpineSim.Core.Terrain;

namespace AlpineSim.Core.Pistes
{
    /// <summary>
    /// Builds pistes, zones and the skiable envelope in the snow grid from scenario definitions,
    /// and (re)computes the derived per-cell arrays after a load. Also used when a run is
    /// constructed later (M4) — AddPiste allocates the new corridor.
    /// </summary>
    public static class PisteNetworkBuilder
    {
        public const float CorridorMarginM = 4f;

        public static void BuildFromScenario(SimContext ctx)
        {
            var world = ctx.World;
            var scen = ctx.Sim.Scenario;
            var net = world.Pistes;
            var grid = world.Snow;
            int act = scen.StartingAct;

            // Base area disc
            var baseZone = new SurfaceZone { Id = "base", Kind = ZoneKind.BaseArea, RadiusM = scen.BaseAreaRadiusM };
            baseZone.Points.Add(scen.BaseArea.Pos);
            net.Zones.Add(baseZone);
            net.EnsureNode("base", NodeKind.Base, scen.BaseArea.Pos);

            foreach (var z in scen.Zones)
            {
                var zone = new SurfaceZone { Id = z.Id, Kind = z.Kind, WidthM = z.WidthM, RadiusM = z.RadiusM, Comment = z.Comment };
                foreach (var p in z.Points) zone.Points.Add(p.Pos);
                net.Zones.Add(zone);
            }

            foreach (var p in scen.Pistes)
            {
                if (p.ExistsFromAct <= 0 || p.ExistsFromAct > act) continue;
                AddPiste(ctx, p.Id, p.Name, p.Difficulty, p.WidthM, ToVecs(p.Points), p.TopNodeId, p.BottomNodeId, false);
            }

            AllocateEnvelope(ctx);
            ApplyInitialSnow(ctx);
            RebuildDerived(ctx);
        }

        public static Vec2[] ToVecs(List<MapPoint> pts)
        {
            var v = new Vec2[pts.Count];
            for (int i = 0; i < v.Length; i++) v[i] = pts[i].Pos;
            return v;
        }

        /// <summary>Adds a piste to the network (segments + nodes). Call AllocateEnvelope/RebuildDerived afterwards.</summary>
        public static PisteState AddPiste(SimContext ctx, string id, string name, PisteDifficulty difficulty, float widthM, Vec2[] points, string topNode, string bottomNode, bool allocateNow)
        {
            var net = ctx.World.Pistes;
            var terrain = ctx.Terrain;
            var piste = new PisteState { Id = id, Name = name, Difficulty = difficulty, WidthM = widthM, TopNodeId = topNode, BottomNodeId = bottomNode };
            piste.Points.AddRange(points);
            float length = 0f, maxGrade = 0f;
            for (int i = 0; i < points.Length - 1; i++)
            {
                var seg = new PisteSegment
                {
                    Id = net.NextSegmentId++,
                    PisteId = id,
                    Index = i,
                    A = points[i],
                    B = points[i + 1],
                    WidthM = widthM,
                };
                seg.LengthM = Vec2.Distance(seg.A, seg.B);
                float dh = terrain.SampleHeight(seg.A) - terrain.SampleHeight(seg.B);
                seg.GradeDeg = seg.LengthM > 0.01f ? MathF.Atan(dh / seg.LengthM) * MathUtil.Rad2Deg : 0f;
                length += seg.LengthM;
                if (MathF.Abs(seg.GradeDeg) > maxGrade) maxGrade = MathF.Abs(seg.GradeDeg);
                net.Segments.Add(seg);
                piste.SegmentIds.Add(seg.Id);
            }
            piste.LengthM = length;
            piste.VerticalM = terrain.SampleHeight(points[0]) - terrain.SampleHeight(points[points.Length - 1]);
            piste.MaxGradeDeg = maxGrade;
            net.Pistes.Add(piste);
            if (net.Node(topNode) == null) net.EnsureNode(topNode, NodeKind.Junction, points[0]);
            if (net.Node(bottomNode) == null) net.EnsureNode(bottomNode, NodeKind.Junction, points[points.Length - 1]);
            if (allocateNow) { AllocateEnvelope(ctx); RebuildDerived(ctx); }
            return piste;
        }

        /// <summary>Allocates grid chunks under every piste and zone and stamps surface / segment / lateral. Idempotent.</summary>
        public static void AllocateEnvelope(SimContext ctx)
        {
            var net = ctx.World.Pistes;
            var grid = ctx.World.Snow;
            // zones first so pistes override roads at crossings (piste surface wins for guests; roads are cleared anyway)
            foreach (var z in net.Zones) StampZone(grid, z);
            foreach (var seg in net.Segments) StampSegment(grid, seg, ctx.Sim.Scenario.InitialSnow);
        }

        private static void StampZone(SnowGrid grid, SurfaceZone z)
        {
            byte surface = (byte)(z.Kind == ZoneKind.Road || z.Kind == ZoneKind.CatTrack ? SurfaceType.Road
                : z.Kind == ZoneKind.Lot ? SurfaceType.Lot
                : z.Kind == ZoneKind.LiftRamp ? SurfaceType.LiftRamp
                : z.Kind == ZoneKind.BaseArea ? SurfaceType.BaseArea : SurfaceType.OffPiste);
            if (z.Kind == ZoneKind.Road || z.Kind == ZoneKind.CatTrack)
            {
                for (int i = 0; i < z.Points.Count - 1; i++)
                    StampCorridor(grid, z.Points[i], z.Points[i + 1], z.WidthM * 0.5f + 1f, (id, lateral) =>
                    {
                        if (grid.Surface[id] == (byte)SurfaceType.Piste) return;
                        grid.Surface[id] = surface;
                    });
            }
            else
            {
                if (z.Points.Count == 0) return;
                StampDisc(grid, z.Points[0], z.RadiusM, id =>
                {
                    if (grid.Surface[id] == (byte)SurfaceType.Piste) return;
                    grid.Surface[id] = surface;
                });
            }
        }

        private static void StampSegment(SnowGrid grid, PisteSegment seg, InitialSnowDef initial)
        {
            StampCorridor(grid, seg.A, seg.B, seg.WidthM * 0.5f + CorridorMarginM, (id, lateral) =>
            {
                float half = seg.WidthM * 0.5f;
                bool inside = MathF.Abs(lateral) <= half;
                if (inside)
                {
                    grid.Surface[id] = (byte)SurfaceType.Piste;
                    grid.Segment[id] = (short)seg.Id;
                    grid.Lateral[id] = MathUtil.Clamp(lateral / half, -1f, 1f);
                }
                else if (grid.Surface[id] == (byte)SurfaceType.OffPiste)
                {
                    grid.Lateral[id] = lateral < 0 ? -1f : 1f;
                }
            });
        }

        /// <summary>Visits every cell within halfWidth of segment AB (allocating chunks), passing the signed lateral offset.</summary>
        public static void StampCorridor(SnowGrid grid, Vec2 a, Vec2 b, float halfWidth, Action<int, float> visit)
        {
            float cs = grid.CellSize;
            int x0 = (int)MathF.Floor((MathF.Min(a.X, b.X) - halfWidth) / cs), x1 = (int)MathF.Ceiling((MathF.Max(a.X, b.X) + halfWidth) / cs);
            int y0 = (int)MathF.Floor((MathF.Min(a.Y, b.Y) - halfWidth) / cs), y1 = (int)MathF.Ceiling((MathF.Max(a.Y, b.Y) + halfWidth) / cs);
            Vec2 ab = b - a;
            float len2 = ab.SqrLength;
            if (len2 < 1e-6f) return;
            Vec2 dir = ab / MathF.Sqrt(len2);
            for (int cy = y0; cy <= y1; cy++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    Vec2 c = grid.CellCenter(cx, cy);
                    float u = Vec2.Dot(c - a, ab) / len2;
                    if (u < 0f || u > 1f) continue;
                    Vec2 closest = a + ab * u;
                    Vec2 off = c - closest;
                    float lateral = Vec2.Cross(dir, off); // signed distance, +left of travel
                    if (MathF.Abs(lateral) > halfWidth) continue;
                    int id = grid.EnsureCell(cx, cy);
                    if (id >= 0) visit(id, lateral);
                }
            }
        }

        public static void StampDisc(SnowGrid grid, Vec2 center, float radius, Action<int> visit)
        {
            float cs = grid.CellSize;
            int x0 = (int)MathF.Floor((center.X - radius) / cs), x1 = (int)MathF.Ceiling((center.X + radius) / cs);
            int y0 = (int)MathF.Floor((center.Y - radius) / cs), y1 = (int)MathF.Ceiling((center.Y + radius) / cs);
            float r2 = radius * radius;
            for (int cy = y0; cy <= y1; cy++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    Vec2 c = grid.CellCenter(cx, cy);
                    if (Vec2.SqrDistance(c, center) > r2) continue;
                    int id = grid.EnsureCell(cx, cy);
                    if (id >= 0) visit(id);
                }
        }

        private static void ApplyInitialSnow(SimContext ctx)
        {
            var grid = ctx.World.Snow;
            var init = ctx.Sim.Scenario.InitialSnow;
            int n = grid.CellCount;
            for (int i = 0; i < n; i++)
            {
                var s = (SurfaceType)grid.Surface[i];
                switch (s)
                {
                    case SurfaceType.Piste:
                    case SurfaceType.NordicTrail:
                    case SurfaceType.Park:
                        grid.PackedMm[i] = init.PackedMm;
                        grid.Density[i] = init.PackedDensity;
                        grid.LooseMm[i] = init.LooseMm;
                        grid.Roughness[i] = init.Roughness;
                        break;
                    case SurfaceType.Road:
                    case SurfaceType.Lot:
                    case SurfaceType.BaseArea:
                    case SurfaceType.LiftRamp:
                        grid.PackedMm[i] = init.PackedMm * 0.25f;
                        grid.Density[i] = init.PackedDensity;
                        grid.LooseMm[i] = init.LooseMm;
                        grid.Roughness[i] = 0.2f;
                        break;
                    default:
                        grid.PackedMm[i] = init.OffPistePackedMm;
                        grid.Density[i] = init.OffPisteDensity;
                        grid.LooseMm[i] = init.OffPisteLooseMm;
                        grid.Roughness[i] = 0.5f;
                        break;
                }
                grid.TempC[i] = init.TempC;
                grid.Moisture[i] = 0.05f;
                grid.LastGroomTick[i] = -1;
            }
            grid.BackgroundLooseMm = init.OffPisteLooseMm;
            grid.BackgroundPackedMm = init.OffPistePackedMm;
            grid.BackgroundDensity = init.OffPisteDensity;
            grid.MarkAllDirty();
        }

        /// <summary>Recomputes elevation, slope, sun exposure, segment cell lists and lateral data from terrain and pistes.</summary>
        public static void RebuildDerived(SimContext ctx)
        {
            var grid = ctx.World.Snow;
            var terrain = ctx.Terrain;
            var net = ctx.World.Pistes;
            int n = grid.CellCount;
            foreach (var seg in net.Segments) seg.Cells.Clear();
            foreach (var z in net.Zones) z.Cells.Clear();
            var segById = new Dictionary<int, PisteSegment>();
            foreach (var seg in net.Segments) segById[seg.Id] = seg;
            for (int i = 0; i < n; i++)
            {
                var c = grid.CellCenterOf(i);
                grid.Elevation[i] = terrain.SampleHeight(c);
                grid.SlopeDeg[i] = terrain.SlopeDeg(c.X, c.Y);
                float aspect = terrain.AspectRad(c.X, c.Y);
                // south-facing (aspect = -pi/2) gets full sun; north-facing none; flat = 0.5
                float slopeW = MathUtil.Clamp01(grid.SlopeDeg[i] / 30f);
                float facing = 0.5f - 0.5f * MathF.Sin(aspect);
                grid.SunExposure[i] = MathUtil.Lerp(0.5f, facing, slopeW);
                int segId = grid.Segment[i];
                if (segId >= 0 && segById.TryGetValue(segId, out var seg)) seg.Cells.Add(i);
            }
            // zone cell lists
            foreach (var z in net.Zones)
            {
                if (z.Kind == ZoneKind.Road || z.Kind == ZoneKind.CatTrack)
                {
                    for (int i = 0; i < z.Points.Count - 1; i++)
                        StampCorridor(grid, z.Points[i], z.Points[i + 1], z.WidthM * 0.5f + 1f, (id, lat) => { if (grid.Surface[id] == (byte)SurfaceType.Road) z.Cells.Add(id); });
                }
                else if (z.Points.Count > 0)
                {
                    StampDisc(grid, z.Points[0], z.RadiusM, id => { if (grid.Surface[id] != (byte)SurfaceType.Piste) z.Cells.Add(id); });
                }
            }
            // lateral for piste cells must survive a load: recompute from segments
            foreach (var seg in net.Segments)
            {
                Vec2 dir = seg.Direction;
                float half = seg.WidthM * 0.5f;
                foreach (var id in seg.Cells)
                {
                    Vec2 c = grid.CellCenterOf(id);
                    float lateral = Vec2.Cross(dir, c - seg.A);
                    grid.Lateral[id] = MathUtil.Clamp(lateral / half, -1f, 1f);
                }
            }
        }
    }
}
