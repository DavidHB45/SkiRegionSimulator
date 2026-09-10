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
            if (v.Stranded) { if (ai.Mode != AiMode.Stranded) { ai.Mode = AiMode.Stranded; vs.ReleaseJob(ctx, v); } return; }
            var t = ctx.Tuning;
            input.Lights = true;

            // low fuel: go home (unless the depot was found dry a moment ago: then work on what is in the tank)
            float reserve = t.F("vehicles.fuelReserveWarningFrac");
            if (ai.FuelDeniedTimer > 0f) ai.FuelDeniedTimer -= dt;
            if (ai.Mode != AiMode.Refuel && ai.Mode != AiMode.ReturnToBase && ai.FuelDeniedTimer <= 0f && v.Fuel < def.EnergyCapacity * reserve && def.FuelType != FuelType.None)
            {
                ai.Phase = "low fuel: returning to depot";
                ai.JobMode = ai.Mode;
                var depot = ctx.Sim.Scenario.Landmark(ctx.Sim.Scenario.FuelLandmarkId).Pos;
                ai.Route = vs.FindRoute(ctx, v, depot);
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
                        else if (ai.Mode == AiMode.Refuel)
                        {
                            if (vs.ArrivedAtDepot(ctx, v))
                            {
                                // tank full: back to the job it left, or park if it had none
                                if (!vs.ResumeJob(ctx, v)) { ai.Mode = AiMode.Idle; ai.Phase = "refuelled, parked"; vs.OnAiIdle(ctx, v); }
                            }
                            else
                            {
                                // the depot is dry: carry on with the job on what is in the tank and try again later
                                ai.FuelDeniedTimer = t.F("vehicles.aiDryDepotRetrySeconds");
                                ctx.Sim.Log(v.Name + ": no fuel at the depot, carrying on with " + MathF.Round(v.Fuel) + " L in the tank.", LogLevel.Warning);
                                if (!vs.ResumeJob(ctx, v)) { ai.Mode = AiMode.Idle; ai.Phase = "waiting for fuel"; vs.OnAiIdle(ctx, v); }
                            }
                        }
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
            // pure pursuit on the path: aim at the point lookahead metres ahead of the machine's projection onto the
            // current leg, so a machine that is off the line steers back onto it within a couple of lookaheads
            // (aiming at the waypoint itself let a plow wander thirteen metres off its lane for eighty metres)
            float look = t.F("vehicles.aiLookaheadM");
            Vec2 aim = target;
            if (ai.RouteIndex >= 1)
            {
                Vec2 legA = ai.Route[ai.RouteIndex - 1], legAb = target - legA;
                float legLen2 = legAb.SqrLength;
                if (legLen2 > 1e-4f)
                {
                    float u = MathUtil.Clamp01(Vec2.Dot(v.Pos - legA, legAb) / legLen2);
                    Vec2 foot = legA + legAb * u;
                    float legLen = MathF.Sqrt(legLen2);
                    float ahead = MathF.Min(legLen, u * legLen + look);
                    aim = legA + legAb * (ahead / legLen);
                }
            }
            if (Vec2.Distance(v.Pos, target) < look && ai.RouteIndex + 1 < ai.Route.Count)
                aim = Vec2.Lerp(target, ai.Route[ai.RouteIndex + 1], MathUtil.Clamp01((look - Vec2.Distance(v.Pos, target)) / MathF.Max(1f, Vec2.Distance(target, ai.Route[ai.RouteIndex + 1]))));
            Vec2 to = aim - v.Pos;
            if (to.SqrLength < 1f) to = target - v.Pos;
            float dist = Vec2.Distance(v.Pos, target);
            bool last = ai.RouteIndex == ai.Route.Count - 1;
            if (dist < reach || (!last && PassedWaypoint(v, ai.Route, ai.RouteIndex)))
            {
                ai.RouteIndex++;
                ai.StuckCount = 0;
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
            // a speed the machine can still stop or turn from: braking distance to the end of the route or to a
            // sharp corner, and never above the AI transit cap (a pickup arriving at a plow lane at 60 km/h swung
            // ten metres wide and plowed the meadow beside the road)
            float corner = 100f;
            if (last) corner = 1.5f;
            else
            {
                Vec2 legA = (target - v.Pos).Normalized, legB = (ai.Route[ai.RouteIndex + 1] - target).Normalized;
                float bend = MathF.Abs(MathUtil.DeltaAngle(legA.Angle, legB.Angle));
                if (bend > 0.5f) corner = MathUtil.Lerp(8f, 2f, MathUtil.Clamp01((bend - 0.5f) / 2f));
            }
            float vmax = MathF.Min(t.F("vehicles.aiTransitMaxKmh") / 3.6f, MathF.Sqrt(2f * 2.5f * MathF.Max(0f, dist - 2f)) + corner);
            bool braking = v.Speed > vmax;
            // stuck = engine running, neither moving nor turning (a tracked machine pivoting in place is not stuck;
            // a wheeled one that cannot turn on a bank is); a machine being held back by its own brake is not stuck either
            bool moving = MathF.Abs(v.Speed) >= 0.15f || MathF.Abs(v.YawRate) >= 0.03f;
            if (!moving && v.EngineOn && !braking) ai.WorkTimer += dt; else ai.WorkTimer = 0f;
            if (ai.WorkTimer > t.F("vehicles.aiStuckSeconds"))
            {
                ai.WorkTimer = 0f;
                ai.StuckCount++;
                if (ai.StuckCount >= (int)t.F("vehicles.aiGiveUpAfterStucks"))
                {
                    // the machine cannot make this pitch (traction, not nerve): same outcome as a refusal
                    ai.StuckCount = 0;
                    float g = ctx.Terrain.GradeAlongDeg(v.Pos.X, v.Pos.Y, to, MathF.Max(3f, def.Visual != null ? def.Visual.BodyL : 3f));
                    if (ai.Mode == AiMode.GroomPiste && ai.Loaded) { ai.LanesSkipped++; ai.LaneAbandoned = true; return true; }
                    if (ai.Mode == AiMode.ReturnToBase || ai.Mode == AiMode.Refuel)
                    {
                        // stuck on the way home: park here with the engine off rather than loop between refusing and returning,
                        // and hand any job back so the board does not carry a task nobody is working
                        ai.Mode = AiMode.Idle; ai.Phase = "stuck on the way back"; ai.ParkedStuckTick = ctx.Time.Tick;
                        ctx.Sim.Log(v.Name + " is stuck on the way back (" + MathF.Round(MathF.Abs(g)) + " deg pitch at " + MathF.Round(v.Pos.X) + "," + MathF.Round(v.Pos.Y) + ") and parked where it is.", LogLevel.Warning);
                        var vsys = ctx.System<VehicleSystem>();
                        vsys.ReleaseJob(ctx, v);
                        vsys.OnAiIdle(ctx, v);
                        return false;
                    }
                    ai.Mode = AiMode.Idle; ai.Phase = "stuck";
                    ctx.System<VehicleSystem>().OnAiRefused(ctx, v, "cannot climb the " + MathF.Round(MathF.Abs(g)) + " deg pitch");
                    return false;
                }
                ai.StuckTimer = -t.F("vehicles.aiReverseSeconds");
                return false;
            }
            // slope refusal: operators do not drive onto pitches above their rating, nor above what the machine is rated for
            float grade = ctx.Terrain.GradeAlongDeg(v.Pos.X, v.Pos.Y, to, MathF.Max(3f, def.Visual != null ? def.Visual.BodyL : 3f));
            float slopeLimit = MathF.Min(maxSlopeDeg, def.MaxGradeDeg);
            // a machine heading for the depot or home takes the pitch it came over: refusing there means never arriving;
            // and only a climb is refused: a driver already on a pitch can always let the machine down it
            if (grade > slopeLimit + t.F("vehicles.aiSlopeMarginDeg") && ai.Mode != AiMode.Refuel && ai.Mode != AiMode.ReturnToBase)
            {
                input.Throttle = 0f; input.Brake = 1f;
                ai.Phase = "refusing slope " + MathF.Round(MathF.Abs(grade)) + " deg";
                ai.RefuseTimer += dt;
                ai.WorkTimer = 0f;
                if (ai.RefuseTimer > 8f)
                {
                    ai.RefuseTimer = 0f;
                    // a groomer leaves the pitch it cannot take and carries on with the next lane from this height (the part
                    // of the run below the pitch still gets groomed; the part above waits for a winch cat); anyone else gives the job back
                    if (ai.Mode == AiMode.GroomPiste && ai.Loaded) { ai.LanesSkipped++; ai.LaneAbandoned = true; return true; }
                    ai.Mode = AiMode.Idle; ai.Phase = "refused slope";
                    ctx.System<VehicleSystem>().OnAiRefused(ctx, v, "slope " + MathF.Round(MathF.Abs(grade)) + " deg is above the operator's rating");
                }
                return false;
            }
            // refusals decay slowly so a pitch that keeps rolling the machine back still adds up to a refusal
            ai.RefuseTimer = MathF.Max(0f, ai.RefuseTimer - dt * 0.25f);
            // steer proportional to heading error; slow down for sharp turns and near the end
            float steer = MathUtil.Clamp(-err / 0.6f, -1f, 1f);
            input.Steer = steer;
            float turnSlow = 1f - 0.7f * MathUtil.Clamp01(MathF.Abs(err) / 1.2f);
            float endSlow = last ? MathUtil.Clamp(dist / 12f, 0.25f, 1f) : 1f;
            float throttle = MathF.Abs(err) > 2.4f ? 0.25f : 1f;
            input.Throttle = throttle * turnSlow * endSlow * MathUtil.Lerp(0.75f, 1f, competence);
            if (braking) { input.Throttle = 0f; input.Brake = MathUtil.Clamp01((v.Speed - vmax) / 2f); }
            return false;
        }

        /// <summary>Lift command (-1 lower, 0 hold, +1 raise) that brings the mounted blade to the given pose.</summary>
        private static float BladeToward(SimContext ctx, VehicleState v, float pose)
        {
            for (int i = 0; i < v.Mounted.Count; i++)
            {
                var a = ctx.Data.Attachment(v.Mounted[i].DefId);
                if (a == null || a.Kind != AttachmentKind.Blade) continue;
                float lift = v.Mounted[i].Lift;
                return lift > pose + 0.02f ? -1f : (lift < pose - 0.02f ? 1f : 0f);
            }
            return 1f;
        }

        /// <summary>Throttle multiplier that lets a working machine pull with everything below its working speed and eases off above it.</summary>
        private static float WorkSpeedFactor(VehicleState v, float workKmh)
        {
            float work = MathF.Max(0.5f, workKmh) / 3.6f;
            return MathUtil.Clamp01(1.5f - MathF.Abs(v.Speed) / work);
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
                ai.LanesSkipped = 0;
                ai.LaneAbandoned = false;
                ai.Uphill = true;
                ai.Phase = "driving to " + piste.Name;
                // route to the bottom of the run first
                ai.Route = vs.FindRoute(ctx, v, piste.Points[piste.Points.Count - 1]);
                ai.RouteIndex = 0;
                ai.Loaded = false; // Loaded = lane route active
            }
            if (!ai.Loaded)
            {
                if (FollowRoute(ctx, v, def, competence, maxSlopeDeg, dt)) { BuildLaneRoute(ctx, v, piste); }
                return;
            }
            // on a lane: tiller down; blade up on the climb, floated shallow on the downhill lanes to shave moguls
            // (fully down it would strip the run to the ground in two passes)
            input.Tiller = true;
            input.BladeLift = ai.Uphill ? 1f : BladeToward(ctx, v, ctx.Tuning.F("vehicles.aiGroomBladeFloat"));
            bool done = FollowRoute(ctx, v, def, competence, maxSlopeDeg, dt);
            // grooming speed: hold the working speed by easing the throttle as the machine reaches it, never by
            // capping the throttle itself (a capped throttle stalls the tiller on the first real pitch)
            input.Throttle *= WorkSpeedFactor(v, def.WorkingSpeedKmh.Max * MathUtil.Lerp(0.8f, 1f, competence));
            if (done)
            {
                ai.Lane++;
                if (ai.Lane >= ai.LaneCount)
                {
                    ai.Phase = "finished " + piste.Name;
                    vs.OnGroomFinished(ctx, v, piste);
                    ai = v.Ai; // the job may have been handed back, which resets the AI state
                    ai.LaneCount = 0;
                    ai.Loaded = false;
                    ai.Mode = AiMode.ReturnToBase;
                    ai.Route = vs.FindRoute(ctx, v, ctx.Sim.Scenario.Landmark(ctx.Sim.Scenario.GarageLandmarkId).Pos);
                    ai.RouteIndex = 0;
                    return;
                }
                ai.Uphill = !ai.Uphill;
                BuildLaneRoute(ctx, v, piste, ai.LaneAbandoned);
                ai.LaneAbandoned = false;
            }
        }

        /// <summary>
        /// Lays out the current lane as a route along the run. With fromHere the route starts at the waypoint level
        /// with the machine instead of the far end of the run, which is how a lane continues after the one beside it
        /// was abandoned part-way up.
        /// </summary>
        private void BuildLaneRoute(SimContext ctx, VehicleState v, PisteState piste, bool fromHere = false)
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
            if (fromHere)
            {
                // continue from the waypoint nearest the machine, and never back toward the pitch it just left
                float best = float.MaxValue; int bi = 0;
                for (int i = 0; i < ai.Route.Count; i++) { float d = Vec2.SqrDistance(ai.Route[i], v.Pos); if (d < best) { best = d; bi = i; } }
                ai.RouteIndex = System.Math.Min(ai.Route.Count - 1, bi + 1);
            }
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
                // approach the first lane from a lead-in point behind its start, so the machine enters it aligned
                Vec2 start = ZoneStart(zone, ai.Lane, ai.LaneCount, true);
                Vec2 next = zone.Kind == ZoneKind.Road || zone.Kind == ZoneKind.CatTrack ? (zone.Points.Count > 1 ? zone.Points[1] : start) : ZoneStart(zone, ai.Lane, ai.LaneCount, false);
                Vec2 laneDir = (next - start).Normalized;
                ai.Route = vs.FindRoute(ctx, v, start - laneDir * LaneLeadInM);
                ai.Route.Add(start);
                ai.RouteIndex = 0;
                ai.Phase = "driving to " + zone.Id;
            }
            if (!ai.Loaded)
            {
                if (FollowRoute(ctx, v, def, competence, maxSlopeDeg, dt)) BuildZoneLane(zone, v);
                return;
            }
            // the implement goes down only once the lane's first waypoint is behind the machine: the approach crosses the
            // pile the previous pass left beyond the zone end, and a blade dropped there would drag it back in
            bool past = ai.RouteIndex >= 1;
            if (ai.Mode == AiMode.PlowZone)
            {
                input.BladeLift = past ? -1f : 1f;
                // angle the blade so the windrow lands on the side away from the zone's centreline: with a straight blade
                // half the spill fell back onto the other lane and a road could be plowed all day without ever clearing
                Vec2 foot = zone.Kind == ZoneKind.Road || zone.Kind == ZoneKind.CatTrack ? NearestOnPolyline(zone.Points, v.Pos) : zone.Center;
                float side = Vec2.Dot(v.Pos - foot, Vec2.FromAngle(v.Heading).Perp);
                input.BladeAngle = side >= 0f ? -1f : 1f;
            }
            else input.Implement = past;
            if (ai.Mode == AiMode.BlowZone) input.BladeLift = past ? -1f : 1f;
            bool done = FollowRoute(ctx, v, def, competence, maxSlopeDeg, dt);
            input.Throttle *= WorkSpeedFactor(v, def.WorkingSpeedKmh.Max);
            if (done)
            {
                ai.Lane++;
                if (ai.Lane >= ai.LaneCount)
                {
                    ai.Phase = "finished " + zone.Id;
                    vs.OnZoneFinished(ctx, v, zone);
                    ai.LaneCount = 0; ai.Loaded = false;
                    ai.Mode = AiMode.ReturnToBase;
                    ai.Route = vs.FindRoute(ctx, v, ctx.Sim.Scenario.Landmark(ctx.Sim.Scenario.GarageLandmarkId).Pos);
                    ai.RouteIndex = 0;
                    return;
                }
                ai.Uphill = !ai.Uphill;
                BuildZoneLane(zone, v);
            }
        }

        private static Vec2 NearestOnPolyline(List<Vec2> pts, Vec2 p)
        {
            if (pts == null || pts.Count == 0) return p;
            Vec2 best = pts[0]; float bd = float.MaxValue;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vec2 a = pts[i], ab = pts[i + 1] - a;
                float len2 = ab.SqrLength;
                float u = len2 > 1e-6f ? MathUtil.Clamp01(Vec2.Dot(p - a, ab) / len2) : 0f;
                Vec2 q = a + ab * u;
                float d = Vec2.SqrDistance(p, q);
                if (d < bd) { bd = d; best = q; }
            }
            return best;
        }

        /// <summary>
        /// Lane strips are worked from the centre outward so every pass pushes its windrow onto a strip that is still to
        /// be plowed, never back onto one already cleared.
        /// </summary>
        private static int CentreOutLane(int lane, int lanes)
        {
            int mid = lanes / 2;
            if (lane == 0) return mid;
            int k = 0, d = 1;
            while (d <= lanes)
            {
                if (mid + d < lanes) { k++; if (k == lane) return mid + d; }
                if (mid - d >= 0) { k++; if (k == lane) return mid - d; }
                d++;
            }
            return System.Math.Min(lane, lanes - 1);
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
            float x = -zone.RadiusM + (CentreOutLane(lane, lanes) + 0.5f) * laneW;
            float half = MathF.Sqrt(MathF.Max(0f, zone.RadiusM * zone.RadiusM - x * x));
            return c + new Vec2(x, forward ? -half : half);
        }

        /// <summary>Metres a plow lane runs on past the zone end so the blade clears the zone before it lifts.</summary>
        private const float LaneOverrunM = 5f;
        /// <summary>Metres behind a lane's start the approach ends, so the machine enters the lane already aligned.</summary>
        private const float LaneLeadInM = 12f;

        private void BuildZoneLane(SurfaceZone zone, VehicleState v)
        {
            var ai = v.Ai;
            ai.Route = new List<Vec2>();
            if (zone.Kind == ZoneKind.Road || zone.Kind == ZoneKind.CatTrack)
            {
                float laneW = zone.WidthM / ai.LaneCount;
                float offset = -zone.WidthM * 0.5f + (CentreOutLane(ai.Lane, ai.LaneCount) + 0.5f) * laneW;
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
            if (ai.Mode == AiMode.PlowZone && ai.Route.Count >= 2 && (zone.Kind == ZoneKind.Road || zone.Kind == ZoneKind.CatTrack))
            {
                Vec2 end = ai.Route[ai.Route.Count - 1];
                Vec2 dir = (end - ai.Route[ai.Route.Count - 2]).Normalized;
                ai.Route.Add(end + dir * LaneOverrunM);
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
                ai.Route = vs.FindRoute(ctx, v, ai.LoadAt); ai.RouteIndex = 0; ai.Phase = "to load site"; ai.Loaded = false;
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
                    ai.Route = vs.FindRoute(ctx, v, ai.DeliverTo); ai.RouteIndex = 0; ai.Phase = "to dump site";
                }
                else if (ai.WorkTimer > 600f) { ai.Phase = "waiting for loader"; }
                return;
            }
            if (ai.Phase == "waiting for loader")
            {
                input.Throttle = 0f; input.Brake = 1f; input.Work = true;
                if (v.CargoKg >= capacity * 0.95f) { ai.Route = vs.FindRoute(ctx, v, ai.DeliverTo); ai.RouteIndex = 0; ai.Phase = "to dump site"; }
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
                        ai.Route = vs.FindRoute(ctx, v, ctx.Sim.Scenario.Landmark(ctx.Sim.Scenario.GarageLandmarkId).Pos); ai.RouteIndex = 0;
                    }
                    else { ai.Route = vs.FindRoute(ctx, v, ai.LoadAt); ai.RouteIndex = 0; ai.Phase = "to load site"; }
                }
            }
        }
    }
}
