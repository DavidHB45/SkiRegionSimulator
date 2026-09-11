using System;
using System.Collections.Generic;
using AlpineSim.Core.Lifts;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Construction
{
    public enum ConstructionStageKind
    {
        Survey, ClearCorridor, ExcavateFootings, PourConcrete, SetTowers, PlaceTerminals, StringHaulRope,
        PullTrackRopes, HangCarriers, BuildCabinBarn, Commission, LoadTest, Inspection,
        // run construction
        GradeRun, ClearVegetation, InstallSnowmaking
    }

    /// <summary>One stage of a construction chain (construction.json). Each stage becomes one or more driven tasks.</summary>
    [Serializable]
    public sealed class ConstructionStageDef
    {
        public ConstructionStageKind Kind = ConstructionStageKind.Survey;
        public string DisplayName = "";
        public List<VehicleRole> RequiredRoles = new List<VehicleRole>();
        public OperatorLicense RequiredLicense = OperatorLicense.Basic;
        /// <summary>tower | terminal | lift | km | carrier | m3 | run</summary>
        public string PerUnit = "lift";
        /// <summary>Machine-hours of work per unit at work rate 1.0 and competence 1.0.</summary>
        public float WorkHoursPerUnit = 8f;
        public float MaterialConcreteM3PerUnit;
        public float MaxWindKmh = 60f;
        public float MinTempC = -25f;
        public bool NeedsDaylight = true;
        public double CostPerUnit;
        public CraneClass MinCrane = CraneClass.None;
        public bool HelicopterOption;
        public float HelicopterHoursPerUnit;
        public int LaborCrew = 2;
        public string Comment = "";
    }

    [Serializable]
    public sealed class ConstructionData
    {
        public Dictionary<string, List<ConstructionStageDef>> StagesByFamily = new Dictionary<string, List<ConstructionStageDef>>();
        public List<ConstructionStageDef> RunStages = new List<ConstructionStageDef>();
        public double HelicopterRatePerHour = 6500;
        public double HelicopterMobilization = 18000;
        public double ConcretePricePerM3 = 180;
        public double LaborRatePerHour = 38;
        public double InspectionFee = 25000;
        public double RunCostPerM = 220;
        public float VegetationClearHoursPerHectare = 12f;
        public string Comment = "";

        public List<ConstructionStageDef> StagesFor(LiftFamily family)
        {
            string key = family.ToString().ToLowerInvariant();
            if (StagesByFamily.TryGetValue(key, out var list)) return list;
            if (StagesByFamily.TryGetValue("chair", out list)) return list;
            return new List<ConstructionStageDef>();
        }
    }
}
