using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public static class JObjectExtensions
{
    extension(JObject self)
    {
        public bool BoolOr(string path, bool fallback)
        {
            return self.SelectToken(path)?.Value<bool>() ?? fallback;
        }

        public int IntOr(string path, int fallback)
        {
            return self.SelectToken(path)?.Value<int>() ?? fallback;
        }

        public uint UintOr(string path, uint fallback)
        {
            return self.SelectToken(path)?.Value<uint>() ?? fallback;
        }

        public float FloatOr(string path, float fallback)
        {
            return self.SelectToken(path)?.Value<float>() ?? fallback;
        }
    }
}
