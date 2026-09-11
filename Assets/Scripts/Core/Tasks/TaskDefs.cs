using System;
using System.Collections.Generic;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Tasks
{
    public enum TaskKind
    {
        Groom, PlowRoad, ClearLot, Spread, BlowRamp, HaulSnow, HaulMaterial, Refuel, Repair, Rescue,
        Survey, Clear, Excavate, PourConcrete, SetTower, PlaceTerminal, StringRope, PullTrackRopes, HangCarriers,
        BuildBarn, Commission, LoadTest, Inspect, MakeSnow, RelocateGun, Transport, GradeRun, Custom
    }

    /// <summary>What a task kind needs (tasks.json). The task system queries roles against the fleet.</summary>
    [Serializable]
    public sealed class TaskKindDef
    {
        public TaskKind Kind = TaskKind.Custom;
        public string DisplayName = "";
        public List<VehicleRole> RequiredRoles = new List<VehicleRole>();
        public OperatorLicense RequiredLicense = OperatorLicense.Basic;
        /// <summary>Work units per hour at competence 1.0 for a machine with work rate 1.0.</summary>
        public float BaseRatePerHour = 1f;
        public float SiteRadiusM = 12f;
        public bool NeedsDaylight;
        public float MaxWindKmh = 120f;
        public float PriorityDefault = 1f;
        public string Comment = "";
    }

    [Serializable]
    public sealed class TasksData
    {
        public List<TaskKindDef> Kinds = new List<TaskKindDef>();
        public string Comment = "";

        public TaskKindDef Kind(TaskKind k)
        {
            foreach (var d in Kinds) if (d.Kind == k) return d;
            return null;
        }
    }
}
