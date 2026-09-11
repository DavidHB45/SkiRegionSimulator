using AlpineSim.Core.Fleet;
using AlpineSim.Core.Serialization;

namespace AlpineSim.Core.Data
{
    public sealed partial class GameData
    {
        public OperatorsData Operators { get; private set; } = new OperatorsData();

        partial void LoadM6()
        {
            var o = ReadJsonOptional("operators.json");
            if (o != null) Operators = JsonMapper.FromJson<OperatorsData>(o);
        }

        partial void ValidateM6()
        {
            if (Operators.Licenses.Count == 0) Warnings.Add("operators.json defines no licenses");
        }
    }
}
