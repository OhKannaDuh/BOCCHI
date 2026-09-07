using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Data.Zones.Graph.Factory;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using System.Numerics;

namespace BOCCHI.Common.Data.Zones.Implementations.SouthHorn;

public class SouthHorn
(
    IObjectTable objects,
    IDalamudPluginInterface plugin,
    IGraphFactory graphs,
    IPathfinder pathfinder,
    ILogger logger
) : BaseZone(objects, plugin, graphs, pathfinder, logger, ZoneId.SouthHorn)
{
    private static readonly AethernetData BaseCamp = new()
    {
        // PlaceName 4927 = Expedition Base Camp (Lifestream / SubArea); 4944 is a duplicate row.
        Id = 4927,
        BaseId = 2014664,
        Position = new(830.75f, 72.98f, -695.98f),
        Destination = new(833.0f, 73f, -697.7f),
        DeadRadius = 3.5f
    };

    private static readonly AethernetData TheWanderersHaven = new()
    {
        // PlaceName 4928 matches Lifestream; 4936 is a duplicate Singular row.
        Id = 4928,
        BaseId = 2014665,
        Position = new(-173.02f, 8.19f, -611.14f),
        // Keep Dest near crystal; approach stops at body + 2y past the edge for Lifestream.
        Destination = new(-170.74f, 6.5f, -610.13f),
        DeadRadius = 3.2f
    };

    private static readonly AethernetData CrystallizedCaverns = new()
    {
        Id = 4929,
        BaseId = 2014666,
        Position = new(-358.14f, 101.98f, -120.96f),
        // Old Dest was ~3.55y out — just past Lifestream range.
        Destination = new(-355.65f, 100f, -120.78f),
        DeadRadius = 3.2f
    };

    private static readonly AethernetData Eldergrowth = new()
    {
        Id = 4930,
        BaseId = 2014667,
        Position = new(306.94f, 105.18f, 305.65f),
        Destination = new(306.94f, 103f, 306f),
        DeadRadius = 3.2f
    };

    private static readonly AethernetData Stonemarsh = new()
    {
        Id = 4942,
        BaseId = 2014744,
        Position = new(-384.12f, 99.20f, 281.42f),
        Destination = new(-384f, 97.2f, 278.1f),
        DeadRadius = 3.2f
    };
    protected override uint BasecampPlaceNameId
    {
        get => 4927;
    }

    public override AethernetData GetMainAetheryte() => BaseCamp;

    public override Vector3 GetAetherytePosition() => new(830.75f, 72.98f, -695.98f);

    public override Vector3 GetStartingPosition() => new(850.33f, 72.99f, -704.07f);

    public override List<AethernetData> GetAetherytes() =>
    [
        BaseCamp,
        TheWanderersHaven,
        CrystallizedCaverns,
        Eldergrowth,
        Stonemarsh
    ];

    public override List<AethernetData> GetAethernetShards() =>
    [
        TheWanderersHaven,
        CrystallizedCaverns,
        Eldergrowth,
        Stonemarsh
    ];

    protected override ushort GetForkedTowerEventId() => 48;

    public override List<ActivityData> GetNormalFateData() =>
    [
        new(1962, new(162f, 56f, 676f)), // "Rough Waters"
        new(1963, new(373.20f, 70f, 486f)), // "The Golden Guardian"
        new(1964, new(-226.10f, 116.38f, 254f)), // "King of the Crescent"
        new(1965, new(-548.50f, 3f, -595f), PreferredAethernetId: TheWanderersHaven.Id), // "The Winged Terror"
        new(1966, new(-223.10f, 107f, 36f)), // "An Unending Duty"
        new(1967, new(-48.10f, 111.76f, -320f), PreferredAethernetId: CrystallizedCaverns.Id), // "Brain Drain"
        new(1968, new(-370f, 75f, 650f)), // "A Delicate Balance"
        new(1969, new(-589.10f, 96.50f, 333f)), // "Sworn to Soil"
        new(1970, new(-71f, 71.31f, 557f)), // "A Prying Eye"
        new(1971, new(79f, 97.86f, 278f)), // "Fatal Allure"
        new(1972, new(413f, 96f, -13f)) // "Serving Darkness"
    ];

    public override List<ActivityData> GetPotFateData() =>
    [
        new(1976, new(200f, 111.73f, -215f), PreferredAethernetId: Eldergrowth.Id), // "Persistent Pots" (North)
        new(1977, new(-481f, 75f, 528f), PreferredAethernetId: Stonemarsh.Id) // "Pleading Pots" (South)
    ];

    // Staging + AreaShape. Registration size/centre come from live LGB MapRange, not these rows.
    public override List<ActivityData> GetCriticalEncounterData() =>
    [
        new(33, new(300.109f, 70f, 730.029f), PreferredAethernetId: Eldergrowth.Id), // "Scourge of the Mind"
        new(34, new(449.613f, 65f, 356.86f), PreferredAethernetId: Eldergrowth.Id,
            AreaShape: ActivityAreaShape.Square), // "The Black Regiment"
        new(35, new(619.864f, 79f, 799.882f), PreferredAethernetId: Eldergrowth.Id), // "The Unbridled"
        new(36, new(680.95f, 74f, 533.939f), PreferredAethernetId: Eldergrowth.Id), // "Crawling Death"
        new(37, new(-340.067f, 75f, 800.32f), PreferredAethernetId: Stonemarsh.Id), // "Calamity Bound"
        new(38, new(-413.775f, 92f, 74.884f), PreferredAethernetId: CrystallizedCaverns.Id), // "Trial by Claw"
        new(39, new(-799.895f, 44f, 245.027f), PreferredAethernetId: Stonemarsh.Id), // "From Times Bygone"
        new(40, new(679.954f, 96f, -279.855f), PreferredAethernetId: BaseCamp.Id), // "Company of Stone"
        new(41, new(-117.227f, 1f, -849.941f), PreferredAethernetId: TheWanderersHaven.Id), // "Shark Attack"
        // BaseCamp: Lost Citadel approach. Eldergrowth walks around the citadel exterior.
        new(42, new(635.981f, 108f, -53.95f), PreferredAethernetId: BaseCamp.Id), // "On the Hunt"
        new(43, new(-351.222f, 5f, -607.909f), PreferredAethernetId: TheWanderersHaven.Id), // "With Extreme Prejudice"
        new(44, new(460.949f, 97f, -362.86f), PreferredAethernetId: BaseCamp.Id), // "Noise Complaint"
        new(45, new(71.964f, 20f, -544.904f), PreferredAethernetId: TheWanderersHaven.Id), // "Cursed Concern"
        // Eternal Watch — stand on the platform (Y~122). Y~1.22 is under the mesh (poly 0) and
        // made Illegal Mode cancel/replan forever when the elevated MapRange (~560y) was rejected.
        new(46, new(860.536f, 122f, 169.893f), PreferredAethernetId: Eldergrowth.Id,
            StandRadius: 8f, CombatRadius: 28f),
        new(47, new(-570.087f, 97f, -160.04f), PreferredAethernetId: CrystallizedCaverns.Id) // "Flame of Dusk"
    ];

    public override BuffZone? GetBuffZone() =>
        new(new Vector3(836.07f, 73.12f, -709.45f), 2.5f, 4.5f);

    public override TreasureRoutePolicy GetTreasureRoutePolicy() =>
        new()
        {
            UnsafeWeatherIds = [7, 62, 64, 192],
            AshkinStartEorzeaMinute = 1350,
            AshkinEndEorzeaMinute = 240,
        };

    public override List<TreasureData> GetTreasureData() =>
    [
        new(1789, 5, new(770.748f, 108.000f, -143.542f)),
        new(1790, 11, new(-283.955f, 116.000f, 377.035f)),
        new(1791, 13, new(-682.765f, 135.619f, -195.270f)),
        new(1792, 16, new(697.322f, 70.000f, 597.925f)),
        new(1793, 14, new(517.754f, 67.897f, 236.133f)),
        new(1794, 23, new(-825.143f, 3.000f, -832.252f)),
        new(1795, 25, new(-798.215f, 105.607f, -310.536f)),
        new(1796, 28, new(-645.655f, 203.000f, 710.170f)),
        new(1797, 1, new(617.090f, 66.309f, -703.883f)),
        new(1798, 1, new(490.410f, 62.479f, -590.570f)),
        new(1799, 2, new(666.545f, 79.133f, -480.360f)),
        new(1800, 2, new(870.695f, 95.703f, -388.327f)),
        new(1801, 3, new(354.116f, 95.663f, -288.899f)),
        new(1802, 3, new(386.953f, 96.817f, -451.347f)),
        new(1803, 4, new(779.019f, 96.100f, -256.243f)),
        new(1804, 4, new(475.731f, 96.000f, -87.083f)),
        new(1805, 5, new(609.624f, 108.000f, 117.292f)),
        new(1806, 5, new(726.284f, 108.150f, -67.898f)),
        new(1807, 3, new(381.765f, 22.178f, -743.648f)),
        new(1808, 6, new(-140.459f, 22.380f, -414.267f)),
        new(1809, 6, new(142.107f, 16.413f, -574.060f)),
        new(1810, 7, new(-118.975f, 5.000f, -708.431f)),
        new(1811, 8, new(-490.990f, 3.000f, -529.595f)),
        new(1812, 8, new(-451.682f, 3.000f, -775.570f)),
        new(1813, 9, new(245.624f, 109.137f, -18.174f)),
        new(1814, 9, new(55.314f, 111.315f, -289.082f)),
        new(1815, 10, new(-25.681f, 102.230f, 150.195f)),
        new(1816, 10, new(277.809f, 103.799f, 241.907f)),
        new(1817, 11, new(-487.114f, 98.531f, -205.463f)),
        new(1818, 11, new(-158.648f, 98.649f, -132.738f)),
        new(1819, 12, new(-444.114f, 90.691f, 26.230f)),
        new(1820, 12, new(-394.888f, 106.744f, 175.463f)),
        new(1821, 13, new(-713.798f, 62.066f, 192.638f)),
        new(1822, 13, new(-756.802f, 76.573f, 97.368f)),
        new(1823, 14, new(256.153f, 73.187f, 492.363f)),
        new(1824, 14, new(35.721f, 65.110f, 648.981f)),
        new(1825, 15, new(294.911f, 56.100f, 640.223f)),
        new(1826, 15, new(140.978f, 56.000f, 770.992f)),
        new(1827, 16, new(643.000f, 70.000f, 407.797f)),
        new(1828, 16, new(471.214f, 70.300f, 530.022f)),
        new(1829, 17, new(433.707f, 70.300f, 683.528f)),
        new(1830, 17, new(596.490f, 70.300f, 622.766f)),
        new(1831, 18, new(-197.192f, 74.926f, 618.341f)),
        new(1832, 18, new(-372.671f, 75.000f, 527.428f)),
        new(1833, 19, new(-401.633f, 85.065f, 332.540f)),
        new(1834, 19, new(-648.005f, 75.000f, 403.982f)),
        new(1835, 20, new(-225.025f, 75.000f, 804.990f)),
        new(1836, 20, new(788.876f, 120.400f, 109.392f)),
        new(1837, 21, new(826.718f, 122.000f, 435.019f)),
        new(1838, 21, new(869.291f, 110.000f, 581.231f)),
        new(1839, 22, new(-585.260f, 5.000f, -864.836f)),
        new(1840, 22, new(-729.427f, 5.000f, -724.788f)),
        new(1841, 22, new(-661.677f, 3.000f, -579.492f)),
        new(1842, 23, new(-884.123f, 3.800f, -682.002f)),
        new(1843, 24, new(-729.915f, 116.541f, -79.057f)),
        new(1844, 24, new(-856.935f, 68.847f, -93.144f)),
        new(1845, 25, new(-767.446f, 115.623f, -235.004f)),
        new(1846, 25, new(-680.537f, 104.861f, -354.788f)),
        new(1847, 26, new(-550.117f, 107.000f, 627.767f)),
        new(1848, 26, new(-729.519f, 107.000f, 561.181f)),
        new(1849, 27, new(-784.748f, 139.000f, 699.784f)),
        new(1850, 27, new(-600.272f, 139.000f, 802.641f)),
        new(1851, 28, new(-676.387f, 171.000f, 640.406f)),
        new(1852, 28, new(-716.121f, 171.000f, 794.430f)),
        new(1853, 21, new(835.111f, 70.000f, 699.122f)),
        new(1854, 10, new(8.987f, 103.224f, 426.993f)),
        new(1855, 11, new(-256.855f, 121.000f, 125.078f)),
        new(1856, 11) // no bake position
    ];


    public override Dictionary<int, List<PotChestData>> GetPotChestData() =>
        new()
        {
            // North
            {
                1976, [
                    new(new(571.5841f, 51.451305f, -813.1642f), 99),
                    new(new(662.4388f, 120f, 161.1339f), 99),
                    new(new(606.4641f, 108.07402f, 184.8517f), 99),
                    new(new(-312.2778f, 103.19944f, -35.25348f), 99),
                    new(new(587.7039f, 78.8956f, -545.8168f), 99),
                    new(new(891.2597f, 120f, -20.672f), 99),
                    new(new(878.1131f, 108.28959f, -91.1057f), 99),
                    new(new(803.6609f, 95.99998f, -354.1809f), 99),
                    new(new(341.4413f, 95.99999f, 194.7507f), 99),
                    new(new(570.2421f, 64.66201f, 272.1734f), 99),
                    new(new(-216.372f, 5.4469404f, -510.1361f), 99),
                    new(new(684.4223f, 96.10129f, -165.4811f), 99),
                    new(new(-188.1745f, 2.999999f, -717.2005f), 99),
                    new(new(-476.3011f, 101.44228f, -86.69939f), 99),
                    new(new(80.19762f, 101.27949f, 391.2263f), 99),
                    new(new(-534.6993f, 2.999998f, -651.6244f), 99),
                    new(new(-165.2374f, 95.33837f, 437.4505f), 99),
                    new(new(330.8659f, 6.7168036f, -654.5339f), 99),
                    new(new(-333.3444f, 2.9999998f, -861.1722f), 99),
                    new(new(-313.2906f, 108.10962f, 70.76207f), 99),
                    new(new(-459.1735f, 93.57443f, 5.054043f), 99),
                    new(new(-54.69518f, 99.40573f, 405.0261f), 99),
                    new(new(-382.4396f, 109.30187f, -378.3482f), 99),
                    new(new(263.2559f, 100.38499f, 326.6834f), 99),
                    new(new(224.7233f, 68.7328f, 518.668f), 99),
                    new(new(19.73968f, 26.045855f, -420.977f), 99),
                    new(new(705.2716f, 68.143616f, 358.6714f), 99),
                    new(new(-660.5336f, 98f, -216.7666f), 99),
                    new(new(-324.2736f, 121f, 203.2017f), 99),
                    new(new(-386.5904f, -0.13994062f, -461.0976f), 99)
                ]
            },
            // South
            {
                1977, [
                    new(new(-195.4419f, 110.15342f, -287.8911f), 99),
                    new(new(74.73397f, 110.494316f, -394.1289f), 99),
                    new(new(-386.437f, 98.60658f, -221.7847f), 99),
                    new(new(-554.6146f, 99.01769f, -309.1231f), 99),
                    new(new(107.0611f, 105.699875f, 146.7059f), 99),
                    new(new(825.9521f, 70f, 772.4054f), 99),
                    new(new(-836.7586f, 106.999985f, 597.2944f), 99),
                    new(new(67.45271f, 69.477974f, 745.8658f), 99),
                    new(new(69.70596f, 111.56108f, -239.064f), 99),
                    new(new(301.8741f, 103.784424f, 70.59854f), 99),
                    new(new(-38.97946f, 102.073296f, -175.4589f), 99),
                    new(new(-60.72729f, 69.687035f, 828.4997f), 99),
                    new(new(17.60418f, 65.93209f, 674.6207f), 99),
                    new(new(393.2685f, 57.545956f, 844.6924f), 99),
                    new(new(393.0191f, 104f, -124.1651f), 99),
                    new(new(-798.7886f, 84.22545f, -4.822005f), 99),
                    new(new(440.8355f, 70.3f, 876.4097f), 99),
                    new(new(-734.1434f, 170.99998f, 683.7238f), 99),
                    new(new(423.3505f, 70.3f, 578.9013f), 99),
                    new(new(200.1241f, 56f, 624.2285f), 99),
                    new(new(-603.3457f, 139f, 858.6771f), 99),
                    new(new(-829.598f, 62.66814f, 66.82948f), 99),
                    new(new(-645.3027f, 135.69208f, -73.54771f), 99),
                    new(new(-836.1612f, 107f, 770.2822f), 99),
                    new(new(-676.6202f, 128.57442f, 1.531581f), 99),
                    new(new(-713.6796f, 203f, 710.08f), 99),
                    new(new(781.2514f, 70f, 560.0701f), 99),
                    new(new(-746.1318f, 172.00023f, 828.8809f), 99),
                    new(new(-730.5441f, 107.694275f, -371.4776f), 99),
                    new(new(-810.8279f, 114.053925f, -226.8324f), 99)
                ]
            }
        };

    public override List<PotChestData> GetRerollPotChestData() =>
    [
        new(new(-676.4631f, 5f, -769.7955f), 99),
        new(new(-823.9183f, 140.00032f, 677.6934f), 99),
        new(new(-886.4718f, 107f, 712.4964f), 99),
        new(new(-625.7809f, 171f, 810.8691f), 99),
        new(new(-813.9943f, 5f, -663.3634f), 99),
        new(new(-842.8967f, 75.76903f, -125.0559f), 99),
        new(new(-680.0345f, 201f, 739.9117f), 99),
        new(new(-793.0552f, 5f, -777.3126f), 99),
        new(new(-708.6777f, 171f, 669.5714f), 99),
        new(new(-718.0424f, 5f, -633.8791f), 99),
        new(new(-868.8489f, 67.5054f, -59.44909f), 99),
        new(new(-803.5182f, 3f, -602.7497f), 99),
        new(new(-732.2048f, 139f, 828.8491f), 99),
        new(new(-659.1158f, 12.198493f, -508.7968f), 99),
        new(new(-785.997f, 162.39513f, 790.5948f), 99),
        new(new(-840.8771f, 107.26465f, -250.273f), 99),
        new(new(-708.687f, 141.16982f, -139.3283f), 99),
        new(new(-796.66f, 114.15647f, -228.9318f), 99),
        new(new(-776.6315f, 5f, -486.978f), 99),
        new(new(-758.8058f, 127.66496f, -183.164f), 99)
    ];

    public override List<CarrotData> GetCarrotData() =>
    [
        // Keep in sync with BOCCHI.Treasure/Data/SouthHorn/carrot_locations.json (worker-accepted).
        new(1, new(477.4074f, 96.10128f, 138.6543f), 4),
        new(2, new(-439.0463f, 115.82392f, 184.4665f), 12),
        new(3, new(466.2025f, 70.3f, 563.2519f), 17),
        new(4, new(283.6546f, 55.999996f, 587.3107f), 15),
        new(5, new(-575.6361f, 162.39511f, 668.7043f), 27),
        new(6, new(-806.5123f, 107f, 887.6146f), 26),
        new(7, new(248.9159f, 55.999996f, 791.1138f), 20),
        new(8, new(772.3591f, 70.3f, 531.1259f), 21),
        new(9, new(845.5334f, 98f, 777.4331f), 17),
        new(10, new(650.2321f, 108f, 141.1927f), 5),
        new(11, new(827.2007f, 108f, -156.4444f), 5),
        new(12, new(-273.0878f, 75f, 850.0336f), 20),
        new(13, new(-727.8528f, 81.47683f, 328.9311f), 19),
        new(14, new(-174.0473f, 121.00001f, 107.6488f), 11),
        new(15, new(-400.528f, 2.999999f, -518.3032f), 8),
        new(16, new(-710.266f, 3f, -451.5128f), 23),
        new(17, new(-743.601f, 96.39003f, 84.43998f), 13),
        new(18, new(-84.73673f, 2.9999988f, -796.0166f), 1),
        new(19, new(-554.0244f, 110.698654f, -365.897f), 11),
        new(20, new(-843.8602f, 83.657074f, -36.78173f), 24),
        new(21, new(720.4133f, 120f, 271.05f), 21),
        new(22, new(865.0009f, 95.99958f, -214.6744f), 5),
        new(23, new(-490.3187f, 3f, -741.0153f), 22),
        new(24, new(-701.8768f, 201f, 718.7181f), 28),
        new(25, new(-771.6308f, 5f, -694.0016f), 22),
    ];
}
