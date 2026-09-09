using System.Text;
using AlpineSim.Core.Serialization;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Save
{
    /// <summary>
    /// 64-bit FNV-1a over the canonical (sorted-key, compact) JSON form of WorldState.
    /// Used by the determinism test and by the debug HUD.
    /// </summary>
    public static class StateHasher
    {
        public static ulong Hash(WorldState world)
        {
            string canonical = JsonWriter.Write(JsonMapper.ToJson(world), false, true);
            return Fnv1a64(canonical);
        }

        public static ulong Fnv1a64(string s)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                var bytes = Encoding.UTF8.GetBytes(s);
                for (int i = 0; i < bytes.Length; i++)
                {
                    h ^= bytes[i];
                    h *= 1099511628211UL;
                }
                return h;
            }
        }

        public static ulong Fnv1a64(byte[] bytes, ulong seed = 14695981039346656037UL)
        {
            unchecked
            {
                ulong h = seed;
                for (int i = 0; i < bytes.Length; i++)
                {
                    h ^= bytes[i];
                    h *= 1099511628211UL;
                }
                return h;
            }
        }
    }
}
