using BOCCHI.Common.Config.Fields;
using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

/// <summary>Illegal Mode FATE allowlist (not used by Pots &amp; Treasure for pot selection).</summary>
[Serializable]
[ConfigGroup("automation", GroupOrder = 0, Order = 2)]
public class FatesConfig : IAutoConfig
{
    /// <summary>South / North Horn Magic Pot FATE ids (Persistent / Pleading / Daylight / Pot of Bother).</summary>
    public static readonly uint[] PotFateIds = [1976, 1977, 2072, 2073];

    [DisabledFateIds(Order = 0, Section = "allowlist")]
    public HashSet<uint> DisabledFateIds { get; set; } =
    [
        // South Horn — dangerous / usually skipped by default
        1965 // The Winged Terror
    ];

    public bool IsFateEnabled(uint fateId) => !DisabledFateIds.Contains(fateId);

    /// <summary>
    ///     Illegal Mode always respects Allowed FATEs. Pot options only affect priority,
    ///     prepositioning, and post-FATE chest farming for Magic Pot FATEs that are still enabled.
    /// </summary>
    public bool IsFateEnabledForIllegalMode(uint fateId, bool isPotFate, bool preferPotFates, bool shouldFarmPotChests)
        => IsFateEnabled(fateId);

    /// <summary>
    ///     Pot fallback cutoffs only apply when pot farming is on AND the predicted next pot FATE is enabled.
    ///     Disabled pot FATEs must not idle the automator near spawn.
    /// </summary>
    public bool IsPotFallbackGatingEnabled(
        uint predictedNextPotFateId,
        bool shouldDoFates,
        bool preferPotFates,
        bool shouldFarmPotChests)
    {
        if (!shouldDoFates || (!shouldFarmPotChests && !preferPotFates))
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
            preferPotFates,
            shouldFarmPotChests);
    }
}
