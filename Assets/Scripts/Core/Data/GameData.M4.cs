using AlpineSim.Core.Construction;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Data
{
    public sealed partial class GameData
    {
        public ConstructionData Construction { get; private set; } = new ConstructionData();

        partial void LoadM4()
        {
            var c = ReadJsonOptional("construction.json");
            if (c != null) Construction = JsonMapper.FromJson<ConstructionData>(c);
        }

        partial void ValidateM4()
        {
            if (Construction.StagesByFamily.Count == 0) Warnings.Add("construction.json defines no stage chains");
        }
    }
}
