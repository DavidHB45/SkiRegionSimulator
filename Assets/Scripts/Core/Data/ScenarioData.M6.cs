using System;
using System.Collections.Generic;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Data
{
    [Serializable]
    public sealed class StartingOperatorDef
    {
        public string Name = "";
        public float Competence = 0.75f;
        public List<OperatorLicense> Licenses = new List<OperatorLicense>();
        public string Comment = "";
    }

    public sealed partial class ScenarioData
    {
        public List<StartingOperatorDef> StartingOperators = new List<StartingOperatorDef>();
        public List<string> StartingAttachments = new List<string>();
        public int StartWorkshopTier = 1;
        public int StartGarageBays = 2;
        public float StartDieselL = 6000f;
        public float StartGasolineL = 400f;
        public Dictionary<string, int> StartParts = new Dictionary<string, int>();
    }
}
