using System;
using AlpineSim.Core.Data;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;
using AlpineSim.Core.Snow;

namespace AlpineSim.Core.Vehicles
{
    /// <summary>Per-tick physical context of one machine (filled by the vehicle system before stepping).</summary>
    public struct DriveContext
    {
        public float TotalMassKg;
        public float PowerAvailW;
        public float TractionCoeff;     // mu after wear
        public float ShearKpa;          // snow shear strength under the tracks
        public float GroundPressureKpa;
        public float LooseDepthM;
        public float GradeAlongRad;     // + uphill along heading
        public float SideSlopeRad;      // + downhill to the right
        public float ImplementDragN;    // tiller / blade / blower resistance
        public float BladeLoadKg;
        public float ImplementPowerW;   // hydraulic draw of engaged implements
        public bool Chains;
        public float Dt;
    }

    /// <summary>
    /// Driving models per chassis type. Arcade-sim: forgiving with a keyboard, punishing on a 32
    /// degree sidehill in fresh snow. All coefficients from tuning "vehicles.*".
    /// Sets Speed, YawRate, Heading, Pos, SlipFrac, LoadFrac, Sinkage, TrackSpeedL/R, SteerAngle.
    /// </summary>
    public static class DrivingModels
    {
        public static void Step(SimContext ctx, VehicleState v, VehicleDef def, ref DriveContext dc)
        {
            switch (def.ChassisType)
            {
                case ChassisType.Tracked: StepTracked(ctx, v, def, ref dc); break;
                case ChassisType.Wheeled: StepWheeled(ctx, v, def, ref dc, false); break;
                case ChassisType.Artic: StepWheeled(ctx, v, def, ref dc, true); break;
                case ChassisType.WalkBehind: StepWalkBehind(ctx, v, def, ref dc); break;
                default: v.Speed = 0f; v.YawRate = 0f; break;
            }
        }

        private static void Longitudinal(SimContext ctx, VehicleState v, VehicleDef def, ref DriveContext dc, float maxSpeed, float tractionScale)
        {
            var t = ctx.Tuning;
            float g = t.F("vehicles.gravity");
            float m = dc.TotalMassKg;
            float dt = dc.Dt;
            float throttle = MathUtil.Clamp(v.Input.Throttle, -1f, 1f);
            if (!v.EngineOn || v.Stranded) throttle = 0f;
            float cosG = MathF.Cos(dc.GradeAlongRad), sinG = MathF.Sin(dc.GradeAlongRad);
            // sinkage: deep loose snow sinks tracks when pressure approaches shear strength
            float ratio = dc.ShearKpa > 0.01f ? dc.GroundPressureKpa / dc.ShearKpa : 5f;
            v.Sinkage = MathF.Min(t.F("vehicles.sinkageMaxM"), dc.LooseDepthM * t.F("vehicles.sinkageFactor") * MathUtil.Clamp01(ratio));
            float rr = t.F("vehicles.rollingResistanceBase") + t.F("vehicles.rollingResistancePerSinkageM") * v.Sinkage;
            float normal = m * g * cosG;
            float fRoll = rr * normal;
            float fGrav = m * g * sinG;                       // positive = resists forward (uphill)
            float fBlade = dc.BladeLoadKg * g * (MathF.Max(0f, sinG) + t.F("vehicles.bladeFrictionCoeff"));
            float fDrag = dc.ImplementDragN + (v.Input.Throttle >= 0f ? fBlade : 0f);
            // desired speed by throttle
            float target = throttle * maxSpeed;
            float speed = v.Speed;
            // power-limited drive force toward target
            float eff = t.F("vehicles.driveEfficiency");
            float pAvail = MathF.Max(0f, dc.PowerAvailW - dc.ImplementPowerW) * eff;
            float fTractionMax = dc.TractionCoeff * tractionScale * normal * MathUtil.Clamp01(1f - ratio * 0.35f);
            float fDrive = 0f;
            if (MathF.Abs(throttle) > 0.01f)
            {
                float dir = target > speed ? 1f : -1f;
                float fPower = pAvail / MathF.Max(0.6f, MathF.Abs(speed));
                // hydrostatic: full force at low speed, limited by power at speed
                fDrive = dir * MathF.Min(fPower, m * 3.5f);
                // do not overshoot the target speed
                float needed = (target - speed) / dt * m;
                if (MathF.Abs(needed) < MathF.Abs(fDrive)) fDrive = needed;
            }
            float demand = MathF.Abs(fDrive);
            v.SlipFrac = 0f;
            if (demand > fTractionMax)
            {
                v.SlipFrac = MathUtil.Clamp01((demand - fTractionMax) / demand);
                fDrive = MathF.Sign(fDrive) * fTractionMax;
            }
            float resist = (fRoll + fDrag) * (speed > 0.02f ? 1f : (speed < -0.02f ? -1f : 0f));
            float net = fDrive - resist - fGrav;
            // braking / coasting
            if (MathF.Abs(throttle) < 0.01f || v.Input.Brake > 0.01f)
            {
                float decel = v.Input.Brake > 0.01f ? t.F("vehicles.brakeDecelMs2") * v.Input.Brake : t.F("vehicles.coastDecelMs2");
                float brakeF = MathF.Min(decel * m, fTractionMax + fRoll);
                if (MathF.Abs(speed) > 0.01f) net -= MathF.Sign(speed) * brakeF;
                else if (MathF.Abs(fGrav) < brakeF + fRoll) { v.Speed = 0f; net = 0f; }
            }
            speed += net / m * dt;
            // holding on a slope: static friction keeps a stopped machine still if it can
            if (MathF.Abs(throttle) < 0.01f && MathF.Abs(speed) < 0.05f && MathF.Abs(fGrav) < fTractionMax + fRoll) speed = 0f;
            float cap = maxSpeed * 1.15f;
            v.Speed = MathUtil.Clamp(speed, -cap, cap);
            v.LoadFrac = pAvail > 1f ? MathUtil.Clamp01((MathF.Abs(fDrive * v.Speed) + dc.ImplementPowerW) / MathF.Max(1f, dc.PowerAvailW)) : 0f;
            if (v.EngineOn && v.LoadFrac < 0.08f && (MathF.Abs(throttle) > 0.1f || v.Input.Tiller || v.Input.Implement)) v.LoadFrac = 0.08f;
        }

        private static void Lateral(SimContext ctx, VehicleState v, ref DriveContext dc, out float lateralSpeed)
        {
            // side-slope sliding when the sidehill exceeds what friction holds
            var t = ctx.Tuning;
            float g = t.F("vehicles.gravity");
            float sinS = MathF.Sin(dc.SideSlopeRad), cosS = MathF.Cos(dc.SideSlopeRad);
            float slide = g * (MathF.Abs(sinS) - dc.TractionCoeff * cosS * 0.9f);
            lateralSpeed = 0f;
            if (slide > 0f)
            {
                // steady slide speed limited by damping
                lateralSpeed = MathF.Sign(sinS) * slide / t.F("vehicles.sideSlipDamping");
            }
        }

        private static void Integrate(VehicleState v, float lateralSpeed, float dt)
        {
            var fwd = Vec2.FromAngle(v.Heading);
            var right = new Vec2(fwd.Y, -fwd.X);
            v.Pos += fwd * (v.Speed * dt) + right * (lateralSpeed * dt);
            v.Heading = MathUtil.WrapAngle(v.Heading + v.YawRate * dt);
        }

        private static void StepTracked(SimContext ctx, VehicleState v, VehicleDef def, ref DriveContext dc)
        {
            var t = ctx.Tuning;
            bool working = v.Input.Tiller || (v.Input.Implement && def.Category != VehicleCategory.Light);
            float maxSpeed = (working ? def.WorkingSpeedKmh.Max : def.TopSpeedKmh) / 3.6f;
            Longitudinal(ctx, v, def, ref dc, maxSpeed, 1f);
            float steer = MathUtil.Clamp(v.Input.Steer, -1f, 1f);
            if (!v.EngineOn || v.Stranded) steer = 0f;
            float spin = t.F("vehicles.spinInPlaceYawRadS");
            float radius = MathF.Max(1.5f, def.TurningRadiusM);
            float yaw = steer * MathF.Min(t.F("vehicles.maxYawRadS"), spin + MathF.Abs(v.Speed) / radius);
            // steering authority drops with slip
            yaw *= 1f - 0.6f * v.SlipFrac;
            if (v.Speed < -0.1f) yaw = -yaw; // reversing: steering reverses like a skid steer
            v.YawRate = -yaw; // positive steer = turn right = clockwise = negative heading rate (heading is CCW)
            float w = MathF.Max(1.5f, def.Visual != null ? def.Visual.BodyW : 2.5f);
            v.TrackSpeedL = v.Speed + v.YawRate * w * 0.5f * -1f;
            v.TrackSpeedR = v.Speed - v.YawRate * w * 0.5f * -1f;
            Lateral(ctx, v, ref dc, out float lat);
            Integrate(v, lat, dc.Dt);
        }

        private static void StepWheeled(SimContext ctx, VehicleState v, VehicleDef def, ref DriveContext dc, bool articulated)
        {
            var t = ctx.Tuning;
            bool working = v.Input.Tiller || v.Input.Implement;
            float maxSpeed = (working ? def.WorkingSpeedKmh.Max : def.TopSpeedKmh) / 3.6f;
            float tractionScale = t.F("vehicles.tireTractionFactor") + (dc.Chains ? t.F("vehicles.chainsTractionBonus") : 0f);
            if (def.TrackWidthM > 0f) tractionScale = 1f; // track conversions
            Longitudinal(ctx, v, def, ref dc, maxSpeed, tractionScale);
            float steer = MathUtil.Clamp(v.Input.Steer, -1f, 1f);
            if (!v.EngineOn || v.Stranded) steer = 0f;
            float bodyL = def.Visual != null ? def.Visual.BodyL : 6f;
            float wheelbase = MathF.Max(1.5f, bodyL * t.F("vehicles.wheelbaseFracOfBody"));
            float maxSteer = articulated ? t.F("vehicles.articulationMaxDeg") * MathUtil.Deg2Rad : MathF.Atan(wheelbase / MathF.Max(2f, def.TurningRadiusM));
            float targetAngle = steer * maxSteer;
            float rate = 1.5f;
            v.SteerAngle = MathUtil.MoveTowards(v.SteerAngle, targetAngle, rate * dc.Dt);
            float effWheelbase = articulated ? wheelbase * 0.5f : wheelbase;
            float yaw = v.Speed * MathF.Tan(v.SteerAngle) / effWheelbase;
            yaw = MathUtil.Clamp(yaw, -t.F("vehicles.maxYawRadS"), t.F("vehicles.maxYawRadS"));
            v.YawRate = -yaw;
            v.TrackSpeedL = v.Speed; v.TrackSpeedR = v.Speed;
            Lateral(ctx, v, ref dc, out float lat);
            Integrate(v, lat, dc.Dt);
        }

        private static void StepWalkBehind(SimContext ctx, VehicleState v, VehicleDef def, ref DriveContext dc)
        {
            var t = ctx.Tuning;
            float maxSpeed = MathF.Min(t.F("vehicles.walkBehindSpeedMs"), def.TopSpeedKmh / 3.6f);
            float throttle = v.EngineOn && !v.Stranded ? MathUtil.Clamp(v.Input.Throttle, -1f, 1f) : 0f;
            v.Speed = MathUtil.MoveTowards(v.Speed, throttle * maxSpeed, 2f * dc.Dt);
            float steer = v.EngineOn ? MathUtil.Clamp(v.Input.Steer, -1f, 1f) : 0f;
            v.YawRate = -steer * t.F("vehicles.walkBehindYawRadS");
            v.SlipFrac = 0f;
            v.Sinkage = MathF.Min(0.2f, dc.LooseDepthM * 0.5f);
            v.LoadFrac = v.Input.Implement ? 0.8f : (MathF.Abs(throttle) > 0.1f ? 0.3f : 0.05f);
            v.TrackSpeedL = v.Speed; v.TrackSpeedR = v.Speed;
            Integrate(v, 0f, dc.Dt);
        }
    }
}
