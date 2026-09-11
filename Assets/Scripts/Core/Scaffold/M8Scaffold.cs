using System;
using System.Collections.Generic;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Scaffold
{
    // ------------------------------------------------------------------------------------------
    // M8 scaffold: five-act campaign tuning, region expansion beyond the 4.2 km2 envelope, mod loading.
    // Interfaces and data schemas only; operations throw NotImplementedException.
    // ------------------------------------------------------------------------------------------

    /// <summary>Scripted campaign beat (campaign.json): an event fired when its trigger holds during an act.</summary>
    [Serializable]
    public sealed class CampaignBeatDef
    {
        public string Id = "";
        public int Act = 1;
        /// <summary>dayReached | cashBelow | reputationAbove | liftBuilt | seasonEnd</summary>
        public string Trigger = "dayReached";
        public float TriggerValue;
        public string Title = "";
        public string Text = "";
        public double CashEffect;
        public float ReputationEffect;
        public string UnlockLiftTypeId = "";
        public string UnlockVehicleId = "";
        public string Comment = "";
    }

    /// <summary>A neighbouring terrain tile the resort can expand into (regions.json).</summary>
    [Serializable]
    public sealed class RegionTileDef
    {
        public string Id = "";
        public string DisplayName = "";
        public int OffsetX;
        public int OffsetY;
        public float SizeM = 2048f;
        public double LeaseCost;
        public int MinAct = 4;
        public int TerrainSeedOffset;
        public string Comment = "";
    }

    /// <summary>A mod package manifest (mod.json inside a mod folder under persistentDataPath/mods).</summary>
    [Serializable]
    public sealed class ModManifest
    {
        public string Id = "";
        public string DisplayName = "";
        public string Version = "0.1";
        public string Author = "";
        /// <summary>Data files the mod overlays onto StreamingAssets/Data (same schema, merged by id).</summary>
        public List<string> DataOverlays = new List<string>();
        public List<string> Scenarios = new List<string>();
        public string MinGameVersion = "";
        public string Comment = "";
    }

    public interface ICampaign
    {
        IReadOnlyList<CampaignBeatDef> Beats(SimContext ctx);
        bool TryAdvanceAct(SimContext ctx, out string reason);
        IReadOnlyList<string> FiredBeats(SimContext ctx);
    }

    public interface IRegionExpansion
    {
        IReadOnlyList<RegionTileDef> Tiles(SimContext ctx);
        bool CanExpand(SimContext ctx, string tileId, out string reason);
        bool Expand(SimContext ctx, string tileId, out string reason);
    }

    public interface IModLoader
    {
        IReadOnlyList<ModManifest> Discover(string modsDirectory);
        void Apply(ModManifest mod, Data.GameData into);
    }

    /// <summary>Registered under the "Campaign" tick slot. No-op tick; every operation throws until M8 is built.</summary>
    public sealed class M8ScaffoldSystem : ISimSystem, ICampaign, IRegionExpansion, IModLoader
    {
        public string Name => "Campaign";
        public const string NotBuilt = "M8 (campaign tuning, region expansion, mod loading) is scaffolded but not implemented; see docs/BUILD_ORDER.md";

        public void Initialize(SimContext ctx, bool newGame) { }
        public void Tick(SimContext ctx, float dt) { }

        public IReadOnlyList<CampaignBeatDef> Beats(SimContext ctx) => ctx.Data.CampaignBeats;
        public bool TryAdvanceAct(SimContext ctx, out string reason) => throw new NotImplementedException(NotBuilt);
        public IReadOnlyList<string> FiredBeats(SimContext ctx) => throw new NotImplementedException(NotBuilt);

        public IReadOnlyList<RegionTileDef> Tiles(SimContext ctx) => ctx.Data.RegionTiles;
        public bool CanExpand(SimContext ctx, string tileId, out string reason) => throw new NotImplementedException(NotBuilt);
        public bool Expand(SimContext ctx, string tileId, out string reason) => throw new NotImplementedException(NotBuilt);

        public IReadOnlyList<ModManifest> Discover(string modsDirectory) => throw new NotImplementedException(NotBuilt);
        public void Apply(ModManifest mod, Data.GameData into) => throw new NotImplementedException(NotBuilt);
    }
}
