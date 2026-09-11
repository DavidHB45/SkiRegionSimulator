using System;
using System.Collections.Generic;

namespace AlpineSim.Core.Data
{
    /// <summary>A lift that exists when the scenario starts.</summary>
    [Serializable]
    public sealed class PrebuiltLiftDef
    {
        public string Id = "";
        public string TypeId = "";
        public string Name = "";
        public float BottomX;
        public float BottomY;
        public float TopX;
        public float TopY;
        public List<string> Options = new List<string>();
        public int ExistsFromAct = 1;
        public float ConditionPct = 100f;
        public int AgeDays;
        public string Comment = "";
    }

    public sealed partial class ScenarioData
    {
        public List<PrebuiltLiftDef> Lifts = new List<PrebuiltLiftDef>();
        public float StartTicketPrice = 70f;
        public float StartReputation = 50f;
        public List<string> StartLoans = new List<string>();
    }
}
