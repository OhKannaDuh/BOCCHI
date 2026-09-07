using BOCCHI.Common.Config.Fields;
using Newtonsoft.Json;
using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

/// <summary>Illegal Mode FATE allowlist and skip-by-progress.</summary>
[Serializable]
[ConfigGroup("automation", GroupOrder = 0, Order = 2)]
public class FatesConfig : IAutoConfig
{
    /// <summary>
    ///     Skip FATEs at or above this progress % (0 = disabled). Once you are in the FATE, it is finished.
    /// </summary>
    [IntRange(0, 100, Order = 0, Section = "skip")]
    public int MaxFateProgressPercent { get; set; } = 50;

    /// <summary>
    ///     FATEs the player turned off. Replace on load — Newtonsoft's default appends into the
    ///     property initializer, which re-disabled The Winged Terror (#1965) every reload after
    ///     the user had enabled it (empty JSON array left the default id in place).
    /// </summary>
    [DisabledFateIds(Order = 1, Section = "allowlist")]
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public HashSet<uint> DisabledFateIds { get; set; } = [];

    public bool IsFateEnabled(uint fateId) => !DisabledFateIds.Contains(fateId);

    /// <summary>True when a live FATE should not be started / pathing to — already registered FATEs stay.</summary>
    public bool ShouldSkipByProgress(byte progress) =>
        MaxFateProgressPercent > 0 && progress >= MaxFateProgressPercent;

    /// <summary>Prefer pot FATEs force-includes Magic Pots; farm pot chests does not.</summary>
    public bool IsFateEnabledForIllegalMode(uint fateId, bool isPotFate, bool preferPotFates)
    {
        if (IsFateEnabled(fateId))
        {
            return true;
        }

        return isPotFate && preferPotFates;
    }

    /// <summary>
    ///     Pot timing applies when farm pot chests or wait-near-pots is on, and that pot is allowed.
    ///     Prefer pot FATEs alone does not turn this on.
    /// </summary>
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
