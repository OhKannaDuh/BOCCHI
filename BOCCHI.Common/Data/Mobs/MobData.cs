using BOCCHI.Common.Data.Zones;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System.Globalization;

namespace BOCCHI.Common.Data.Mobs;

public static class MobData
{
    public const uint NorthHornMinNameId = 14857;

    private static readonly Dictionary<Mob, string> NameCache = [];

    private static readonly Dictionary<Mob, Mob> LegacyToCrescent = new()
    {
        { Mob.Goobbue, Mob.Goobbue2 },
        { Mob.Taurus, Mob.Taurus2 },
        { Mob.Headstone, Mob.Headstone2 },
        { Mob.Garula, Mob.Garula2 },
        { Mob.VoidViper, Mob.VoidViper2 }
    };

    private static readonly HashSet<Mob> HiddenLegacyMobs = LegacyToCrescent.Keys.ToHashSet();

    public static readonly IReadOnlySet<Mob> MobsWithSpawnCondition = new HashSet<Mob>
    {
        // South Horn
        Mob.Armor,
        Mob.Bomb,
        Mob.Caoineag,
        Mob.Dhruva,
        Mob.Dullahan,
        Mob.Fool,
        Mob.Geshunpest,
        Mob.Ghost,
        Mob.Gourmand,
        Mob.Mimic,
        Mob.Mousse,
        Mob.Troubadour,
        // North Horn (night)
        Mob.Bicephalus,
        Mob.Glutton,
        Mob.Ankou,
        Mob.Geshunpest2,
        Mob.Necrodium,
        Mob.Rothound,
        Mob.Skatene
    };

    public static ZoneId GetZone(Mob mob) =>
        (uint)mob >= NorthHornMinNameId ? ZoneId.NorthHorn : ZoneId.SouthHorn;

    public static IEnumerable<Mob> GetSelectableMobs(ZoneId? zone = null)
    {
        IEnumerable<Mob> mobs = Enum.GetValues<Mob>().Where(m => !HiddenLegacyMobs.Contains(m));
        if (zone is { } filter && filter is ZoneId.SouthHorn or ZoneId.NorthHorn)
        {
            mobs = mobs.Where(m => GetZone(m) == filter);
        }

        return mobs;
    }

    public static bool TryFromNameId(uint nameId, out Mob mob)
    {
        mob = (Mob)nameId;
        if (Enum.IsDefined(mob))
        {
            return true;
        }

        mob = default;
        return false;
    }

    public static bool IsSpecialMob(uint nameId) =>
        TryFromNameId(nameId, out Mob mob) && MobsWithSpawnCondition.Contains(mob);

    public static void ReplaceLegacyMobs(List<Mob> mobs)
    {
        for (int i = mobs.Count - 1; i >= 0; i--)
        {
            if (!LegacyToCrescent.TryGetValue(mobs[i], out Mob crescent))
            {
                continue;
            }

            if (mobs.Contains(crescent))
            {
                mobs.RemoveAt(i);
            }
            else
            {
                mobs[i] = crescent;
            }
        }
    }

    public static bool IsSelected(uint nameId, IReadOnlyCollection<Mob> selected)
    {
        if (!TryFromNameId(nameId, out Mob mob))
        {
            return false;
        }

        if (selected.Contains(mob))
        {
            return true;
        }

        if (LegacyToCrescent.TryGetValue(mob, out Mob crescent) && selected.Contains(crescent))
        {
            return true;
        }

        foreach((Mob legacy, Mob crescentMob) in LegacyToCrescent)
        {
            if (mob == crescentMob && selected.Contains(legacy))
            {
                return true;
            }
        }

        return false;
    }

    public static string GetDisplayName(Mob mob, IDataManager data)
    {
        if (NameCache.TryGetValue(mob, out string? cached))
        {
            return FormatDisplayName(mob, cached);
        }

        if (data.GetExcelSheet<BNpcName>().TryGetRow((uint)mob, out BNpcName row))
        {
            string titleCase = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(row.Singular.ToString().ToLower());
            NameCache[mob] = titleCase;
            return FormatDisplayName(mob, titleCase);
        }

        return mob.ToString();
    }

    public static bool MatchesSearch(Mob mob, string search, IDataManager data)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        string displayName = GetDisplayName(mob, data);

        return displayName.Contains(search, comparison)
               || mob.ToString().Contains(search, comparison)
               || ((uint)mob).ToString().Contains(search, comparison);
    }

    private static string FormatDisplayName(Mob mob, string baseName)
    {
        // Sheet Singular is already "crescent taurus" / "crescent aetherscab" — don't double-suffix.
        if (GetZone(mob) == ZoneId.NorthHorn)
        {
            return $"{baseName} (North Horn)";
        }

        return baseName;
    }
}
