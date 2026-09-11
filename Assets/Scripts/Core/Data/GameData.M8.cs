using System.Collections.Generic;
using AlpineSim.Core.Scaffold;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Data
{
    public sealed partial class GameData
    {
        /// <summary>M8 scaffold tables. Optional files; schemas are final, systems are not built.</summary>
        public List<CampaignBeatDef> CampaignBeats { get; private set; } = new List<CampaignBeatDef>();
        public List<RegionTileDef> RegionTiles { get; private set; } = new List<RegionTileDef>();

        partial void LoadM8()
        {
            var c = ReadJsonOptional("campaign.json");
            if (c != null && c.Has("beats")) CampaignBeats = JsonMapper.FromJson<List<CampaignBeatDef>>(c["beats"]);
            var r = ReadJsonOptional("regions.json");
            if (r != null && r.Has("tiles")) RegionTiles = JsonMapper.FromJson<List<RegionTileDef>>(r["tiles"]);
        }
    }
}
