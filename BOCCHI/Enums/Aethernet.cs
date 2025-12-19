using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BOCCHI.Data;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using Lumina.Excel.Sheets;

namespace BOCCHI.Enums;

public enum Aethernet : uint
{
    BaseCamp = 4944,
    TheWanderersHaven = 4936,
    CrystallizedCaverns = 4929,
    Eldergrowth = 4930,
    Stonemarsh = 4942,
}

public class AethernetData
{
    public readonly static float DISTANCE = 3.8f;

    public Aethernet Aethernet;

    public uint BaseId;

    public Vector3 Position;


    public Vector3 Destination; // Where you end up after teleporting to this shard

    public static IEnumerable<AethernetData> All()
    {
        return ((Aethernet[])Enum.GetValues(typeof(Aethernet)))
            .Select(a => a.GetData());
    }

    public static IOrderedEnumerable<AethernetData> AllByDistance()
    {
        return AllByDistance(Player.Position);
    }

    public static IOrderedEnumerable<AethernetData> AllByDistance(Vector3 position)
    {
        return All().OrderBy(a => Vector3.Distance(a.Position, position));
    }

    public static AethernetData GetClosestTo(Vector3 to)
    {
        return All().OrderBy(data => Vector3.Distance(to, data.Position)).First();
    }

    public static AethernetData GetClosestToPlayer()
    {
        return GetClosestTo(Player.Position);
    }

    public float DistanceTo(Vector3 to)
    {
        return Vector3.Distance(to, Position);
    }

    public float DistanceToPlayer()
    {
        return DistanceTo(Player.Position);
    }
}

public static class AethernetExtensions
{
    public static string ToFriendlyString(this Aethernet aethernet)
    {
        return Svc.Data.GetExcelSheet<PlaceName>().FirstOrDefault(p => p.RowId == (uint)aethernet).Name.ToString();
    }

    public static AethernetData GetData(this Aethernet aethernet)
    {
        switch (aethernet)
        {
            case Aethernet.BaseCamp:
                return new AethernetData
                {
                    Aethernet = Aethernet.BaseCamp,
                    BaseId = 2014664,
                    Position = ZoneData.Aetherytes[ZoneData.SOUTHHORN],
                    Destination = new Vector3(835.3f, 73f, -695.9f),
                };
            case Aethernet.TheWanderersHaven:
                return new AethernetData
                {
                    Aethernet = Aethernet.TheWanderersHaven,
                    BaseId = 2014665,
                    Position = new Vector3(-173.02f, 8.19f, -611.14f),
                    Destination = new Vector3(-169.1f, 6.5f, -609.4f),
                };
            case Aethernet.CrystallizedCaverns:
                return new AethernetData
                {
                    Aethernet = Aethernet.CrystallizedCaverns,
                    BaseId = 2014666,
                    Position = new Vector3(-358.14f, 101.98f, -120.96f),
                    Destination = new Vector3(-354.6f, 100f, -120.7f),
                };
            case Aethernet.Eldergrowth:
                return new AethernetData
                {
                    Aethernet = Aethernet.Eldergrowth,
                    BaseId = 2014667,
                    Position = new Vector3(306.94f, 105.18f, 305.65f),
                    Destination = new Vector3(-302.3f, 103f, 306f),
                };
            case Aethernet.Stonemarsh:
                return new AethernetData
                {
                    Aethernet = Aethernet.Stonemarsh,
                    BaseId = 2014744,
                    Position = new Vector3(-384.12f, 99.20f, 281.42f),
                    Destination = new Vector3(-384f, 97.2f, 278.1f),
                };
            default:
                return Aethernet.BaseCamp.GetData();
        }
    }
}
