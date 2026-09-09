using BOCCHI.Common.Data.Zones;

namespace BOCCHI.Common.Data.EventDrops;

/// <summary>
///     Authored FATE/CE reward icons (demiatma / notes / soul shards) keyed by Occult zone.
/// </summary>
public static class OccultEventDrops
{
    private static readonly Dictionary<ZoneId, Dictionary<uint, EventDropInfo>> Fates = new()
    {
        [ZoneId.SouthHorn] = new()
        {
            [1962] = new(Demiatma.Azurite, null, null),
            [1963] = new(Demiatma.Azurite, null, null),
            [1964] = new(Demiatma.Orpiment, null, null),
            [1965] = new(Demiatma.Realgar, null, null),
            [1966] = new(Demiatma.Malachite, null, null),
            [1967] = new(Demiatma.Realgar, null, null),
            [1968] = new(Demiatma.Verdigris, null, null),
            [1969] = new(Demiatma.Verdigris, null, null),
            [1970] = new(Demiatma.Azurite, null, null),
            [1971] = new(Demiatma.Orpiment, null, null),
            [1972] = new(Demiatma.CaputMortuum, null, null),
            [1976] = new(Demiatma.Orpiment, MonsterNote.PersistentPots, null),
            [1977] = new(Demiatma.Verdigris, MonsterNote.PersistentPots, null),
        },
        [ZoneId.NorthHorn] = new()
        {
            [2072] = new(null, MonsterNote.PersistentPots, null), // Daylight Pottery
            [2073] = new(null, MonsterNote.PersistentPots, null), // In a Pot of Bother
        },
    };

    private static readonly Dictionary<ZoneId, Dictionary<uint, EventDropInfo>> CriticalEncounters = new()
    {
        [ZoneId.SouthHorn] = new()
        {
            [33] = new(Demiatma.Azurite, null, null),
            [34] = new(Demiatma.Orpiment, MonsterNote.BlackChocobos, SoulShard.Ranger),
            [35] = new(Demiatma.Azurite, MonsterNote.CrescentBerserker, SoulShard.Berserker),
            [36] = new(Demiatma.Azurite, null, null),
            [37] = new(Demiatma.Verdigris, MonsterNote.CloisterDemon, null),
            [38] = new(Demiatma.Malachite, null, null),
            [39] = new(Demiatma.Malachite, MonsterNote.MythicIdol, null),
            [40] = new(Demiatma.CaputMortuum, null, null),
            [41] = new(Demiatma.Realgar, MonsterNote.NymianPotaladus, null),
            [42] = new(Demiatma.CaputMortuum, null, SoulShard.Oracle),
            [43] = new(Demiatma.Realgar, null, null),
            [44] = new(Demiatma.Orpiment, null, null),
            [45] = new(Demiatma.Realgar, MonsterNote.TradeTortoise, null),
            [46] = new(Demiatma.CaputMortuum, null, null),
            [47] = new(Demiatma.Malachite, null, null),
            // 48 Forked Tower — no drop icons
        },
        [ZoneId.NorthHorn] = new()
        {
            [50] = new(null, MonsterNote.ConjuredCalofisteri, null), // Doubled Trouble
            [51] = new(null, MonsterNote.AlabasterBlade, null), // Quarried Away
            [52] = new(null, MonsterNote.Arbatel, null), // Forbidden Folios
            [53] = new(null, MonsterNote.ClaretDragon, null), // Cursed Resurgence
            [54] = new(null, MonsterNote.Algol, null), // Imbalanced Diet
            [57] = new(null, MonsterNote.PhantomNecromancer, null), // Dark Artistry
            [59] = new(null, MonsterNote.Pallmagia, null), // Appalling Behavior
            [60] = new(null, MonsterNote.TinyMage, null), // Tiny Terror
            [61] = new(null, MonsterNote.Abductor, null), // Lost on the Wind
            [63] = new(null, MonsterNote.Metamorph, null), // Accept No Imitators
        },
    };

    public static bool TryGetFate(ZoneId zoneId, uint fateId, out EventDropInfo drops)
    {
        if (Fates.TryGetValue(zoneId, out Dictionary<uint, EventDropInfo>? table)
            && table.TryGetValue(fateId, out drops))
        {
            return true;
        }

        drops = default;
        return false;
    }

    public static bool TryGetCriticalEncounter(ZoneId zoneId, uint encounterId, out EventDropInfo drops)
    {
        if (CriticalEncounters.TryGetValue(zoneId, out Dictionary<uint, EventDropInfo>? table)
            && table.TryGetValue(encounterId, out drops))
        {
            return true;
        }

        drops = default;
        return false;
    }

    /// <summary>Looks up a note across South and North Horn FATE tables (id space does not overlap).</summary>
    public static bool TryGetNoteForFate(uint fateId, out MonsterNote note)
    {
        foreach (Dictionary<uint, EventDropInfo> table in Fates.Values)
        {
            if (table.TryGetValue(fateId, out EventDropInfo drops) && drops.Notes is { } n)
            {
                note = n;
                return true;
            }
        }

        note = default;
        return false;
    }

    /// <summary>Looks up a note across South and North Horn CE tables (id space does not overlap).</summary>
    public static bool TryGetNoteForCriticalEncounter(uint encounterId, out MonsterNote note)
    {
        foreach (Dictionary<uint, EventDropInfo> table in CriticalEncounters.Values)
        {
            if (table.TryGetValue(encounterId, out EventDropInfo drops) && drops.Notes is { } n)
            {
                note = n;
                return true;
            }
        }

        note = default;
        return false;
    }
}
