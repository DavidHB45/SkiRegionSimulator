using System;
using System.Collections.Generic;
using AlpineSim.Core.Data;
using AlpineSim.Core.Math;
using AlpineSim.Core.Pistes;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Tasks;

namespace AlpineSim.Core.Vehicles
{
    /// <summary>
    /// The hired operator: pure-pursuit route following with stuck recovery, lane-by-lane grooming,
    /// zone passes for plowing/blowing/spreading, haul loops, drive-to-site work, and the low-fuel
    /// return. Competence (0.6..0.95) scales speed, finish quality and fuel efficiency; operators
    /// refuse slopes above their rating.
    /// </summary>
    public sealed class VehicleAi
    {
        private readonly List<Vec2> _tmp = new List<Vec2>();

        public void Step(SimContext ctx, VehicleSystem vs, VehicleState v, VehicleDef def, float competence, float maxSlopeDeg, float dt)
        {
            var ai = v.Ai;
            var input = v.Input;
            input.Clear();
            if (ai.Mode == AiMode.Idle || ai.Mode == AiMode.Stranded) { input.Throttle = 0f; return; }
            if (!v.EngineOn && !v.Stranded)
            {
                if (!vs.StartEngine(ctx, v.Id, out _)) { ai.WorkTimer += dt; return; }
            }
            if (v.Stranded) { ai.Mode = AiMode.Stranded; return; }
            var t = ctx.Tuning;
            input.Lights = true;

            // low fuel: go home
            float reserve = t.F("vehicles.fuelReserveWarningFrac");
            if (ai.Mode != AiMode.Refuel && ai.Mode != AiMode.ReturnToBase && v.Fuel < def.EnergyCapacity * reserve && def.FuelType != FuelType.None)
            {
                ai.Phase = "low fuel: returning to depot";
                var depot = ctx.Sim.Scenario.Landmark(ctx.Sim.Scenario.FuelLandmarkId).Pos;
                ai.Route = vs.FindRoute(ctx, v.Pos, depot);
                ai.RouteIndex = 0;
                ai.Mode = AiMode.Refuel;
            }

            switch (ai.Mode)
            {
                case AiMode.FollowRoute:
                case AiMode.GoToSite:
                case AiMode.ReturnToBase:
                case AiMode.Refuel:
                    if (FollowRoute(ctx, v, def, competence, maxSlopeDeg, dt))
                    {
                        if (ai.Mode == AiMode.GoToSite) { ai.Mode = AiMode.WorkAtSite; ai.WorkTimer = 0f; }
                        else if (ai.Mode == AiMode.Refuel) { ai.Mode = AiMode.Idle; ai.Phase = "waiting for fuel"; vs.ArrivedAtDepot(ctx, v); }
                        else { ai.Mode = AiMode.Idle; ai.Phase = "parked"; vs.OnAiIdle(ctx, v); }
                    }
                    break;
                case AiMode.GroomPiste:
                    Groom(ctx, vs, v, def, competence, maxSlopeDeg, dt);
                    break;
                case AiMode.PlowZone:
                case AiMode.BlowZone:
                case AiMode.SpreadZone:
                    ZoneWork(ctx, vs, v, def, competence, maxSlopeDeg, dt);
                    break;
                case AiMode.HaulLoop:
                    Haul(ctx, vs, v, def, competence, maxSlopeDeg, dt);
                    break;
                case AiMode.WorkAtSite:
                    input.Work = true;
                    input.Implement = true;
                    input.Throttle = 0f;
                    ai.WorkTimer += dt;
                    break;
            }
        }

        // ------------------------------------------------------------------ route following
        /// <summary>Returns true when the last waypoint is reached.</summary>
        private bool FollowRoute(SimContext ctx, VehicleState v, VehicleDef def, float competence, float maxSlopeDeg, float dt)
        {
            var ai = v.Ai;
            var input = v.Input;
            var t = ctx.Tuning;
            if (ai.Route == null || ai.RouteIndex >= ai.Route.Count) return true;
            float reach = t.F("vehicles.aiWaypointReachM");
            Vec2 target = ai.Route[ai.RouteIndex];
            // pure pursuit: aim at a point lookahead metres along the route
            float look = t.F("vehicles.aiLookaheadM");
            Vec2 aim = target;
            if (Vec2.Distance(v.Pos, target) < look && ai.RouteIndex + 1 < ai.Route.Count)
                aim = Vec2.Lerp(target, ai.Route[ai.RouteIndex + 1], MathUtil.Clamp01((look - Vec2.Distance(v.Pos, target)) / MathF.Max(1f, Vec2.Distance(target, ai.Route[ai.RouteIndex + 1]))));
            Vec2 to = aim - v.Pos;
            float dist = Vec2.Distance(v.Pos, target);
            bool last = ai.RouteIndex == ai.Route.Count - 1;
            if (dist < reach || (!last && PassedWaypoint(v, ai.Route, ai.RouteIndex)))
            {
                ai.RouteIndex++;
                if (ai.RouteIndex >= ai.Route.Count) { input.Throttle = 0f; input.Brake = 1f; return true; }
                return false;
            }
            float desired = to.Angle;
            float err = MathUtil.DeltaAngle(v.Heading, desired);
            // stuck recovery
            if (ai.StuckTimer < 0f)
            {
                ai.StuckTimer += dt;
                input.Throttle = -0.6f;
                input.Steer = err > 0f ? 0.5f : -0.5f;
                return false;
            }
            if (MathF.Abs(v.Speed) < 0.15f && v.EngineOn) ai.WorkTimer += dt; else ai.WorkTimer = 0f;
            if (ai.WorkTimer > t.F("vehicles.aiStuckSeconds")) { ai.StuckTimer = -t.F("vehicles.aiReverseSeconds"); ai.WorkTimer = 0f; return false; }
            // slope refusal: operators do not drive onto pitches above their rating
            float grade = ctx.Terrain.GradeAlongDeg(v.Pos.X, v.Pos.Y, to);
            if (MathF.Abs(grade) > maxSlopeDeg + t.F("vehicles.aiSlopeMarginDeg") && ai.Mode != AiMode.Refuel)
            {
                input.Throttle = 0f; input.Brake = 1f;
                ai.Phase = "refusing slope " + MathF.Round(MathF.Abs(grade)) + " deg";
                ai.WorkTimer += dt;
                if (ai.WorkTimer > 8f) { ai.Mode = AiMode.Idle; ai.Phase = "refused slope"; }
                return false;
            }
            // steer proportional to heading error; slow down for sharp turns and near the end
            float steer = MathUtil.Clamp(-err / 0.6f, -1f, 1f);
            input.Steer = steer;
            float turnSlow = 1f - 0.7f * MathUtil.Clamp01(MathF.Abs(err) / 1.2f);
            float endSlow = last ? MathUtil.Clamp(dist / 12f, 0.25f, 1f) : 1f;
            float throttle = MathF.Abs(err) > 2.4f ? 0.25f : 1f;
            input.Throttle = throttle * turnSlow * endSlow * MathUtil.Lerp(0.75f, 1f, competence);
            return false;
        }

        private static bool PassedWaypoint(VehicleState v, List<Vec2> route, int idx)
        {
            if (idx + 1 >= route.Count) return false;
            Vec2 a = route[idx], b = route[idx + 1];
            Vec2 seg = b - a;
            if (seg.SqrLength < 1e-4f) return true;
            // passed the plane through the waypoint perpendicular to the next leg
            return Vec2.Dot(v.Pos - a, seg) > 0f && Vec2.Distance(v.Pos, a) < 12f;
        }

        // ------------------------------------------------------------------ grooming
        private void Groom(SimContext ctx, VehicleSystem vs, VehicleState v, VehicleDef def, float competence, float maxSlopeDeg, float dt)
        {
            var ai = v.Ai;
            var input = v.Input;
            var piste = ctx.World.Pistes.Piste(ai.TargetId);
            if (piste == null) { ai.Mode = AiMode.Idle; return; }
            if (ai.LaneCount == 0)
            {
                float width = MathF.Max(1.5f, vs.WorkingWidthM(ctx, v, VehicleRole.Groom));
                float lane = width * (1f - ctx.Tuning.F("vehicles.aiLaneOverlapFrac"));
                ai.LaneCount = System.Math.Max(1, (int)MathF.Ceiling(piste.WidthM / lane));
                ai.Lane = 0;
                ai.Uphill = true;
                ai.Phase = "driving to " + piste.Name;
                // route to the bottom of the run first
                ai.Route = vs.FindRoute(ctx, v.Pos, piste.Points[piste.Points.Count - 1]);
                ai.RouteIndex = 0;
                ai.Loaded = false; // Loaded = lane route active
            }
            if (!ai.Loaded)
            {
                if (FollowRoute(ctx, v, def, competence, maxSlopeDeg, dt)) { BuildLaneRoute(ctx, v, piste); }
                return;
            }
            // on a lane: tiller down, blade up (blade down on the downhill lanes to shave moguls)
            input.Tiller = true;
            input.BladeLift = ai.Uphill ? 1f : -1f;
            bool done = FollowRoute(ctx, v, def, competence, maxSlopeDeg, dt);
            // grooming speed: working range scaled by competence
            float work = def.WorkingSpeedKmh.Max / MathF.Max(1f, def.TopSpeedKmh);
            input.Throttle = MathF.Min(input.Throttle, work * MathUtil.Lerp(0.8f, 1f, competence)) ;
            if (done)
            {
                ai.Lane++;
                if (ai.Lane >= ai.LaneCount)
                {
                    ai.Phase = "finished " + piste.Name;
                    vs.OnGroomFinished(ctx, v, piste);
                    ai.LaneCount = 0;
                    ai.Loaded = false;
                    ai.Mode = AiMode.ReturnToBase;
                    ai.Route = vs.FindRoute(ctx, v.Pos, ctx.Sim.Scenario.Landmark(ctx.Sim.Scenario.GarageLandmarkId).Pos);
                    ai.RouteIndex = 0;
                    return;
                }
                ai.Uphill = !ai.Uphill;
                BuildLaneRoute(ctx, v, piste);
            }
        }

        private void BuildLaneRoute(SimContext ctx, VehicleState v, PisteState piste)
        {
            var ai = v.Ai;
            float width = piste.WidthM;
            float laneW = width / ai.LaneCount;
            float offset = -width * 0.5f + (ai.Lane + 0.5f) * laneW;
            _tmp.Clear();
            var pts = piste.Points;
            for (int i = 0; i < pts.Count; i++)
            {
                Vec2 dir;
                if (i == 0) dir = (pts[1] - pts[0]).Normalized;
                else if (i == pts.Count - 1) dir = (pts[i] - pts[i - 1]).Normalized;
                else dir = ((pts[i] - pts[i - 1]).Normalized + (pts[i + 1] - pts[i]).Normalized).Normalized;
                Vec2 left = dir.Perp; // +lateral is left of downhill travel (matches SnowGrid.Lateral sign)
                _tmp.Add(pts[i] + left * offset);
            }
            ai.Route = new List<Vec2>();
            if (ai.Uphill) for (int i = _tmp.Count - 1; i >= 0; i--) ai.Route.Add(_tmp[i]);
            else ai.Route.AddRange(_tmp);
            ai.RouteIndex = 0;
            ai.Loaded = true;
            ai.Phase = "grooming " + piste.Name + " lane " + (ai.Lane + 1) + "/" + ai.LaneCount + (ai.Uphill ? " up" : " down");
        }

        // ------------------------------------------------------------------ zone passes
        private void ZoneWork(SimContext ctx, VehicleSystem vs, VehicleState v, VehicleDef def, float competence, float maxSlopeDeg, float dt)
        {
            var ai = v.Ai;
            var input = v.Input;
            var zone = ctx.World.Pistes.Zone(ai.TargetId);
            if (zone == null) { ai.Mode = AiMode.Idle; return; }
            if (ai.LaneCount == 0)
            {
                VehicleRole role = ai.Mode == AiMode.PlowZone ? VehicleRole.Plow : (ai.Mode == AiMode.BlowZone ? VehicleRole.Blow : VehicleRole.Spread);
                float width = MathF.Max(1.5f, vs.WorkingWidthM(ctx, v, role));
                float laneW = width * (1f - ctx.Tuning.F("vehicles.aiLaneOverlapFrac"));
                if (zone.Kind == ZoneKind.Road || zone.Kind == ZoneKind.CatTrack) ai.LaneCount = System.Math.Max(1, (int)MathF.Ceiling(zone.WidthM / laneW));
                else ai.LaneCount = System.Math.Max(1, (int)MathF.Ceiling(zone.RadiusM * 2f / laneW));
                ai.Lane = 0; ai.Uphill = true; ai.Loaded = false;
                ai.Route = vs.FindRoute(ctx, v.Pos, ZoneStart(zone, ai.Lane, ai.LaneCount, true));
                ai.RouteIndex = 0;
                ai.Phase = "driving to " + zone.Id;
            }
            if (!ai.Loaded)
            {
                if (FollowRoute(ctx, v, def, competence, maxSlopeDeg, dt)) BuildZoneLane(zone, v);
                return;
            }
            if (ai.Mode == AiMode.PlowZone) input.BladeLift = -1f;
            else input.Implement = true;
            if (ai.Mode == AiMode.BlowZone) input.BladeLift = -1f;
            bool done = FollowRoute(ctx, v, def, competence, maxSlopeDeg, dt);
            input.Throttle = MathF.Min(input.Throttle, MathF.Max(0.3f, def.WorkingSpeedKmh.Max / MathF.Max(1f, def.TopSpeedKmh)));
            if (done)
            {
                ai.Lane++;
                if (ai.Lane >= ai.LaneCount)
                {
                    ai.Phase = "finished " + zone.Id;
                    vs.OnZoneFinished(ctx, v, zone);
                    ai.LaneCount = 0; ai.Loaded = false;
                    ai.Mode = AiMode.ReturnToBase;
                    ai.Route = vs.FindRoute(ctx, v.Pos, ctx.Sim.Scenario.Landmark(ctx.Sim.Scenario.GarageLandmarkId).Pos);
                    ai.RouteIndex = 0;
                    return;
                }
                ai.Uphill = !ai.Uphill;
                BuildZoneLane(zone, v);
            }
        }

        private static Vec2 ZoneStart(SurfaceZone zone, int lane, int lanes, bool forward)
        {
            if (zone.Kind == ZoneKind.Road || zone.Kind == ZoneKind.CatTrack)
            {
                var pts = zone.Points;
                return forward ? pts[0] : pts[pts.Count - 1];
            }
            Vec2 c = zone.Center;
            float laneW = zone.RadiusM * 2f / lanes;
            float x = -zone.RadiusM + (lane + 0.5f) * laneW;
            float half = MathF.Sqrt(MathF.Max(0f, zone.RadiusM * zone.RadiusM - x * x));
            return c + new Vec2(x, forward ? -half : half);
        }

        private void BuildZoneLane(SurfaceZone zone, VehicleState v)
        {
            var ai = v.Ai;
            ai.Route = new List<Vec2>();
            if (zone.Kind == ZoneKind.Road || zone.Kind == ZoneKind.CatTrack)
            {
                float laneW = zone.WidthM / ai.LaneCount;
                float offset = -zone.WidthM * 0.5f + (ai.Lane + 0.5f) * laneW;
                var pts = zone.Points;
                _tmp.Clear();
                for (int i = 0; i < pts.Count; i++)
                {
                    Vec2 dir = i < pts.Count - 1 ? (pts[i + 1] - pts[i]).Normalized : (pts[i] - pts[i - 1]).Normalized;
                    _tmp.Add(pts[i] + dir.Perp * offset);
                }
                if (ai.Uphill) ai.Route.AddRange(_tmp); else for (int i = _tmp.Count - 1; i >= 0; i--) ai.Route.Add(_tmp[i]);
            }
            else
            {
                Vec2 a = ZoneStart(zone, ai.Lane, ai.LaneCount, ai.Uphill);
                Vec2 b = ZoneStart(zone, ai.Lane, ai.LaneCount, !ai.Uphill);
                ai.Route.Add(a); ai.Route.Add(b);
            }
            ai.RouteIndex = 0;
            ai.Loaded = true;
            ai.Phase = "working " + zone.Id + " pass " + (ai.Lane + 1) + "/" + ai.LaneCount;
        }

        // ------------------------------------------------------------------ hauling
        private void Haul(SimContext ctx, VehicleSystem vs, VehicleState v, VehicleDef def, float competence, float maxSlopeDeg, float dt)
        {
            var ai = v.Ai;
            var input = v.Input;
            float capacity = SnowContact.TruckCapacityKg(ctx, v);
            bool selfLoading = vs.HasRole(ctx, v, VehicleRole.Load);
            if (ai.Phase == "" || ai.Route == null || ai.Route.Count == 0)
            {
                ai.Route = vs.FindRoute(ctx, v.Pos, ai.LoadAt); ai.RouteIndex = 0; ai.Phase = "to load site"; ai.Loaded = false;
            }
            if (ai.Phase == "to load site")
            {
                if (FollowRoute(ctx, v, def, competence, maxSlopeDeg, dt)) { ai.Phase = "loading"; ai.WorkTimer = 0f; }
                return;
            }
            if (ai.Phase == "loading")
            {
                input.Throttle = 0f; input.Brake = 1f;
                if (selfLoading)
                {
                    // scoop cycle: bucket down + work to scoop, then raise + work to dump into own bed is meaningless;
                    // a self-loading hauler fills itself directly from the ground here.
                    input.Work = true; input.BladeLift = -1f;
                    vs.SelfLoad(ctx, v, dt);
                }
                else
                {
                    input.Work = true; // signals loaders nearby
                }
                ai.WorkTimer += dt;
                if (v.CargoKg >= capacity * 0.95f || (ai.WorkTimer > 240f && v.CargoKg > capacity * 0.3f))
                {
                    ai.Route = vs.FindRoute(ctx, v.Pos, ai.DeliverTo); ai.RouteIndex = 0; ai.Phase = "to dump site";
                }
                else if (ai.WorkTimer > 600f) { ai.Phase = "waiting for loader"; }
                return;
            }
            if (ai.Phase == "waiting for loader")
            {
                input.Throttle = 0f; input.Brake = 1f; input.Work = true;
                if (v.CargoKg >= capacity * 0.95f) { ai.Route = vs.FindRoute(ctx, v.Pos, ai.DeliverTo); ai.RouteIndex = 0; ai.Phase = "to dump site"; }
                return;
            }
            if (ai.Phase == "to dump site")
            {
                if (FollowRoute(ctx, v, def, competence, maxSlopeDeg, dt)) { ai.Phase = "dumping"; ai.WorkTimer = 0f; }
                return;
            }
            if (ai.Phase == "dumping")
            {
                input.Throttle = 0f; input.Brake = 1f; input.Work = true; input.BladeLift = 1f;
                float before = v.CargoKg;
                ai.WorkTimer += dt;
                if (v.CargoKg <= 0.5f || ai.WorkTimer > 60f)
                {
                    vs.OnHaulDelivered(ctx, v, before);
                    if (vs.HaulRemaining(ctx, v) <= 0f)
                    {
                        ai.Mode = AiMode.ReturnToBase; ai.Phase = "done hauling";
                        ai.Route = vs.FindRoute(ctx, v.Pos, ctx.Sim.Scenario.Landmark(ctx.Sim.Scenario.GarageLandmarkId).Pos); ai.RouteIndex = 0;
                    }
                    else { ai.Route = vs.FindRoute(ctx, v.Pos, ai.LoadAt); ai.RouteIndex = 0; ai.Phase = "to load site"; }
                }
            }
        }
    }
}
