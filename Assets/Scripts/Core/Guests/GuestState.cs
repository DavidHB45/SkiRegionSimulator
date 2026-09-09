using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;

namespace AlpineSim.Core.Guests
{
    public enum GuestPhase { Arriving, AtBase, WalkingToLift, InQueue, Riding, AtTop, Skiing, Eating, Leaving, Gone }

    /// <summary>A cohort of guests moving as one agent (size = simulation.guestsPerAgent).</summary>
    [Serializable]
    public sealed class GuestAgent
    {
        public int Id;
        public string ArchetypeId = "";
        public int Guests = 1;
        public GuestPhase Phase = GuestPhase.Arriving;
        public Vec2 Pos;
        /// <summary>Preferred lateral position across a run, -1..1.</summary>
        public float Lateral;
        public int LiftId = -1;
        public string PisteId = "";
        public int SegmentIndex;
        public float SegmentT;
        public float SpeedMs = 8f;
        /// <summary>Running satisfaction 0..1 (EMA of experiences).</summary>
        public float Satisfaction = 0.7f;
        public float QueueWaitMin;
        public float TotalQueueMin;
        public float TicksInPhase;
        public int Laps;
        public long ArriveTick;
        public long DepartTick;
        public float Fatigue;
        public bool HasRental;
        public bool HadLesson;
        public bool Parked;
        public float SpentToday;
        public string LastPisteId = "";
        public float PqiExperienced;
        public int PqiSamples;
        public float ComfortExperienced;
        public string TargetNodeId = "";
    }

    [Serializable]
    public sealed class DailyGuestReport
    {
        public int Day;
        public int Demand;
        public int Arrived;
        public int Turned;          // left because of queues/closure
        public float AvgSatisfaction;
        public float AvgQueueMin;
        public float AvgPqi;
        public float ReputationAfter;
        public double TicketRevenue;
        public double Spend;
        public int Laps;
    }

    [Serializable]
    public sealed class GuestPopulationState
    {
        public List<GuestAgent> Agents = new List<GuestAgent>();
        public int NextId = 1;
        public int TodayDemand;
        public int TodayArrived;
        public int TodayDeparted;
        public int TodayTurnedAway;
        public double TodaySatisfactionSum;
        public int TodaySatisfactionCount;
        public float TodayQueueMinSum;
        public int TodayQueueSamples;
        public int TodayLaps;
        /// <summary>Lagging reputation 0..100.</summary>
        public float Reputation = 50f;
        /// <summary>Satisfaction of recent days feeding the lag (index 0 = oldest).</summary>
        public List<float> SatisfactionHistory = new List<float>();
        public List<DailyGuestReport> History = new List<DailyGuestReport>();
        public float SnowReportScore;   // yesterday's PQI + fresh snow as the public sees it
        public bool ResortOpenToday;
        public int GuestsOnMountain;
    }

    public struct GuestArrivedEvent { public int AgentId; public int Guests; }
    public struct GuestLeftEvent { public int AgentId; public float Satisfaction; }
    public struct GuestDayClosedEvent { public DailyGuestReport Report; }
    public struct ReputationChangedEvent { public float Reputation; public float Delta; }
}
