using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;

namespace AlpineSim.Core.Construction
{
    public enum ProjectKind { Lift, Run, Hydrants, Building }
    public enum ProjectStatus { Staked, InProgress, WaitingWeather, WaitingMaterials, Paused, Complete, Cancelled }

    [Serializable]
    public sealed class StageProgress
    {
        public ConstructionStageKind Kind = ConstructionStageKind.Survey;
        public string DisplayName = "";
        public int UnitsRequired = 1;
        public int UnitsDone;
        public float WorkDone;
        public float WorkRequired;
        public List<int> TaskIds = new List<int>();
        public bool Complete;
        public long StartedTick = -1;
        public long CompletedTick = -1;
        public double CostPaid;
    }

    /// <summary>A lift or run under construction: a visible, blocking object that earns nothing until inspected.</summary>
    [Serializable]
    public sealed class ConstructionProject
    {
        public int Id;
        public ProjectKind Kind = ProjectKind.Lift;
        public string Name = "";
        public int LiftId = -1;
        public string PisteId = "";
        public List<StageProgress> Stages = new List<StageProgress>();
        public int StageIndex;
        public ProjectStatus Status = ProjectStatus.Staked;
        public string StatusReason = "";
        public float ConcreteRequiredM3;
        public float ConcreteDeliveredM3;
        public int TowersSet;
        public int TowersTotal;
        public double Budget;
        public double Spent;
        public int StartDay;
        public int CompletedDay = -1;
        public bool HelicopterMode;
        public float HelicopterHoursUsed;
        public Vec2 Site;
        public List<Vec2> Corridor = new List<Vec2>();
        public float CorridorWidthM;
        public int LaborCrew;

        public StageProgress CurrentStage => StageIndex >= 0 && StageIndex < Stages.Count ? Stages[StageIndex] : null;
        public float OverallProgress
        {
            get
            {
                if (Stages.Count == 0) return 0f;
                float sum = 0f;
                for (int i = 0; i < Stages.Count; i++) sum += Stages[i].Complete ? 1f : (Stages[i].WorkRequired > 0f ? MathUtil.Clamp01(Stages[i].WorkDone / Stages[i].WorkRequired) : 0f);
                return sum / Stages.Count;
            }
        }
    }

    [Serializable]
    public sealed class ConstructionState
    {
        public List<ConstructionProject> Projects = new List<ConstructionProject>();
        public int NextId = 1;

        public ConstructionProject Get(int id)
        {
            for (int i = 0; i < Projects.Count; i++) if (Projects[i].Id == id) return Projects[i];
            return null;
        }

        public ConstructionProject ForLift(int liftId)
        {
            for (int i = 0; i < Projects.Count; i++) if (Projects[i].LiftId == liftId && Projects[i].Status != ProjectStatus.Cancelled) return Projects[i];
            return null;
        }
    }

    public struct ProjectStagedEvent { public int ProjectId; public int StageIndex; public string StageName; }
    public struct ProjectCompletedEvent { public int ProjectId; public int LiftId; public string PisteId; }
    public struct ProjectStatusEvent { public int ProjectId; public ProjectStatus Status; public string Reason; }
}
