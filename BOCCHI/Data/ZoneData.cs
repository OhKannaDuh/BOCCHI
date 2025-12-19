using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using BOCCHI.Enums;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;

namespace BOCCHI.Data;

public static class ZoneData
{
    public const uint SOUTHHORN = 1252;

    // This can and should be filled using layout files or excel data
    public readonly static Dictionary<uint, Vector3> Aetherytes = new()
    {
        { SOUTHHORN, new Vector3(830.75f, 72.98f, -695.98f) },
    };

    public readonly static Dictionary<uint, Vector3> StartingLocations = new()
    {
        { SOUTHHORN, new Vector3(850.33f, 72.99f, -704.07f) },
    };

    // Zone functions
    private static bool IsInSouthHorn()
    {
        return Svc.ClientState.TerritoryType == SOUTHHORN;
    }

    public static bool IsInOccultCrescent()
    {
        return Svc.ClientState.LocalPlayer != null && IsInSouthHorn();
    }

    // Tower functions
    private static bool IsInForkedTowerBlood()
    {
        var player = Svc.ClientState.LocalPlayer;
        if (player == null)
        {
            return false;
        }

        return player.StatusList.HasAny(
            PlayerStatus.DutiesAsAssigned,
            PlayerStatus.ResurrectionDenied,
            PlayerStatus.ResurrectionRestricted
        ) && IsInSouthHorn();
    }

    public static bool IsInForkedTower()
    {
        return IsInForkedTowerBlood();
    }

    private static string GetCurrentZoneName()
    {
        if (IsInSouthHorn())
        {
            return "South Horn";
        }

        throw new Exception("Unknown Zone");
    }

    public static string GetCurrentZoneDataDirectory()
    {
        var directory = Path.Join(Svc.PluginInterface.AssemblyLocation.DirectoryName, "Data", GetCurrentZoneName().Replace(" ", ""));
        Directory.CreateDirectory(directory);

        return directory;
    }

    public static Aethernet GetClosestAethernetShard(Vector3 position)
    {
        return AethernetData.All().OrderBy((data) => Vector3.Distance(position, data.Position)).First()!.Aethernet;
    }

    public static IList<IGameObject> GetNearbyAethernetShards(float range = 4.3f)
    {
        var playerPos = Svc.ClientState.LocalPlayer?.Position ?? Vector3.Zero;

        return Svc.Objects
            .Where(o => o.ObjectKind == ObjectKind.EventObj)
            .Where(o => AethernetData.All().Select((datum) => datum.BaseId).Contains(o.BaseId))
            .Where(o => Vector3.Distance(o.Position, playerPos) <= range)
            .ToList();
    }

    public static bool IsNearAethernetShard(Aethernet aethernet, float range = 4.3f)
    {
        return GetNearbyAethernetShards(range).Any(o => o.BaseId == aethernet.GetData().BaseId);
    }

    public static IList<IGameObject> GetNearbyKnowledgeCrystal(float range = 4.5f)
    {
        var playerPos = Svc.ClientState.LocalPlayer?.Position ?? Vector3.Zero;

        return Svc.Objects
            .Where(o => o.ObjectKind == ObjectKind.EventObj)
            .Where(o => o.BaseId == (uint)OccultObjectType.KnowledgeCrystal)
            .Where(o => Vector3.Distance(o.Position, playerPos) <= range)
            .ToList();
    }

    public static bool IsNearKnowledgeCrystal(float range = 4.5f)
    {
        return GetNearbyKnowledgeCrystal(range).Any();
    }
}
