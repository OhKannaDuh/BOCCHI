using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Ocelot.Extensions;

namespace BOCCHI.Services.Repair;

internal static class RepairNpc
{
    public const uint RepairEventId = 720915;

    public const float SearchRadius = 50f;

    public const float InteractRadius = 3.5f;

    public static bool TryFindNearby(
        IObjectTable objects,
        IDataManager data,
        Vector3 playerPosition,
        out IGameObject npc,
        out int repairMenuIndex)
    {
        npc = null!;
        repairMenuIndex = -1;
        float best = float.MaxValue;

        foreach (IGameObject obj in objects)
        {
            if (obj is not { ObjectKind: ObjectKind.EventNpc, IsTargetable: true })
            {
                continue;
            }

            float dist = obj.Position.Distance2D(playerPosition);
            if (dist > SearchRadius || dist >= best)
            {
                continue;
            }

            if (!data.GetExcelSheet<ENpcBase>().TryGetRow(obj.BaseId, out ENpcBase sheet))
            {
                continue;
            }

            int index = IndexOfRepair(sheet);
            if (index < 0)
            {
                continue;
            }

            best = dist;
            npc = obj;
            repairMenuIndex = index;
        }

        return npc != null;
    }

    private static int IndexOfRepair(ENpcBase sheet)
    {
        for (int i = 0; i < sheet.ENpcData.Count; i++)
        {
            if (sheet.ENpcData[i].RowId == RepairEventId)
            {
                return i;
            }
        }

        return -1;
    }
}
