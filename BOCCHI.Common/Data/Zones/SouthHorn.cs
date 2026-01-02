using System.Numerics;
using BOCCHI.Common.Data.Aethernet;
using Dalamud.Plugin.Services;

namespace BOCCHI.Common.Data.Zones;

public class SouthHorn(IObjectTable objects) : BaseZone(objects)
{
    protected override uint BasecampPlaceNmeId { get; } = 4944;

    public override Vector3 GetAetherytePosition()
    {
        return new Vector3(830.75f, 72.98f, -695.98f);
    }

    public override Vector3 GetStartingPosition()
    {
        return new Vector3(850.33f, 72.99f, -704.07f);
    }

    public override List<AethernetData> GetAethernetShards()
    {
        return
        [
            BaseCamp,
            TheWanderersHaven,
            CrystallizedCaverns,
            Eldergrowth,
            Stonemarsh,
        ];
    }

    private readonly static AethernetData BaseCamp = new()
    {
        Id = 4944,
        BaseId = 2014664,
        Position = new Vector3(830.75f, 72.98f, -695.98f),
        Destination = new Vector3(835.3f, 73f, -695.9f),
        DeadRadius = 3f,
    };

    private readonly static AethernetData TheWanderersHaven = new()
    {
        Id = 4936,
        BaseId = 2014665,
        Position = new Vector3(-173.02f, 8.19f, -611.14f),
        Destination = new Vector3(-169.1f, 6.5f, -609.4f),
        DeadRadius = 3.2f,
    };

    private readonly static AethernetData CrystallizedCaverns = new()
    {
        Id = 4929,
        BaseId = 2014666,
        Position = new Vector3(-358.14f, 101.98f, -120.96f),
        Destination = new Vector3(-354.6f, 100f, -120.7f),
        DeadRadius = 3.2f,
    };

    private readonly static AethernetData Eldergrowth = new()
    {
        Id = 4930,
        BaseId = 2014667,
        Position = new Vector3(306.94f, 105.18f, 305.65f),
        Destination = new Vector3(-302.3f, 103f, 306f),
        DeadRadius = 3.2f,
    };

    private readonly static AethernetData Stonemarsh = new()
    {
        Id = 4942,
        BaseId = 2014744,
        Position = new Vector3(-384.12f, 99.20f, 281.42f),
        Destination = new Vector3(-384f, 97.2f, 278.1f),
        DeadRadius = 3.2f,
    };

    private IEnumerable<uint> KnowledgeCrystalLayoutIds
    {
        get =>
        [
            11617915, // Base Camp
            11617920, // Stonemarsh
            11617921, // The Wanderer's Haven
            11617922, // Eldergrowth
            11617923, // Crystalized Caverns
        ];
    }
}
