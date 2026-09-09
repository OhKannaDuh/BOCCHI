using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Treasure.Services;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using System.Numerics;
using System.Runtime.CompilerServices;
using TreasureSheet = Lumina.Excel.Sheets.Treasure;

namespace BOCCHI.Debug;

/// <summary>
///     Shared bronze/silver pad scan from the active layout (Export + Bake debug panels).
/// </summary>
public static class LayoutTreasureScan
{
    public readonly record struct Spot(uint DataId, Vector3 Position, uint SgbId);

    /// <summary>
    ///     Collects bronze/silver layout coffers for the current zone.
    ///     Empty when there is no active layout or no treasure instances.
    /// </summary>
    public static unsafe List<Spot> CollectBronzeSilver(IZone zone, IDataManager data)
    {
        List<Spot> spots = [];
        LayoutManager* layout = LayoutWorld.Instance()->ActiveLayout;
        if (layout == null)
        {
            return spots;
        }

        if (!layout->InstancesByType.TryGetValue(
                InstanceType.Treasure,
                out Pointer<StdMap<ulong, Pointer<ILayoutInstance>>> mapPtr,
                false))
        {
            return spots;
        }

        List<TreasureData> authored = zone.GetTreasureData();
        bool hasPositionData = authored.Exists(d => d.Position.HasValue);
        var sheet = data.GetExcelSheet<TreasureSheet>();

        foreach (ILayoutInstance* instance in mapPtr.Value->Values)
        {
            Transform* transform = instance->GetTransformImpl();
            Vector3 position = transform->Translation;
            if (position.Y <= -10f && !hasPositionData)
            {
                continue;
            }

            uint treasureRowId = Unsafe.Read<uint>((byte*)instance + 0x30);
            if (!sheet.TryGetRow(treasureRowId, out TreasureSheet row))
            {
                continue;
            }

            uint sgbId = row.SGB.RowId;
            if (!TreasureCoffer.IsBronzeOrSilverSgb(sgbId))
            {
                continue;
            }

            if (hasPositionData && !authored.Any(d => d.Matches(treasureRowId, position)))
            {
                continue;
            }

            spots.Add(new Spot(treasureRowId, position, sgbId));
        }

        spots.Sort((a, b) => a.DataId.CompareTo(b.DataId));
        return spots;
    }
}
