using System;
using System.Collections.Generic;

namespace AlpineSim.Core.Lifts
{
    public enum LiftFamily { Surface, Chair, Gondola, Hybrid, Aerial, Rail }
    public enum GripType { Fixed, Detachable, None }
    public enum RopeConfig { Mono, Bi, Tri, Funitel, Reversible, Rack, Surface }
    public enum CraneClass { None, Telehandler, CraneTruck, Crawler, Helicopter }

    [Serializable] public sealed class StaffRequired { public int Top; public int Bottom; public int Mid; public int Patrol; public int Total => Top + Bottom + Mid + Patrol; }

    /// <summary>One lift type (lifts.json). The upgrade ladder from magic carpet to 3S.</summary>
    [Serializable]
    public sealed class LiftTypeDef
    {
        public string Id = "";
        public string DisplayName = "";
        public LiftFamily Family = LiftFamily.Chair;
        public int Tier;
        public int CarriersPerHaulRope = 1;
        public int SeatsOrCabinCapacity = 4;
        public GripType Grip = GripType.Fixed;
        public RopeConfig RopeConfiguration = RopeConfig.Mono;
        public float LineSpeedMs = 2.5f;
        public float RideSpeedMs = 2.5f;
        public float LoadingSpeedMs = 1.0f;
        public float CapacityPph = 2000f;
        public float MaxLengthM = 2000f;
        public float MaxVerticalM = 600f;
        public float MaxSpanM = 300f;
        public float MaxGradeDeg = 30f;
        public float MinTowerSpacingM = 40f;
        public float TowersPerKm = 10f;
        public double CapexPerKm = 2000000;
        public double CapexPerTower = 60000;
        public double CapexTerminalDrive = 500000;
        public double CapexTerminalReturn = 250000;
        public double CapexPerCarrier = 4000;
        public float FoundationConcreteM3PerTower = 12f;
        public CraneClass CraneClass = CraneClass.CraneTruck;
        public bool HelicopterRequired;
        public double OpexPerOperatingHour = 100;
        public StaffRequired StaffRequired = new StaffRequired();
        public float PowerDrawKw = 300f;
        public float StandbyPowerKw = 10f;
        public float WindHoldKmh = 60f;
        public bool LightningHold = true;
        public float ColdWeatherLimitC = -30f;
        public float AnnualMaintenancePct = 3f;
        public float MtbfHours = 600f;
        public int InspectionIntervalDays = 30;
        public float EvacuationTimeMin = 90f;
        public double EvacuationCost = 20000;
        public float ComfortScore = 0.5f;
        public bool BeginnerFriendly;
        public bool SkiOffRampRequired = true;
        public List<string> Options = new List<string>();
        /// <summary>0 (open chair) .. 1 (enclosed cabin): how much weather the rider feels.</summary>
        public float WeatherExposure = 0.8f;
        /// <summary>Seconds a carrier spends in the load zone (gondolas load slower).</summary>
        public float LoadTimeS = 6f;
        public double CabinBarnCapex;
        public float CarrierSpacingM;
        public float SummerRevenueFactor;
        public float NonSkierRidership;
        /// <summary>
        /// Optional Resources path to a folder of hand-authored lift models. The registry looks inside
        /// it for tower, tower_low, tower_high, terminal_drive, terminal_return, carrier and barn, and
        /// falls back to the generated folder for anything the override does not provide.
        /// </summary>
        public string ModelOverride = "";
        public string Comment = "";

        public bool IsSurface => Family == LiftFamily.Surface;
        public bool IsEnclosed => Family == LiftFamily.Gondola || Family == LiftFamily.Aerial || Family == LiftFamily.Rail || Family == LiftFamily.Hybrid;
    }

    /// <summary>Optional equipment (lifts.json "options").</summary>
    [Serializable]
    public sealed class LiftOptionDef
    {
        public string Id = "";
        public string DisplayName = "";
        public float CapexPct;
        public float OpexPct;
        public float ComfortBonus;
        public float WindHoldBonusKmh;
        public float CapacityPct;
        public float LoadTimeFactor = 1f;
        public bool NightOperation;
        public bool BeginnerBonus;
        public List<LiftFamily> Families = new List<LiftFamily>();
        public string Comment = "";
    }
}
