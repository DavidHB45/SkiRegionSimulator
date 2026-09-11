using System;
using System.Collections.Generic;

namespace AlpineSim.Core.Data
{
    /// <summary>A machine the resort owns at scenario start.</summary>
    [Serializable]
    public sealed class StartingVehicleDef
    {
        public string DefId = "";
        public string Name = "";
        public float X;
        public float Y;
        public float HeadingDeg;
        public float Hours;
        public float ConditionPct = 100f;
        public float FuelFrac = 0.8f;
        public bool Sheltered = true;
        public int ExistsFromAct = 1;
        public string Comment = "";
    }

    public sealed partial class ScenarioData
    {
        public List<StartingVehicleDef> StartingFleet = new List<StartingVehicleDef>();
        /// <summary>Where machines park, refuel and get serviced (garage landmark).</summary>
        public string GarageLandmarkId = "garage";
        public string FuelLandmarkId = "fuelDepot";
        public string WorkshopLandmarkId = "workshop";
        public float GarageRadiusM = 45f;

        public MapPoint Landmark(string id)
        {
            foreach (var l in Landmarks) if (l.Id == id) return l;
            return BaseArea;
        }
    }
}
