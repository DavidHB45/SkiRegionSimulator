using System;
using System.Collections.Generic;
using AlpineSim.Core.Fleet;

namespace AlpineSim.Core.Data
{
    public sealed partial class ScenarioData
    {
        public List<HydrantDef> Hydrants = new List<HydrantDef>();
        public float ReservoirInitialM3 = 45000f;
        public float ReservoirCapacityM3 = 60000f;
        public List<string> StartPumpStations = new List<string>();
        public List<string> StartCompressorStations = new List<string>();
        public string ClimateId = "alpine";
    }
}
