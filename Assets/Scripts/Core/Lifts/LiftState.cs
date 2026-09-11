using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Lifts
{
    public enum LiftStatus { Planned, UnderConstruction, Closed, Open, WindHold, LightningHold, ColdHold, Breakdown, Inspection, Evacuation, Decommissioned }

    /// <summary>A cohort riding the lift; unloaded when its arrive tick passes.</summary>
    [Serializable]
    public sealed class RiderCohort
    {
        public int AgentId;
        public int Guests;
        public long BoardTick;
        public long ArriveTick;
    }

    /// <summary>A cohort waiting in the maze.</summary>
    [Serializable]
    public sealed class QueueEntry
    {
        public int AgentId;
        public int Guests;
        public long JoinTick;
    }

    /// <summary>One built (or being built) lift on the mountain.</summary>
    [Serializable]
    public sealed class LiftState
    {
        public int Id;
        public string TypeId = "";
        public string Name = "";
        public Vec2 Bottom;
        public Vec2 Top;
        public float LengthM;
        public float VerticalM;
        public float MaxGradeDeg;
        public float MaxSpanM;
        public List<Vec2> Towers = new List<Vec2>();
        public List<string> Options = new List<string>();
        public LiftStatus Status = LiftStatus.Closed;
        public string StatusReason = "";
        public string BottomNodeId = "";
        public string TopNodeId = "";
        public List<QueueEntry> Queue = new List<QueueEntry>();
        public List<RiderCohort> Riders = new List<RiderCohort>();
        /// <summary>Fractional carrier-loads accumulated toward the next boarding.</summary>
        public float LoadAccumulator;
        public int QueueGuests;
        public long TotalLoaded;
        public long TotalUnloaded;
        public long LoadedToday;
        public float HoursOperated;
        public float HoursOperatedToday;
        public float HoursSinceInspection;
        public int DaysSinceInspection;
        public int DaysSinceAnnualInspection;
        public float Condition = 1f;
        public long BreakdownUntilTick = -1;
        public int Breakdowns;
        public double CapexTotal;
        public double BookValue;
        public int BuildDay;
        public bool SinglesLine;
        public bool Maze = true;
        public int Carriers;
        public float PowerKwhToday;
        public float AvgQueueMinToday;
        public float QueueSampleSum;
        public int QueueSamples;
        public int MidStation = -1;
        public bool NightLighting;
        public bool PrebuiltByScenario;
        /// <summary>Closed by the player (stays closed through the resort's opening hour).</summary>
        public bool PlayerClosed;

        [JsonIgnore] public LiftTypeDef Type;

        public bool IsRunning => Status == LiftStatus.Open;
        public bool IsBuilt => Status != LiftStatus.Planned && Status != LiftStatus.UnderConstruction;
        public Vec2 Direction => (Top - Bottom).Normalized;
    }

    [Serializable]
    public sealed class LiftsState
    {
        public List<LiftState> Lifts = new List<LiftState>();
        public int NextId = 1;

        public LiftState Get(int id)
        {
            for (int i = 0; i < Lifts.Count; i++) if (Lifts[i].Id == id) return Lifts[i];
            return null;
        }
    }

    /// <summary>Result of evaluating a staked line for a lift type (terrain gating + costing).</summary>
    [Serializable]
    public sealed class LiftPlanResult
    {
        public string TypeId = "";
        public Vec2 Bottom;
        public Vec2 Top;
        public bool Ok;
        /// <summary>Human-readable reasons, each naming the constraint (e.g. "span 1,030 m exceeds max span 380 m").</summary>
        public List<string> Reasons = new List<string>();
        public float LengthM;
        public float VerticalM;
        public float MaxGradeDeg;
        public float MaxSpanM;
        public List<Vec2> Towers = new List<Vec2>();
        public double CapexTotal;
        public double CapexTowers;
        public double CapexTerminals;
        public double CapexLine;
        public double CapexCarriers;
        public double CapexBarn;
        public float ConcreteM3;
        public int Carriers;
        public float RideTimeS;
        public float CapacityPph;
        public bool NeedsHelicopter;
        public float HelicopterHours;
        public float EstimatedBuildDays;
        public bool CrossesGorge;
    }

    public struct LiftStatusChangedEvent { public int LiftId; public LiftStatus Status; public string Reason; }
    public struct LiftBuiltEvent { public int LiftId; }
    public struct LiftBreakdownEvent { public int LiftId; public float DowntimeHours; public double Cost; }
    public struct LiftUnloadEvent { public int LiftId; public int AgentId; public int Guests; }
}
