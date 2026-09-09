using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Vehicles
{
    /// <summary>Control inputs for one machine, written by the player (each frame) or by the AI operator (each tick).</summary>
    [Serializable]
    public sealed class VehicleInput
    {
        public float Throttle;       // -1..1 (reverse..forward)
        public float Steer;          // -1..1 (left..right)
        public float Brake;          // 0..1
        public float BladeLift;      // -1..1 rate: lower..raise (front implement)
        public float BladeAngle;     // -1..1 rate: rotate left..right (12-way blades)
        public float BladeTilt;      // -1..1 rate
        public bool Tiller;          // rear implement engaged (tiller down / setter down / spreader on)
        public bool Implement;       // generic work implement on: blower, PTO, pump, crane, spread
        public bool Lights;
        public bool Work;            // "do the job here" at a task site (construction, loading, refuel)
        public bool WinchTension;    // M7 scaffold

        public void Clear()
        {
            Throttle = 0f; Steer = 0f; Brake = 0f; BladeLift = 0f; BladeAngle = 0f; BladeTilt = 0f;
            Tiller = false; Implement = false; Work = false; WinchTension = false;
        }
        public VehicleInput Clone() => (VehicleInput)MemberwiseClone();
    }

    [Serializable]
    public sealed class MountedAttachment
    {
        public string DefId = "";
        public SlotPosition Slot = SlotPosition.Front;
        public float Wear;           // 0..1
        public float Hours;
        /// <summary>Implement pose: 0 = down/working, 1 = fully raised.</summary>
        public float Lift = 1f;
        public float Angle;          // radians relative to chassis (blades)
        public float Tilt;
        public bool Engaged;
        /// <summary>Snow currently carried in front of a blade or inside a bucket, kg.</summary>
        public float LoadKg;
        public float LoadDensity = 300f;
    }

    public enum FailureKind { EngineFailure, HydraulicFailure, DrivetrainFailure, TrackFailure, AttachmentFailure, Electrical, Accident }

    [Serializable]
    public sealed class FailureState
    {
        public FailureKind Kind = FailureKind.EngineFailure;
        public WearSubsystem Subsystem = WearSubsystem.Engine;
        public long Tick;
        public double RepairCost;
        public float RepairHours;
        public string PartsKind = "";
        public bool Immobilising = true;
        public string Description = "";
    }

    [Serializable]
    public sealed class HiddenDefect
    {
        public WearSubsystem Subsystem = WearSubsystem.Engine;
        public float ExtraWear;      // added wear once revealed (0..1)
        public bool Revealed;
        public string Description = "";
    }

    /// <summary>Per-subsystem wear (0 = new, 1 = failed) and service history.</summary>
    [Serializable]
    public sealed class VehicleCondition
    {
        public float WearEngine;
        public float WearHydraulics;
        public float WearDrivetrain;
        public float WearTracks;
        public float WearAttachment;
        public float HoursSinceService;
        public float HoursSinceRebuild;
        public int ServicesDone;
        public int FailuresTotal;
        public List<FailureState> ActiveFailures = new List<FailureState>();
        public List<HiddenDefect> HiddenDefects = new List<HiddenDefect>();
        public List<string> ServiceLog = new List<string>();

        public float Wear(WearSubsystem s)
        {
            switch (s)
            {
                case WearSubsystem.Engine: return WearEngine;
                case WearSubsystem.Hydraulics: return WearHydraulics;
                case WearSubsystem.Drivetrain: return WearDrivetrain;
                case WearSubsystem.TracksOrTires: return WearTracks;
                default: return WearAttachment;
            }
        }

        public void SetWear(WearSubsystem s, float v)
        {
            v = v < 0f ? 0f : (v > 1f ? 1f : v);
            switch (s)
            {
                case WearSubsystem.Engine: WearEngine = v; break;
                case WearSubsystem.Hydraulics: WearHydraulics = v; break;
                case WearSubsystem.Drivetrain: WearDrivetrain = v; break;
                case WearSubsystem.TracksOrTires: WearTracks = v; break;
                default: WearAttachment = v; break;
            }
        }

        /// <summary>Overall condition 0..100 (100 = new).</summary>
        public float ConditionPct => 100f * (1f - (WearEngine * 0.35f + WearHydraulics * 0.2f + WearDrivetrain * 0.2f + WearTracks * 0.15f + WearAttachment * 0.1f));
        public bool IsDown
        {
            get
            {
                for (int i = 0; i < ActiveFailures.Count; i++) if (ActiveFailures[i].Immobilising) return true;
                return false;
            }
        }
    }

    public enum AiMode { Idle, FollowRoute, GroomPiste, PlowZone, BlowZone, SpreadZone, HaulLoop, GoToSite, WorkAtSite, ReturnToBase, Refuel, Stranded }

    /// <summary>AI operator driving state (route following and job execution).</summary>
    [Serializable]
    public sealed class VehicleAiState
    {
        public AiMode Mode = AiMode.Idle;
        public List<Vec2> Route = new List<Vec2>();
        public int RouteIndex;
        public string TargetId = "";       // piste / zone / lift id
        public int TargetIndex = -1;
        public int TaskId = -1;
        public int Lane;                   // grooming lane index
        public int LaneCount;
        public bool Uphill;
        public float StuckTimer;
        public float WorkTimer;
        public Vec2 LoadAt;
        public Vec2 DeliverTo;
        public bool Loaded;
        public string Phase = "";
    }

    /// <summary>
    /// Runtime state of one machine. Position is on the map plane (metres); Z comes from terrain + snow.
    /// Heading is radians counter-clockwise from +X (east). Speed is signed along the heading.
    /// </summary>
    [Serializable]
    public sealed class VehicleState
    {
        public int Id;
        public string DefId = "";
        public string Name = "";
        public Vec2 Pos;
        public float Heading;
        public float Speed;
        public float YawRate;
        public float PitchDeg;
        public float RollDeg;
        public float TrackSpeedL;
        public float TrackSpeedR;
        public float SteerAngle;      // radians (wheeled), articulation angle (artic)
        public bool EngineOn;
        public float Rpm;
        public float EngineTempC = -5f;
        public float HydraulicTempC = -5f;
        /// <summary>Litres of fuel, or kWh for electric machines.</summary>
        public float Fuel;
        public float HoursMeter;
        public float OdometerKm;
        public float SlipFrac;        // 0..1 current track/tyre slip
        public float LoadFrac;        // 0..1 engine load
        public float FuelBurnLph;     // current burn for the HUD
        public float Sinkage;         // metres the tracks sink into the snow
        public List<MountedAttachment> Mounted = new List<MountedAttachment>();
        public VehicleInput Input = new VehicleInput();
        public VehicleCondition Condition = new VehicleCondition();
        public VehicleAiState Ai = new VehicleAiState();
        public int OperatorId = -1;
        public bool PlayerControlled;
        public int TaskId = -1;
        public float CargoKg;
        public string CargoKind = "";
        public float CargoDensity = 300f;
        public float SaltKg;
        public float BrineL;
        public float WaterL;
        /// <summary>Diesel carried by a fuel/service truck for field refuelling, litres.</summary>
        public float FuelCargoL;
        public bool Sheltered;
        public bool Stranded;
        public string StrandedReason = "";
        public float WarmupFrac;      // 0..1 hydraulic warm-up
        public bool LightsOn;
        public bool Leased;
        public bool Rented;
        public int RentalEndDay = -1;
        public int AcquiredDay;
        public double PurchasePaid;
        public float TilledM2Today;
        public float TilledM2Total;
        public float PlowedM2Today;
        public float BlownKgToday;
        public float MadeSnowM3Today;
        /// <summary>Nearest landmark / piste name for the fleet screen; refreshed by the vehicle system.</summary>
        public string LocationLabel = "";
        /// <summary>For placed snow guns: the gun record id in the snowmaking state (-1 = stowed).</summary>
        public int PlacedGunId = -1;

        [JsonIgnore] public VehicleDef Def;

        public MountedAttachment Slot(SlotPosition p)
        {
            for (int i = 0; i < Mounted.Count; i++) if (Mounted[i].Slot == p) return Mounted[i];
            return null;
        }

        public bool IsUsable => !Stranded && !Condition.IsDown;
        public float SpeedKmh => System.Math.Abs(Speed) * 3.6f;
    }

    [Serializable]
    public sealed class VehicleFleetState
    {
        public List<VehicleState> List = new List<VehicleState>();
        public int NextId = 1;
        public int PlayerVehicleId = -1;

        public VehicleState Get(int id)
        {
            for (int i = 0; i < List.Count; i++) if (List[i].Id == id) return List[i];
            return null;
        }
    }

    // ---- events ----
    public struct VehicleSpawnedEvent { public int VehicleId; }
    public struct VehicleRemovedEvent { public int VehicleId; }
    public struct VehicleFailureEvent { public int VehicleId; public FailureState Failure; }
    public struct VehicleStrandedEvent { public int VehicleId; public string Reason; }
    public struct VehicleRefueledEvent { public int VehicleId; public float Liters; }
    public struct PlayerVehicleChangedEvent { public int VehicleId; }
    public struct AttachmentChangedEvent { public int VehicleId; public SlotPosition Slot; public string AttachmentId; public bool Mounted; }
}
