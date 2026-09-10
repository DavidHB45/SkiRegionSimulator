using System.Collections.Generic;
using AlpineSim.Core.Scaffold;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Data
{
    public sealed partial class GameData
    {
        /// <summary>M7 scaffold tables. Optional files; schemas are final, systems are not built.</summary>
        public List<WinchAnchorDef> WinchAnchors { get; private set; } = new List<WinchAnchorDef>();
        public List<ParkFeatureDef> ParkFeatures { get; private set; } = new List<ParkFeatureDef>();
        public List<AvalanchePathDef> AvalanchePaths { get; private set; } = new List<AvalanchePathDef>();

        partial void LoadM7()
        {
            var w = ReadJsonOptional("winch.json");
            if (w != null && w.Has("anchors")) WinchAnchors = JsonMapper.FromJson<List<WinchAnchorDef>>(w["anchors"]);
            var p = ReadJsonOptional("park.json");
            if (p != null && p.Has("features")) ParkFeatures = JsonMapper.FromJson<List<ParkFeatureDef>>(p["features"]);
            var a = ReadJsonOptional("avalanche.json");
            if (a != null && a.Has("paths")) AvalanchePaths = JsonMapper.FromJson<List<AvalanchePathDef>>(a["paths"]);
        }
    }
}
