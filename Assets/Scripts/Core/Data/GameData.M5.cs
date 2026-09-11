using AlpineSim.Core.Serialization;
using AlpineSim.Core.Weather;

namespace AlpineSim.Core.Data
{
    public sealed partial class GameData
    {
        public ClimateProfile Climate { get; private set; } = new ClimateProfile();

        partial void LoadM5()
        {
            var c = ReadJsonOptional("climate.json");
            if (c != null) Climate = JsonMapper.FromJson<ClimateProfile>(c);
        }

        partial void ValidateM5()
        {
            if (Climate.Periods.Count == 0) Warnings.Add("climate.json defines no periods");
        }
    }
}
