using System;
using System.Collections.Generic;

namespace AlpineSim.Core.Economy
{
    public enum LedgerCategory
    {
        Tickets, SeasonPasses, Rentals, SkiSchool, FoodBev, Parking, Events, Summer,
        Fuel, Wages, Electricity, Water, Insurance, LiftMaintenance, LiftOpex, VehicleRepair, VehicleService, Parts,
        Snowmaking, LoanInterest, LoanPrincipal, PropertyTax, Lease, Rental, Purchase, Sale, TradeIn, Construction,
        Helicopter, Training, Inspection, Evacuation, Penalty, Misc
    }

    [Serializable]
    public sealed class ActDef
    {
        public int Act = 1;
        public string DisplayName = "";
        public double MinNetWorth;
        public float MinReputation;
        public int MaxLiftTier = 1;
        public int MaxVehicleTier = 1;
        public double MaxLoan = 200000;
        public float TicketPriceBase = 70f;
        public float DemandBase = 400f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class WageDef
    {
        public string Role = "";
        public float PerHour = 25f;
        public float ShiftHours = 8f;
        public string Comment = "";
    }

    /// <summary>Economy tables (economy.json).</summary>
    [Serializable]
    public sealed class EconomyData
    {
        public List<ActDef> Acts = new List<ActDef>();
        public List<WageDef> Wages = new List<WageDef>();
        public float TicketPriceMin = 40f;
        public float TicketPriceMax = 180f;
        public float HalfDayFactor = 0.7f;
        public float SeasonPassFactor = 12f;
        public float RentalPrice = 45f;
        public float LessonPrice = 90f;
        public float ParkingPrice = 10f;
        public float FoodMarginPct = 55f;
        public double PropertyTaxAnnual = 60000;
        public double InsuranceBaseAnnual = 40000;
        public float LoanBaseRatePct = 6f;
        public float LoanReputationDiscountPct = 2f;
        public float LoanDebtRatioPenaltyPct = 5f;
        public int LoanTermDaysDefault = 730;
        public float ElectricityPricePerKwh = 0.14f;
        public float WaterPricePerM3 = 0.6f;
        public float DieselPricePerL = 1.25f;
        public float GasolinePricePerL = 1.45f;
        public float ReputationLagDays = 7f;
        public float ReputationStart = 50f;
        public float DemandPqiElasticity = 0.8f;
        public float DemandPriceElasticity = 1.2f;
        public float DemandReputationElasticity = 1.0f;
        public float DemandWeatherPenalty = 0.5f;
        public float DemandSnowReportBonus = 0.3f;
        public float RegionalCompetitionFactor = 0.9f;
        public float OperatingHoursPerDay = 7f;
        public string Comment = "";

        public ActDef Act(int act)
        {
            ActDef best = null;
            foreach (var a in Acts) if (a.Act <= act && (best == null || a.Act > best.Act)) best = a;
            return best ?? (Acts.Count > 0 ? Acts[0] : new ActDef());
        }

        public float Wage(string role)
        {
            foreach (var w in Wages) if (w.Role == role) return w.PerHour;
            return Wages.Count > 0 ? Wages[0].PerHour : 25f;
        }
    }
}
