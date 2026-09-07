using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace BOCCHI.Common.Data.OccultCrescent;

/// <summary>
///     Occult Crescent currency and cipher item ids from <see cref="MKDData"/>.
///     Fallbacks are the 7.3x row ids so inventory reads still work before excel init.
/// </summary>
public static class OccultCurrencies
{
    public static uint SilverPieceItemId { get; private set; } = 45043;

    public static uint GoldPieceItemId { get; private set; } = 45044;

    public static uint SilverObolItemId { get; private set; } = 51975;

    public static uint GoldObolItemId { get; private set; } = 51976;

    /// <summary>South Horn — Sanguine Cipher.</summary>
    public static uint SouthHornCipherItemId { get; private set; } = 47739;

    /// <summary>North Horn — Arcane Amulet.</summary>
    public static uint NorthHornCipherItemId { get; private set; } = 51977;

    public static void Initialize(IDataManager data)
    {
        ExcelSheet<MKDData> sheet = data.GetExcelSheet<MKDData>();
        ExcelSheet<Addon> addonsEn = data.GetExcelSheet<Addon>(ClientLanguage.English);

        uint? firstSilver = null;
        uint? firstGold = null;
        uint? firstCipher = null;
        uint? secondSilver = null;
        uint? secondGold = null;
        uint? secondCipher = null;
        var matchedZone = false;

        foreach (MKDData row in sheet)
        {
            uint silver = CurrencyId(row, 0);
            uint gold = CurrencyId(row, 1);
            if (silver == 0)
            {
                continue;
            }

            uint cipher = row.CipherItem.RowId;
            string zone = ZoneName(addonsEn, row);

            if (zone.Equals("South Horn", StringComparison.OrdinalIgnoreCase))
            {
                SilverPieceItemId = silver;
                GoldPieceItemId = gold;
                if (cipher != 0)
                {
                    SouthHornCipherItemId = cipher;
                }

                matchedZone = true;
            }
            else if (zone.Equals("North Horn", StringComparison.OrdinalIgnoreCase))
            {
                SilverObolItemId = silver;
                GoldObolItemId = gold;
                if (cipher != 0)
                {
                    NorthHornCipherItemId = cipher;
                }

                matchedZone = true;
            }

            if (firstSilver is null)
            {
                firstSilver = silver;
                firstGold = gold;
                firstCipher = cipher;
            }
            else if (secondSilver is null && silver != firstSilver)
            {
                secondSilver = silver;
                secondGold = gold;
                secondCipher = cipher;
            }
        }

        if (matchedZone)
        {
            return;
        }

        if (firstSilver is uint southSilver)
        {
            SilverPieceItemId = southSilver;
            GoldPieceItemId = firstGold ?? GoldPieceItemId;
            if (firstCipher is uint southCipher and not 0)
            {
                SouthHornCipherItemId = southCipher;
            }
        }

        if (secondSilver is uint northSilver)
        {
            SilverObolItemId = northSilver;
            GoldObolItemId = secondGold ?? GoldObolItemId;
            if (secondCipher is uint northCipher and not 0)
            {
                NorthHornCipherItemId = northCipher;
            }
        }
    }

    private static uint CurrencyId(MKDData row, int index)
    {
        if (index < 0 || index >= row.CurrencyItem.Count)
        {
            return 0;
        }

        return row.CurrencyItem[index].RowId;
    }

    private static string ZoneName(ExcelSheet<Addon> addonsEn, MKDData row)
    {
        if (!addonsEn.TryGetRow(row.ZoneName.RowId, out Addon addon))
        {
            return string.Empty;
        }

        return addon.Text.ToString().Trim();
    }

    public static bool IsSilverCurrency(uint itemId) =>
        itemId == SilverPieceItemId || itemId == SilverObolItemId;

    public static bool IsGoldCurrency(uint itemId) =>
        itemId == GoldPieceItemId || itemId == GoldObolItemId;

    public static bool IsTrackedCurrency(uint itemId) =>
        IsSilverCurrency(itemId) || IsGoldCurrency(itemId);
}
