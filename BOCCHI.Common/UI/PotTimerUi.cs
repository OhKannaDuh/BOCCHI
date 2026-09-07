using BOCCHI.Common.Data.Zones;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Ocelot.Services.Translation;
using Ocelot.Windows;
using XIVFate = Lumina.Excel.Sheets.Fate;

namespace BOCCHI.Common.UI;

/// <summary>
///     Local pot FATE countdown(s) from <see cref="IPotCycleTracker"/>.
///     South Horn and North Horn are tracked separately and stay visible after you leave the zone.
/// </summary>
public static class PotTimerUi
{
    public static void Draw(
        IPotCycleTracker potCycle,
        IZoneProvider zones,
        IDataManager data,
        ITranslator<MainWindow> translator)
    {
        ExcelSheet<XIVFate> sheet = data.GetExcelSheet<XIVFate>();
        IReadOnlyList<PotCycleSnapshot> known = potCycle.KnownCycles;

        if (known.Count == 0)
        {
            if (!zones.GetZone().IsOccultCrescentZone())
            {
                return;
            }

            BocchiUi.MutedText(translator.T(".pot_timer.unknown"));
            return;
        }

        foreach (PotCycleSnapshot snap in known)
        {
            DrawOne(snap, sheet, translator);
        }
    }

    /// <summary>One-line status text for all known zone cycles, or null when nothing to show.</summary>
    public static string? FormatCompact(
        IPotCycleTracker potCycle,
        IDataManager data,
        ITranslator<MainWindow> translator)
    {
        ExcelSheet<XIVFate> sheet = data.GetExcelSheet<XIVFate>();
        IReadOnlyList<PotCycleSnapshot> known = potCycle.KnownCycles;
        if (known.Count == 0)
        {
            return null;
        }

        List<string> parts = [];
        foreach (PotCycleSnapshot snap in known)
        {
            string zone = ZoneShort(snap.TerritoryTypeId, translator);
            if (snap.CurrentActivePotFateId != 0)
            {
                string active = GeoShort(snap.CurrentActivePotFateId, translator) is { } geo
                    ? $"{zone} {geo} {translator.T(".pot_timer.chip_active")}"
                    : $"{zone} {translator.T(".pot_timer.chip_active")}";
                parts.Add(active);
                continue;
            }

            if (!snap.HasPredictedNextPot)
            {
                continue;
            }

            string next = GeoShort(snap.PredictedNextPotFateId, translator) is { } nextGeo
                ? $"{zone} {nextGeo} {FormatClock(Remaining(snap))}"
                : $"{zone} {FormatClock(Remaining(snap))}";
            parts.Add(next);
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static void DrawOne(
        PotCycleSnapshot snap,
        ExcelSheet<XIVFate> sheet,
        ITranslator<MainWindow> translator)
    {
        string zone = ZoneLabel(snap.TerritoryTypeId, translator);

        if (snap.CurrentActivePotFateId != 0)
        {
            BocchiUi.LabelledValue(
                $"{zone} — {translator.T(".pot_timer.active")}",
                FateLabel(sheet, snap.CurrentActivePotFateId, translator));
            return;
        }

        if (!snap.HasPredictedNextPot)
        {
            return;
        }

        BocchiUi.LabelledValue(
            $"{zone} — {translator.T(".pot_timer.next")}",
            $"{FateLabel(sheet, snap.PredictedNextPotFateId, translator)} · {FormatClock(Remaining(snap))}");
    }

    private static TimeSpan Remaining(PotCycleSnapshot snap)
    {
        TimeSpan remaining = snap.PredictedNextSpawnAt - DateTimeOffset.UtcNow;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private static string ZoneLabel(ushort territoryTypeId, ITranslator<MainWindow> translator) =>
        (ZoneId)territoryTypeId switch
        {
            ZoneId.SouthHorn => translator.T(".pot_timer.south_horn"),
            ZoneId.NorthHorn => translator.T(".pot_timer.north_horn"),
            _ => $"#{territoryTypeId}",
        };

    private static string ZoneShort(ushort territoryTypeId, ITranslator<MainWindow> translator) =>
        (ZoneId)territoryTypeId switch
        {
            ZoneId.SouthHorn => translator.T(".pot_timer.south_horn_short"),
            ZoneId.NorthHorn => translator.T(".pot_timer.north_horn_short"),
            _ => $"#{territoryTypeId}",
        };

    private static string FateLabel(
        ExcelSheet<XIVFate> sheet,
        int fateId,
        ITranslator<MainWindow> translator)
    {
        string name = FateName(sheet, fateId);
        return GeoLabel(fateId, translator) is { } geo ? $"{name} ({geo})" : name;
    }

    private static string FateName(ExcelSheet<XIVFate> sheet, int fateId)
    {
        try
        {
            string name = sheet.GetRow((uint)fateId).Name.ToString();
            return string.IsNullOrWhiteSpace(name) ? $"#{fateId}" : name;
        }
        catch
        {
            return $"#{fateId}";
        }
    }

    private static string? GeoLabel(int fateId, ITranslator<MainWindow> translator) =>
        PotFateGeography.TryGetSide(fateId, out PotFateGeography.Side side)
            ? side switch
            {
                PotFateGeography.Side.North => translator.T(".pot_timer.geo_north"),
                PotFateGeography.Side.South => translator.T(".pot_timer.geo_south"),
                _ => null,
            }
            : null;

    private static string? GeoShort(int fateId, ITranslator<MainWindow> translator) =>
        PotFateGeography.TryGetSide(fateId, out PotFateGeography.Side side)
            ? side switch
            {
                PotFateGeography.Side.North => translator.T(".pot_timer.geo_north_short"),
                PotFateGeography.Side.South => translator.T(".pot_timer.geo_south_short"),
                _ => null,
            }
            : null;

    private static string FormatClock(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return $"{(int)value.TotalHours}:{value.Minutes:D2}:{value.Seconds:D2}";
        }

        return $"{value.Minutes:D2}:{value.Seconds:D2}";
    }
}
