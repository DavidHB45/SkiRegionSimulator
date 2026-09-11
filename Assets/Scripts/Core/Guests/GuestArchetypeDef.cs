using System;
using System.Collections.Generic;
using AlpineSim.Core.Pistes;

namespace AlpineSim.Core.Guests
{
    /// <summary>One guest archetype (guests.json). Weights are relative sensitivities in the satisfaction model.</summary>
    [Serializable]
    public sealed class GuestArchetypeDef
    {
        public string Id = "";
        public string DisplayName = "";
        /// <summary>Relative share of demand on an ordinary weekday.</summary>
        public float Share = 1f;
        public float WeekendMultiplier = 1f;
        public float HolidayMultiplier = 1f;
        public float PriceSensitivity = 1f;
        public float PqiWeight = 1f;
        public float QueueWeight = 1f;
        public float TerrainWeight = 1f;
        public float FoodWeight = 0.5f;
        public float ParkingWeight = 0.5f;
        public float ComfortWeight = 0.5f;
        public float WeatherSensitivity = 1f;
        public PisteDifficulty MinDifficulty = PisteDifficulty.Green;
        public PisteDifficulty MaxDifficulty = PisteDifficulty.Black;
        public PisteDifficulty PreferredDifficulty = PisteDifficulty.Blue;
        public float SkiSpeedMs = 8f;
        public int LapsPerDayTarget = 8;
        public float StayHours = 5f;
        public float ArrivalHourMean = 9.5f;
        public float ArrivalHourSpread = 1.2f;
        public float FoodSpendPerDay = 18f;
        public float RentalProbability = 0.1f;
        public float LessonProbability = 0.05f;
        public float ParkingProbability = 0.8f;
        public float LateralSpread = 0.4f;
        public float QueuePatienceMin = 20f;
        public float SnowReportSensitivity = 1f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class GuestsData
    {
        public List<GuestArchetypeDef> Archetypes = new List<GuestArchetypeDef>();
        /// <summary>Season-day indices (0-based) that are holidays.</summary>
        public List<int> HolidaySeasonDays = new List<int>();
        public string Comment = "";

        public GuestArchetypeDef Archetype(string id)
        {
            foreach (var a in Archetypes) if (a.Id == id) return a;
            return null;
        }
    }
}
