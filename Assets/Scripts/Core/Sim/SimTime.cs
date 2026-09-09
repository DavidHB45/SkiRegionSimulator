using System;

namespace AlpineSim.Core.Sim
{
    /// <summary>
    /// Simulation calendar. The clock is a tick counter; everything else is derived so that
    /// save/load and determinism only depend on the integer tick.
    /// Fixed 20 Hz: dt is a constant and is never scaled by time compression.
    /// </summary>
    [Serializable]
    public sealed class SimTime
    {
        public const int TicksPerSecond = 20;
        public const float Dt = 1f / TicksPerSecond;
        public const int TicksPerMinute = TicksPerSecond * 60;
        public const int TicksPerHour = TicksPerMinute * 60;
        public const int TicksPerDay = TicksPerHour * 24;

        /// <summary>Ticks elapsed since the scenario started.</summary>
        public long Tick;
        /// <summary>Hour of day at tick 0.</summary>
        public int StartHour = 18;
        /// <summary>Day of week at day 0 (0 = Monday).</summary>
        public int StartDayOfWeek = 4;
        /// <summary>Day-of-season index at day 0 (0 = first day of the operating season).</summary>
        public int StartSeasonDay = 0;

        private long Absolute => Tick + (long)StartHour * TicksPerHour;

        public double TotalSeconds => Tick / (double)TicksPerSecond;
        public double TotalHours => Tick / (double)TicksPerHour;
        /// <summary>Whole days since day 0 (day 0 starts at 00:00 of the scenario's first calendar day).</summary>
        public int Day => (int)(Absolute / TicksPerDay);
        public int HourOfDay => (int)((Absolute % TicksPerDay) / TicksPerHour);
        public float HourOfDayF => (Absolute % TicksPerDay) / (float)TicksPerHour;
        public int MinuteOfHour => (int)((Absolute % TicksPerHour) / TicksPerMinute);
        public int SecondOfMinute => (int)((Absolute % TicksPerMinute) / TicksPerSecond);
        /// <summary>Hours since day 0 00:00 — the key used by hourly systems.</summary>
        public long AbsoluteHour => Absolute / TicksPerHour;
        public int DayOfWeek => (int)(((long)StartDayOfWeek + Day) % 7);
        public int SeasonDay => StartSeasonDay + Day;

        public bool IsHourStart => Absolute % TicksPerHour == 0;
        public bool IsMinuteStart => Absolute % TicksPerMinute == 0;
        public bool IsDayStart => Absolute % TicksPerDay == 0;
        public bool IsSecondStart => Absolute % TicksPerSecond == 0;

        public static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        public string Clock => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:00}:{1:00}", HourOfDay, MinuteOfHour);
        public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture, "Day {0} {1} {2}", Day + 1, DayNames[DayOfWeek], Clock);
    }
}
