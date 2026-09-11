using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;

namespace AlpineSim.Core.Snowmaking
{
    [Serializable]
    public sealed class Hydrant
    {
        public int Id;
        public string ExternalId = "";
        public Vec2 Pos;
        public string PisteId = "";
        public bool Built = true;
        public int ConnectedGunId = -1;
    }

    /// <summary>A placed snow gun. Mobile guns are vehicles of category Snowmaking; towers are fixed.</summary>
    [Serializable]
    public sealed class SnowGunState
    {
        public int Id;
        public string DefId = "";
        public int VehicleId = -1;
        public Vec2 Pos;
        public float HeadingDeg;     // aim direction, compass degrees
        public bool Running;
        public bool AutoRun = true;
        public int HydrantId = -1;
        public float OutputM3PerHour;
        public float WaterM3Today;
        public float KwhToday;
        public float SnowMadeM3Total;
        public float Hours;
        public bool Fixed;
        public string Status = "";
        public float NozzleWear;
    }

    [Serializable]
    public sealed class BuiltStation
    {
        public int Id;
        public string DefId = "";      // pump / compressor station def id (stations.json)
        public string Kind = "";       // pump | compressor
        public Vec2 Pos;
        public int BuiltDay;
        public double Cost;
    }

    [Serializable]
    public sealed class SnowmakingState
    {
        public List<SnowGunState> Guns = new List<SnowGunState>();
        public List<Hydrant> Hydrants = new List<Hydrant>();
        public List<BuiltStation> Stations = new List<BuiltStation>();
        public int NextGunId = 1;
        public int NextHydrantId = 1;
        public int NextStationId = 1;
        public float ReservoirM3;
        public float ReservoirCapacityM3;
        public float ReservoirInflowM3PerHour;
        public float PumpCapacityLps;
        public float CompressorCapacityM3Min;
        public float PowerAvailableKw;
        public float WaterUsedSeasonM3;
        public float SnowMadeSeasonM3;
        public float KwhSeason;
        public float WaterUsedTodayM3;
        public float KwhToday;
        public bool SystemAuto = true;

        public SnowGunState Gun(int id)
        {
            for (int i = 0; i < Guns.Count; i++) if (Guns[i].Id == id) return Guns[i];
            return null;
        }
        public Hydrant HydrantById(int id)
        {
            for (int i = 0; i < Hydrants.Count; i++) if (Hydrants[i].Id == id) return Hydrants[i];
            return null;
        }
    }

    public struct GunPlacedEvent { public int GunId; }
    public struct GunRemovedEvent { public int GunId; }
    public struct GunStateEvent { public int GunId; public bool Running; public string Status; }
    public struct SnowmakingHourEvent { public float WaterM3; public float Kwh; public float SnowM3; public float WetBulbC; }
}
