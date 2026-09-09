using System.Collections.Generic;
using AlpineSim.Core.Economy;
using AlpineSim.Core.Guests;
using AlpineSim.Core.Lifts;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Data
{
    public sealed partial class GameData
    {
        public List<LiftTypeDef> LiftTypes { get; private set; } = new List<LiftTypeDef>();
        public List<LiftOptionDef> LiftOptions { get; private set; } = new List<LiftOptionDef>();
        public GuestsData Guests { get; private set; } = new GuestsData();
        public EconomyData Economy { get; private set; } = new EconomyData();

        private readonly Dictionary<string, LiftTypeDef> _liftTypesById = new Dictionary<string, LiftTypeDef>();

        partial void LoadM3()
        {
            var l = ReadJsonOptional("lifts.json");
            if (l != null)
            {
                LiftTypes = JsonMapper.FromJson<List<LiftTypeDef>>(l["lifts"]);
                if (l.Has("options")) LiftOptions = JsonMapper.FromJson<List<LiftOptionDef>>(l["options"]);
            }
            var g = ReadJsonOptional("guests.json");
            if (g != null) Guests = JsonMapper.FromJson<GuestsData>(g);
            var e = ReadJsonOptional("economy.json");
            if (e != null) Economy = JsonMapper.FromJson<EconomyData>(e);
            _liftTypesById.Clear();
            foreach (var d in LiftTypes) _liftTypesById[d.Id] = d;
        }

        partial void ValidateM3()
        {
            var ids = new HashSet<string>();
            foreach (var d in LiftTypes)
            {
                if (string.IsNullOrEmpty(d.Id)) throw new DataLoadException("lifts.json: a lift type has no id");
                if (!ids.Add(d.Id)) throw new DataLoadException("lifts.json: duplicate id " + d.Id);
                if (d.CapacityPph <= 0) throw new DataLoadException("lift " + d.Id + " has no capacity");
                foreach (var o in d.Options) if (LiftOption(o) == null) throw new DataLoadException("lift " + d.Id + " references unknown option " + o);
            }
            foreach (var a in Guests.Archetypes) if (a.Share <= 0) Warnings.Add("guest archetype " + a.Id + " has zero share");
        }

        public LiftTypeDef LiftType(string id) => id != null && _liftTypesById.TryGetValue(id, out var d) ? d : null;
        public LiftTypeDef RequireLiftType(string id) => LiftType(id) ?? throw new DataLoadException("Unknown lift type '" + id + "'");
        public LiftOptionDef LiftOption(string id)
        {
            foreach (var o in LiftOptions) if (o.Id == id) return o;
            return null;
        }
    }
}
