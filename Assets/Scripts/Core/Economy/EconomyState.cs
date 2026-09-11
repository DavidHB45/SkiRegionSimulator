using System;
using System.Collections.Generic;

namespace AlpineSim.Core.Economy
{
    [Serializable]
    public sealed class LedgerEntry
    {
        public int Day;
        public int Hour;
        public long Tick;
        public LedgerCategory Category = LedgerCategory.Misc;
        /// <summary>Positive = revenue, negative = cost.</summary>
        public double Amount;
        public string Memo = "";
    }

    [Serializable]
    public sealed class DailyReport
    {
        public int Day;
        public double Revenue;
        public double Costs;
        public double Net;
        public double CashStart;
        public double CashEnd;
        public Dictionary<string, double> ByCategory = new Dictionary<string, double>();
        public int Guests;
        public float AvgPqi;
        public float Reputation;
        public int Entries;
    }

    [Serializable]
    public sealed class PeriodReport
    {
        public string Label = "";
        public int DayStart;
        public int DayEnd;
        public double Revenue;
        public double Costs;
        public double Net;
        public Dictionary<string, double> ByCategory = new Dictionary<string, double>();
        public int Guests;
        public float AvgPqi;
    }

    [Serializable]
    public sealed class Loan
    {
        public int Id;
        public double Principal;
        public double Balance;
        public float AnnualRatePct;
        public int TermDays;
        public double DailyPayment;
        public int StartDay;
        public string Purpose = "";
        public bool Closed;
    }

    [Serializable]
    public sealed class EconomyState
    {
        public double Cash;
        public List<LedgerEntry> Ledger = new List<LedgerEntry>();
        public List<DailyReport> Daily = new List<DailyReport>();
        public List<PeriodReport> Weekly = new List<PeriodReport>();
        public PeriodReport Season = new PeriodReport { Label = "Season" };
        public List<Loan> Loans = new List<Loan>();
        public int NextLoanId = 1;
        public float TicketPrice = 70f;
        public float HalfDayPrice = 49f;
        public float SeasonPassPrice = 840f;
        public int Act = 1;
        public double NetWorth;
        public double TotalRevenue;
        public double TotalCosts;
        public double TodayRevenue;
        public double TodayCosts;
        public double CashAtDayStart;
        public int DaysInBusiness;
        public bool Bankrupt;
        public double LiftBookValue;
        public double FleetBookValue;
        /// <summary>Ledger entries are trimmed to this many days of history (older days survive in Daily reports).</summary>
        public int LedgerRetentionDays = 45;
        /// <summary>Tick of the last daily close; the next report covers every entry posted after it.</summary>
        public long LastCloseTick = -1;
    }

    public struct LedgerPostedEvent { public LedgerEntry Entry; public double CashAfter; }
    public struct DayClosedEvent { public DailyReport Report; }
    public struct WeekClosedEvent { public PeriodReport Report; }
    public struct ActChangedEvent { public int Act; public string Name; }
    public struct BankruptcyEvent { public double Cash; }
}
