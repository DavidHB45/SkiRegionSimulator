using System;
using System.Collections.Generic;

namespace AlpineSim.Core.Vehicles
{
    public enum VehicleCategory { Groomer, Blower, Snowmaking, Tractor, Loader, Truck, RoadMaint, Light, Construction, Stationary }
    public enum ChassisType { Tracked, Wheeled, Artic, WalkBehind, Towed, Stationary }
    public enum Transmission { Hydrostatic, Powershift, Cvt, Manual, None }
    public enum FuelType { Diesel, Gasoline, Electric, HydraulicSlave, None }
    public enum SlotPosition { Front, Rear, Mid, Roof, Tow }
    public enum VehicleRole { Groom, Blow, MakeSnow, Plow, Spread, Haul, Load, Lift, Tow, Transport, Patrol, Construct, Service, Refuel, Rescue, Pump, Compress, Store, Repair }
    public enum OperatorLicense { Basic, Groomer, WinchCat, Crane, Cdl, Heavy, Sled }
    public enum WearSubsystem { Engine, Hydraulics, Drivetrain, TracksOrTires, Attachment }

    [Serializable] public sealed class TorquePoint { public float Rpm; public float TorqueNm; }
    [Serializable] public sealed class BurnRates { public float Idle; public float Transit; public float Working; public float HighLoad; }
    [Serializable] public sealed class SpeedRange { public float Min; public float Max; }
    [Serializable] public sealed class WearRates { public float Engine = 1f; public float Hydraulics = 1f; public float Drivetrain = 1f; public float TracksOrTires = 1f; public float Attachment = 1f;
        public float Get(WearSubsystem s) { switch (s) { case WearSubsystem.Engine: return Engine; case WearSubsystem.Hydraulics: return Hydraulics; case WearSubsystem.Drivetrain: return Drivetrain; case WearSubsystem.TracksOrTires: return TracksOrTires; default: return Attachment; } } }
    [Serializable] public sealed class TireSpec { public string Size = ""; public float WidthM = 0.4f; public int Count = 4; public bool Chains; public bool Duals; }

    [Serializable]
    public sealed class AttachmentSlot
    {
        public SlotPosition Position = SlotPosition.Front;
        public float MaxMassKg = 1000f;
        public bool RequiresHydraulics;
        public bool RequiresPto;
        /// <summary>Attachment mounted when the machine is delivered (may be empty).</summary>
        public string DefaultAttachmentId = "";
    }

    /// <summary>Procedural mesh recipe. The Unity layer builds primitives from these numbers; no art assets.</summary>
    [Serializable]
    public sealed class MeshRecipe
    {
        /// <summary>groomer | loader | truck | pickup | tractor | snowmobile | utv | walkbehind | excavator | crane | gun | station | sled | trailer | blower | grader | telehandler | tanker | mixer | lowboy | carrier</summary>
        public string Silhouette = "truck";
        public float BodyL = 6f;
        public float BodyW = 2.5f;
        public float BodyH = 1.4f;
        public float CabL = 1.8f;
        public float CabW = 2.2f;
        public float CabH = 1.3f;
        /// <summary>Cab centre offset along the body (metres, +forward).</summary>
        public float CabOffset = 1.5f;
        public float TrackH = 0.9f;
        public float WheelRadiusM = 0.5f;
        public int Axles = 2;
        public float BoomLengthM;
        public string ColorHex = "#c0392b";
        public string AccentHex = "#2c3e50";
    }

    /// <summary>
    /// One machine class (vehicles.json). Nothing about a machine is hard-coded: adding a 51st is a
    /// record plus a mesh recipe. Category-specific numbers (blower t/h, gun output curve, pump l/s,
    /// workshop tier...) live in <see cref="Specs"/> and <see cref="Curves"/>.
    /// </summary>
    [Serializable]
    public sealed class VehicleDef
    {
        public string Id = "";
        public string DisplayName = "";
        public VehicleCategory Category = VehicleCategory.Groomer;
        public int Tier;
        public ChassisType ChassisType = ChassisType.Tracked;
        public float MassKg = 8000f;
        public float EnginePowerKw = 200f;
        public List<TorquePoint> TorqueCurve = new List<TorquePoint>();
        public Transmission Transmission = Transmission.Hydrostatic;
        public FuelType FuelType = FuelType.Diesel;
        public float FuelCapacityL = 300f;
        public float BatteryKwh;
        public BurnRates BurnLPerHour = new BurnRates();
        public float TrackWidthM;
        public TireSpec TireSpec;
        public float GroundPressureKpa = 6f;
        public float MaxGradeDeg = 30f;
        public float MaxSideSlopeDeg = 20f;
        public float TopSpeedKmh = 20f;
        public SpeedRange WorkingSpeedKmh = new SpeedRange { Min = 6f, Max = 12f };
        public float TurningRadiusM = 4f;
        public float HydraulicFlowLpm;
        public bool Pto;
        public bool CabHeated = true;
        public float LightingLumens = 8000f;
        public int Seats = 1;
        public float CargoCapacityKg;
        public float BucketM3;
        public float TankL;
        public List<AttachmentSlot> AttachmentSlots = new List<AttachmentSlot>();
        public double PurchasePrice = 100000;
        public float UsedMarketMultiplier = 0.5f;
        public double LeaseMonthly;
        /// <summary>Resale fraction of purchase price by 1000-hour bands: [0h, 1000h, 2000h, ...].</summary>
        public float[] ResaleCurve = new float[0];
        public double HourlyOpCost = 20;
        public float ServiceIntervalHours = 250f;
        public float MtbfHours = 400f;
        public double InsuranceAnnual = 3000;
        public OperatorLicense OperatorLicense = OperatorLicense.Basic;
        public WearRates WearRates = new WearRates();
        public List<VehicleRole> Roles = new List<VehicleRole>();
        public MeshRecipe Visual = new MeshRecipe();
        /// <summary>
        /// Optional Resources path to a hand-authored model, e.g. "Art/Authored/Groomers/flagship".
        /// Tier 1 of the ModelRegistry ladder (docs/ART_CONTRACT.md section 9): set it and this machine
        /// stops using the generated model. Empty means the generated model, then the primitive one.
        /// </summary>
        public string ModelOverride = "";
        /// <summary>Category-specific scalars, e.g. blowerCapacityTph, throwDistanceM, winchRopeM, winchPullT, pumpLps, pumpHeadM, compressorM3Min, workshopTier, fuelStorageL, partsSlots, garageBays, plowWidthM, spreaderHopperM3, blowerHeadTph, gunPowerKw, gunReachM, coneHalfAngleDeg, waterLpm, workRate, sirenSpeedBonus.</summary>
        public Dictionary<string, float> Specs = new Dictionary<string, float>();
        /// <summary>Category-specific curves, e.g. gunOutputByWetBulb: xs = wet-bulb C, ys = m3 snow per hour.</summary>
        public Dictionary<string, CurveDef> Curves = new Dictionary<string, CurveDef>();
        public string Comment = "";

        public bool HasRole(VehicleRole r) => Roles.Contains(r);
        public float Spec(string key)
        {
            if (Specs != null && Specs.TryGetValue(key, out float v)) return v;
            throw new KeyNotFoundException("Vehicle '" + Id + "' has no spec '" + key + "' (add it to vehicles.json)");
        }
        public float SpecOr(string key, float fallback) => Specs != null && Specs.TryGetValue(key, out float v) ? v : fallback;
        public bool HasSpec(string key) => Specs != null && Specs.ContainsKey(key);
        public float Curve(string key, float x)
        {
            if (Curves != null && Curves.TryGetValue(key, out var c) && c.Xs != null && c.Xs.Length > 0) return Math.MathUtil.SampleCurve(c.Xs, c.Ys, x);
            throw new KeyNotFoundException("Vehicle '" + Id + "' has no curve '" + key + "'");
        }
        public bool HasCurve(string key) => Curves != null && Curves.ContainsKey(key);
        public AttachmentSlot Slot(SlotPosition p)
        {
            foreach (var s in AttachmentSlots) if (s.Position == p) return s;
            return null;
        }
        public bool IsStationary => ChassisType == ChassisType.Stationary;
        public bool IsElectric => FuelType == FuelType.Electric;
        /// <summary>Energy store capacity in the vehicle's fuel unit (L or kWh).</summary>
        public float EnergyCapacity => IsElectric ? BatteryKwh : FuelCapacityL;
        /// <summary>Resale fraction at a given hour meter (piecewise over ResaleCurve).</summary>
        public float ResaleFraction(float hours)
        {
            if (ResaleCurve == null || ResaleCurve.Length == 0) return 0.5f;
            var xs = new float[ResaleCurve.Length];
            for (int i = 0; i < xs.Length; i++) xs[i] = i * 1000f;
            return Math.MathUtil.SampleCurve(xs, ResaleCurve, hours);
        }
    }

    [Serializable]
    public sealed class CurveDef
    {
        public float[] Xs = new float[0];
        public float[] Ys = new float[0];
        public string Comment = "";
    }

    public enum AttachmentKind
    {
        Blade, Blade12Way, UBlade, VPlow, PlowWings, BoxPusher, Tiller, TrackSetter, ParkBlade, PipeCutter, Winch,
        BlowerHead, SnowBucket, LightBucket, Forks, Grapple, Broom, Auger, Spreader, BrineTank, Hitch, CableReel,
        TowerJib, SledHitch, LightTower, PumpSkid, Mulcher, PlowStraight
    }

    public static class AttachmentKindExtensions
    {
        /// <summary>The kinds SnowContact pushes snow with, so the one place that asks "is there a blade on the front" agrees with the one that cuts.</summary>
        public static bool IsBlade(this AttachmentKind kind)
        {
            switch (kind)
            {
                case AttachmentKind.Blade:
                case AttachmentKind.Blade12Way:
                case AttachmentKind.UBlade:
                case AttachmentKind.VPlow:
                case AttachmentKind.PlowStraight:
                case AttachmentKind.BoxPusher:
                case AttachmentKind.ParkBlade:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Effect coefficients an attachment applies to the snow grid or a job.</summary>
    [Serializable]
    public sealed class AttachmentEffects
    {
        public float CutDepthMm;            // blades: deepest snow taken in one pass (the moldboard's effective height)
        public float PushCapacityKg;        // blades: snow carried in front before spill
        public float TillTargetDensity;     // tillers: re-laid density
        public float TillEfficiency;        // tillers: fraction of the gap to target closed per pass
        public float TillCompaction;        // tillers: work-hardening increment per pass (kg/m3)
        public float FinishQuality;         // tillers/track setters: roughness removed (0..1)
        public float DepthRatingMm;         // tillers: max depth worked
        public float RemoveRateKgs;         // blowers/buckets: kg/s intake at rated speed
        public float ThrowDistanceM;        // blowers: chute throw
        public float CompactionKpa;         // rollers/setters: extra pressure
        public float SpreadRateKgPerLaneKm; // spreaders
        public float SpreadWidthM;
        public float BrineLPerM2;
        public float BucketM3;
        public float LiftCapacityKg;
        public float ReachM;
        public float RopeLengthM;
        public float PullKn;
        public float LumensBonus;
        public float WorkRate = 1f;         // generic task work-rate multiplier
        public float TrackGaugeM;           // nordic track setter
        public float ShapeDepthMm;          // park blade / pipe cutter
    }

    /// <summary>One attachment (attachments.json). Swapping costs shop time; it is not an inventory toggle.</summary>
    [Serializable]
    public sealed class AttachmentDef
    {
        public string Id = "";
        public string DisplayName = "";
        public AttachmentKind Kind = AttachmentKind.Blade;
        public List<SlotPosition> CompatibleSlots = new List<SlotPosition>();
        public float MassKg = 500f;
        public float HydraulicFlowLpm;
        public bool RequiresPto;
        public float WorkingWidthM = 2.5f;
        public AttachmentEffects Effects = new AttachmentEffects();
        public double PurchasePrice = 10000;
        public float WearRate = 1f;
        public float MountMinutes = 20f;
        public float DismountMinutes = 15f;
        public float PowerDrawKw;
        /// <summary>Roles the attachment enables on its host (e.g. Groom for a tiller, Plow for a blade).</summary>
        public List<VehicleRole> GrantsRoles = new List<VehicleRole>();
        /// <summary>Chassis categories this attachment is meant for (empty = any with a matching slot).</summary>
        public List<VehicleCategory> ForCategories = new List<VehicleCategory>();
        public float MinHostPowerKw;
        public MeshRecipe Visual = new MeshRecipe();
        /// <summary>Optional Resources path to a hand-authored implement model; see VehicleDef.ModelOverride.</summary>
        public string ModelOverride = "";
        public string Comment = "";

        public bool FitsSlot(SlotPosition p) => CompatibleSlots.Contains(p);
    }
}
