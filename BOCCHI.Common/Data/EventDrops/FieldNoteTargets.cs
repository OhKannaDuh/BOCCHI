using BOCCHI.Common.Data.Zones;
using System.Numerics;

namespace BOCCHI.Common.Data.EventDrops;

/// <summary>
///     Occult Record sources for Completionist Mode (note-filtered automation + survey / tower display).
/// </summary>
public static class FieldNoteTargets
{
    /// <param name="MkdLoreId">MKDLore / Occult Record row id.</param>
    /// <param name="DisplayOnly">True for survey points and Forked Tower — not auto-chosen by Completionist.</param>
    /// <param name="Note">Notes item when the record drops from a FATE/CE/tower; null for survey points.</param>
    /// <param name="MapX">In-game map X for click-to-flag (survey points).</param>
    /// <param name="MapY">In-game map Y for click-to-flag (survey points).</param>
    /// <param name="WorldPosition">
    ///     Authored world XZ for Ctrl+click travel (Y may be 0 — floored at path time).
    ///     Derived from map coords with SizeFactor 100 / Offset 0 (OC Horn maps).
    /// </param>
    public readonly record struct Entry(
        uint MkdLoreId,
        string SourceKey,
        bool DisplayOnly,
        MonsterNote? Note,
        IReadOnlyList<uint> FateIds,
        IReadOnlyList<uint> CriticalEncounterIds,
        float? MapX = null,
        float? MapY = null,
        Vector3? WorldPosition = null)
    {
        public bool CanFlag => MapX is not null && MapY is not null;

        public bool CanPath => WorldPosition is not null;
    }

    private static Entry Survey(uint mkdLoreId, float mapX, float mapY, Vector3 world) =>
        new(mkdLoreId, "survey_point", true, null, [], [], mapX, mapY, world);

    private static readonly Entry[] SouthHorn =
    [
        // FATE / CE (Illegal Mode can pursue)
        new(11, "persistent_pots", false, MonsterNote.PersistentPots, [1976, 1977], []),
        new(16, "black_regiment", false, MonsterNote.BlackChocobos, [], [34]),
        new(17, "the_unbridled", false, MonsterNote.CrescentBerserker, [], [35]),
        new(20, "calamity_bound", false, MonsterNote.CloisterDemon, [], [37]),
        new(14, "from_times_bygone", false, MonsterNote.MythicIdol, [], [39]),
        new(10, "shark_attack", false, MonsterNote.NymianPotaladus, [], [41]),
        new(8, "cursed_concern", false, MonsterNote.TradeTortoise, [], [45]),

        // Open-world survey points — click flags; Ctrl+click cost-routes to WorldPosition
        Survey(5, 38.6f, 7.6f, new(854.17f, 0f, -694.32f)), // Expedition Base Camp
        Survey(7, 31.4f, 17.0f, new(494.52f, 0f, -224.78f)), // Lost Citadel
        Survey(9, 20.2f, 12.2f, new(-64.94f, 0f, -464.55f)), // Vanishing Slope
        Survey(12, 23.2f, 21.5f, new(84.92f, 0f, 0f)), // Heathcliff
        Survey(13, 10.2f, 22.5f, new(-564.45f, 0f, 49.95f)), // Fell Warren
        Survey(15, 24.2f, 32.8f, new(134.87f, 0f, 564.45f)), // Shadowed City
        Survey(18, 18.5f, 33.8f, new(-149.85f, 0f, 614.4f)), // Eldergrowth
        Survey(19, 15.6f, 29.5f, new(-294.71f, 0f, 399.61f)), // Abandoned Ascent
        Survey(21, 36.6f, 33.7f, new(754.26f, 0f, 609.4f)), // Silent Streets
        Survey(22, 36.0f, 22.6f, new(724.29f, 0f, 54.95f)), // Ancient Stoneworks
        Survey(23, 3.8f, 5.8f, new(-884.14f, 0f, -784.23f)), // Wanderer's Haven
        Survey(24, 8.7f, 35.9f, new(-639.38f, 0f, 719.3f)), // Brazier

        // Forked Tower: Blood (checklist only)
        new(27, "forked_tower_blood", true, MonsterNote.DeadStars, [], []),
        new(28, "forked_tower_blood", true, MonsterNote.MarbleDragon, [], []),
        new(29, "forked_tower_blood", true, MonsterNote.Magitaur, [], []),
        new(30, "forked_tower_blood", true, null, [], []), // Secluded Stacks (inside tower)
    ];

    private static readonly Entry[] NorthHorn =
    [
        // FATE / CE
        new(11, "persistent_pots_nh", false, MonsterNote.PersistentPots, [2072, 2073], []),
        new(53, "doubled_trouble", false, MonsterNote.ConjuredCalofisteri, [], [50]),
        new(50, "quarried_away", false, MonsterNote.AlabasterBlade, [], [51]),
        new(33, "forbidden_folios", false, MonsterNote.Arbatel, [], [52]),
        new(48, "cursed_resurgence", false, MonsterNote.ClaretDragon, [], [53]),
        new(35, "imbalanced_diet", false, MonsterNote.Algol, [], [54]),
        new(42, "dark_artistry", false, MonsterNote.PhantomNecromancer, [], [57]),
        new(41, "appalling_behavior", false, MonsterNote.Pallmagia, [], [59]),
        new(34, "tiny_terror", false, MonsterNote.TinyMage, [], [60]),
        new(44, "lost_on_the_wind", false, MonsterNote.Abductor, [], [61]),
        new(38, "accept_no_imitators", false, MonsterNote.Metamorph, [], [63]),

        // Open-world survey points
        Survey(31, 39.1f, 38.0f, new(879.14f, 0f, 824.2f)), // North Horn Base Camp
        Survey(32, 36.4f, 32.2f, new(744.27f, 0f, 534.48f)), // Crown of Karnak
        Survey(36, 27.6f, 26.3f, new(304.7f, 0f, 239.77f)), // Shadowdeep
        Survey(37, 39.7f, 22.6f, new(909.11f, 0f, 54.95f)), // Labyrinth of the Damned
        Survey(39, 27.0f, 14.0f, new(274.73f, 0f, -374.63f)), // Panoptical Pillar
        Survey(40, 40.0f, 3.9f, new(924.1f, 0f, -879.14f)), // Sinking Sanctuary
        Survey(43, 17.5f, 5.2f, new(-199.8f, 0f, -814.2f)), // Marrowood
        Survey(45, 11.1f, 38.8f, new(-519.49f, 0f, 864.16f)), // Suspended Masonry
        Survey(46, 4.8f, 36.4f, new(-834.19f, 0f, 744.27f)), // Windward Watch
        Survey(47, 3.4f, 24.5f, new(-904f, 0f, 151f)), // Shrine of Spheres
        Survey(49, 7.4f, 14.0f, new(-704.31f, 0f, -374.63f)), // Moldering Streets
        Survey(51, 4.0f, 3.4f, new(-874f, 0f, -904f)), // Moldering Lake
        Survey(52, 21.3f, 19.7f, new(-9.99f, 0f, -89.91f)), // Unhallowed Hamlet
        Survey(54, 22.7f, 23.9f, new(59.94f, 0f, 119.88f)), // Dark Territory

        // Forked Tower: Magic
        new(55, "forked_tower_magic", true, MonsterNote.TwoHeadedAevis, [], []),
        new(56, "forked_tower_magic", true, MonsterNote.SwordDancer, [], []),
        new(57, "forked_tower_magic", true, MonsterNote.Necrophobia, [], []),
        new(58, "forked_tower_magic", true, MonsterNote.Index, [], []),
        new(60, "forked_tower_magic", true, null, [], []), // Concealed Collection
    ];

    public static IReadOnlyList<Entry> ChecklistFor(ZoneId zoneId) =>
        zoneId switch
        {
            ZoneId.SouthHorn => SouthHorn,
            ZoneId.NorthHorn => NorthHorn,
            _ => [],
        };

    public static bool TryGetNoteForFate(uint fateId, out MonsterNote note) =>
        OccultEventDrops.TryGetNoteForFate(fateId, out note);

    public static bool TryGetNoteForCriticalEncounter(uint encounterId, out MonsterNote note) =>
        OccultEventDrops.TryGetNoteForCriticalEncounter(encounterId, out note);

    public static bool TryGetDropsForFate(ZoneId zoneId, uint fateId, out EventDropInfo drops) =>
        OccultEventDrops.TryGetFate(zoneId, fateId, out drops);

    public static bool TryGetDropsForCriticalEncounter(ZoneId zoneId, uint encounterId, out EventDropInfo drops) =>
        OccultEventDrops.TryGetCriticalEncounter(zoneId, encounterId, out drops);
}
