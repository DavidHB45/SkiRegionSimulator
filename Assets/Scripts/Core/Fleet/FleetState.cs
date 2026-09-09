using System;
using System.Collections.Generic;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Fleet
{
    [Serializable]
    public sealed class OperatorState
    {
        public int Id;
        public string Name = "";
        public float Competence = 0.75f;
        public List<OperatorLicense> Licenses = new List<OperatorLicense>();
        public float WagePerHour = 26f;
        public float MaxSlopeDeg = 25f;
        public int AssignedVehicleId = -1;
        public bool OnShift;
        public float HoursToday;
        public float HoursTotal;
        public float Fatigue;
        public int HiredDay;
        public bool IsPlayer;
        public int TrainingLicensePending = -1;
        public int TrainingDoneDay = -1;
        public int Accidents;

        public bool Has(OperatorLicense l) => Licenses.Contains(l);
    }

    public enum ListingKind { New, Used, Lease, Rent }

    [Serializable]
    public sealed class MarketListing
    {
        public int Id;
        public string DefId = "";
        public ListingKind Kind = ListingKind.New;
        public double Price;
        public double MonthlyOrDaily;
        public float Hours;
        public float ConditionPct = 100f;
        public List<HiddenDefect> Defects = new List<HiddenDefect>();
        public bool Inspected;
        public int ExpiresDay;
        public string SellerNote = "";
    }

    public enum ServiceJobKind { PreventiveMaintenance, Repair, Rebuild, Mount, Dismount, Inspection }
    public enum ServiceJobStatus { Queued, WaitingParts, InProgress, SentOut, Done, Cancelled }

    [Serializable]
    public sealed class ServiceJob
    {
        public int Id;
        public int VehicleId;
        public ServiceJobKind Kind = ServiceJobKind.PreventiveMaintenance;
        public ServiceJobStatus Status = ServiceJobStatus.Queued;
        public WearSubsystem Subsystem = WearSubsystem.Engine;
        public float HoursRequired = 4f;
        public float HoursDone;
        public string PartsKind = "";
        public int PartsQty;
        public double Cost;
        public string AttachmentId = "";
        public SlotPosition Slot = SlotPosition.Front;
        public int CreatedDay;
        public int ReturnDay = -1;
        public string Note = "";
    }

    [Serializable]
    public sealed class PartsOrder
    {
        public int Id;
        public string Kind = "";
        public int Qty;
        public int ArriveDay;
        public double Cost;
    }

    [Serializable]
    public sealed class PartsInventory
    {
        public Dictionary<string, int> Stock = new Dictionary<string, int>();
        public List<PartsOrder> Orders = new List<PartsOrder>();
        public int NextOrderId = 1;
        public int Get(string kind) => Stock.TryGetValue(kind, out int n) ? n : 0;
    }

    [Serializable]
    public sealed class WorkshopState
    {
        public int Tier = 1;
        public List<ServiceJob> Jobs = new List<ServiceJob>();
        public int NextJobId = 1;
        public bool WashBay;
        public int GarageBays = 2;
        public int JobsDoneTotal;
    }

    [Serializable]
    public sealed class FuelDelivery
    {
        public int Id;
        public float Liters;
        public string Fuel = "diesel";
        public int ArriveDay;
        public int ArriveHour;
        public double Cost;
        public bool Contract;
    }

    [Serializable]
    public sealed class FuelDepotState
    {
        public float DieselL = 8000f;
        public float GasolineL = 800f;
        public float DieselCapacityL = 20000f;
        public float GasolineCapacityL = 2000f;
        public float DieselPricePerL = 1.25f;
        public float GasolinePricePerL = 1.45f;
        public float ContractRemainingL;
        public float ContractPricePerL;
        public List<FuelDelivery> Pending = new List<FuelDelivery>();
        public int NextDeliveryId = 1;
        public float DieselUsedSeasonL;
        public double FuelSpendSeason;
    }

    [Serializable]
    public sealed class LeaseContract
    {
        public int VehicleId;
        public double Monthly;
        public int StartDay;
        public int TermMonths = 36;
        public double Residual;
    }

    [Serializable]
    public sealed class ServiceCall
    {
        public int Id;
        public int VehicleId;
        public string Reason = "";
        public int TaskId = -1;
        public long CreatedTick;
        public bool Resolved;
    }

    [Serializable]
    public sealed class FleetState
    {
        public List<OperatorState> Operators = new List<OperatorState>();
        public List<OperatorState> Candidates = new List<OperatorState>();
        public int NextOperatorId = 1;
        public List<MarketListing> Market = new List<MarketListing>();
        public int NextListingId = 1;
        public int MarketRefreshDay = -1;
        public PartsInventory Parts = new PartsInventory();
        public WorkshopState Workshop = new WorkshopState();
        public FuelDepotState Fuel = new FuelDepotState();
        public List<LeaseContract> Leases = new List<LeaseContract>();
        public List<ServiceCall> ServiceCalls = new List<ServiceCall>();
        public int NextServiceCallId = 1;
        public int InsurancePaidDay = -1;
        public double WagesToday;
        public double FleetValue;
        /// <summary>Attachments owned but not mounted (ids; duplicates allowed).</summary>
        public List<string> OwnedAttachments = new List<string>();
        public bool AutoOrderParts = true;
        public int CandidatesRefreshDay = -1;
        public List<string> PmDueWarned = new List<string>();

        public OperatorState Operator(int id)
        {
            for (int i = 0; i < Operators.Count; i++) if (Operators[i].Id == id) return Operators[i];
            return null;
        }
    }

    public struct MarketRefreshedEvent { public int Listings; }
    public struct VehicleAcquiredEvent { public int VehicleId; public ListingKind Kind; public double Price; }
    public struct VehicleSoldEvent { public int VehicleId; public double Price; }
    public struct ServiceJobEvent { public int JobId; public ServiceJobStatus Status; }
    public struct ServiceCallEvent { public int CallId; public int VehicleId; public string Reason; public bool Resolved; }
    public struct OperatorEvent { public int OperatorId; public string What; }
    public struct FuelDeliveredEvent { public float Liters; public double Cost; }
    public struct AccidentEvent { public int VehicleId; public int OperatorId; public double Cost; public string Description; }
}
