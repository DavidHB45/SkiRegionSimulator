using System;
using System.Collections.Generic;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Fleet
{
    [Serializable]
    public sealed class LicenseDef
    {
        public OperatorLicense License = OperatorLicense.Basic;
        public string DisplayName = "";
        public double TrainingCost;
        public float TrainingDays;
        public float WagePremiumPerHour;
        public List<OperatorLicense> Requires = new List<OperatorLicense>();
        public string Comment = "";
    }

    /// <summary>Operator hiring tables (operators.json).</summary>
    [Serializable]
    public sealed class OperatorsData
    {
        public List<LicenseDef> Licenses = new List<LicenseDef>();
        public List<string> FirstNames = new List<string>();
        public List<string> LastNames = new List<string>();
        public float CompetenceMin = 0.6f;
        public float CompetenceMax = 0.95f;
        public float BaseWagePerHour = 26f;
        public float WagePerCompetencePoint = 40f;
        public float MaxSlopeDegAtMinCompetence = 18f;
        public float MaxSlopeDegAtMaxCompetence = 34f;
        public float ShiftHours = 8f;
        public int CandidatesPerWeek = 4;
        public float UnlicensedAccidentProbabilityPerHour = 0.04f;
        public float LicensedAccidentProbabilityPerHour = 0.002f;
        public double AccidentCostMean = 9000;
        public float CompetenceGainPerHour = 0.0004f;
        public string Comment = "";

        public LicenseDef License(OperatorLicense l)
        {
            foreach (var d in Licenses) if (d.License == l) return d;
            return null;
        }
    }
}
