using System;
using System.Collections.Generic;

namespace AlpineSim.Core.Fleet
{
    /// <summary>Workshop bay tier (stations.json). Determines what is repairable on site and how fast.</summary>
    [Serializable]
    public sealed class WorkshopTierDef
    {
        public int Tier = 1;
        public string DisplayName = "";
        public int Bays = 1;
        public double UpgradeCost;
        public double MonthlyCost;
        public float RepairSpeed = 1f;
        public float PmSpeed = 1f;
        /// <summary>Subsystems repairable in-house: engine, hydraulics, drivetrain, tracksOrTires, attachment.</summary>
        public List<string> CanRepair = new List<string>();
        public bool CanRebuild;
        public float SendOutDaysMultiplier = 1f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class FuelDepotDef
    {
        public float DieselCapacityL = 20000f;
        public float GasolineCapacityL = 2000f;
        public double UpgradeCostPer10000L = 30000;
        public float DeliveryLeadHours = 24f;
        public float DeliveryMinL = 5000f;
        public float BasePricePerL = 1.25f;
        public float PriceVolatility = 0.12f;
        public float ContractDiscountPct = 6f;
        public float ContractVolumeL = 40000f;
        public float SpotSurchargePct = 15f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class PartsKindDef
    {
        public string Id = "";
        public string DisplayName = "";
        public double UnitCost;
        public float LeadDays = 3f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class GarageDef
    {
        public int BaysPerUpgrade = 4;
        public double CostPerBay = 25000;
        public float UnshelteredWearMultiplier = 1.3f;
        public float UnshelteredColdStartPenalty = 1.5f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class WashBayDef
    {
        public double Cost = 45000;
        public float WearReductionPct = 8f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class HydrantDef
    {
        public string Id = "";
        public float X;
        public float Y;
        public string PisteId = "";
        public double InstallCost;
        public string Comment = "";
        public Math.Vec2 Pos => new Math.Vec2(X, Y);
    }

    [Serializable]
    public sealed class ReservoirDef
    {
        public float CapacityM3 = 60000f;
        public float InitialM3 = 45000f;
        public float InflowM3PerHour = 8f;
        public double ExpansionCostPer10000M3 = 400000;
        public string Comment = "";
    }

    [Serializable]
    public sealed class PumpStationDef
    {
        public string Id = "";
        public string DisplayName = "";
        public float FlowLps = 60f;
        public float HeadM = 300f;
        public float PowerKw = 250f;
        public double Cost = 350000;
        public string Comment = "";
    }

    [Serializable]
    public sealed class CompressorStationDef
    {
        public string Id = "";
        public string DisplayName = "";
        public float AirM3PerMin = 40f;
        public float PowerKw = 250f;
        public double Cost = 220000;
        public string Comment = "";
    }

    /// <summary>All stationary support infrastructure definitions (stations.json).</summary>
    [Serializable]
    public sealed class StationsData
    {
        public List<WorkshopTierDef> WorkshopTiers = new List<WorkshopTierDef>();
        public FuelDepotDef FuelDepot = new FuelDepotDef();
        public List<PartsKindDef> PartsKinds = new List<PartsKindDef>();
        public GarageDef Garage = new GarageDef();
        public WashBayDef WashBay = new WashBayDef();
        public ReservoirDef Reservoir = new ReservoirDef();
        public List<PumpStationDef> PumpStations = new List<PumpStationDef>();
        public List<CompressorStationDef> CompressorStations = new List<CompressorStationDef>();
        public double HydrantInstallCost = 12000;
        public float HydrantSpacingM = 80f;
        public double ElectricityPricePerKwh = 0.14;
        public double WaterPricePerM3 = 0.6;
        public string Comment = "";

        public WorkshopTierDef Workshop(int tier)
        {
            WorkshopTierDef best = null;
            foreach (var w in WorkshopTiers) if (w.Tier <= tier && (best == null || w.Tier > best.Tier)) best = w;
            return best ?? (WorkshopTiers.Count > 0 ? WorkshopTiers[0] : new WorkshopTierDef());
        }
    }
}
