using BOCCHI.Common.Config.Fields;
using Newtonsoft.Json;
using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("automation", GroupOrder = 0, Order = 2)]
public class FatesConfig : IAutoConfig
{
    [IntRange(0, 100, Order = 0, Section = "skip")]
    public int MaxFateProgressPercent { get; set; } = 50;

    [DisabledFateIds(Order = 1, Section = "allowlist")]
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public HashSet<uint> DisabledFateIds { get; set; } = [];

    public bool IsFateEnabled(uint fateId) => !DisabledFateIds.Contains(fateId);

    public bool ShouldSkipByProgress(byte progress) =>
        MaxFateProgressPercent > 0 && progress >= MaxFateProgressPercent;

    public bool IsFateEnabledForIllegalMode(uint fateId, bool isPotFate, bool preferPotFates)
    {
        if (IsFateEnabled(fateId))
        {
            return true;
        }

        return isPotFate && preferPotFates;
    }

    public bool IsPotFallbackGatingEnabled(
        uint predictedNextPotFateId,
        bool shouldDoFates,
        bool preferPotFates,
        bool shouldFarmPotChests,
        bool shouldPrepositionToPots)
    {
        if (!shouldDoFates || (!shouldFarmPotChests && !shouldPrepositionToPots))
        {
            return false;
        }

        if (predictedNextPotFateId == 0)
        {
            return false;
        }

        return IsFateEnabledForIllegalMode(
            predictedNextPotFateId,
            isPotFate: true,
            preferPotFates);
    }
}
