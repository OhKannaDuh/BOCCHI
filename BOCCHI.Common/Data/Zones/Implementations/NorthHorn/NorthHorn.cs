using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Data.Zones.Graph.Factory;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using System.Numerics;

namespace BOCCHI.Common.Data.Zones.Implementations.NorthHorn;

public class NorthHorn
(
    IObjectTable objects,
    IDalamudPluginInterface plugin,
    IGraphFactory graphs,
    IPathfinder pathfinder,
    ILogger logger
) : BaseZone(objects, plugin, graphs, pathfinder, logger, ZoneId.NorthHorn)
{
    private static readonly AethernetData BaseCamp = new()
    {
        Id = 5571,
        BaseId = 2015429,
        Position = new(880.002f, 259.74f, 880.059f),
        Destination = new(882.18f, 258.5f, 882.263f),
        DeadRadius = 3.34f
    };

    private static readonly AethernetData TheCrownOfKarnak = new()
    {
        Id = 5576,
        BaseId = 2015434,
        Position = new(451.684f, 70.927f, 528.839f),
        Destination = new(453.897f, 70f, 530.314f),
        DeadRadius = 3f
    };

    private static readonly AethernetData SinkingSanctuary = new()
    {
        Id = 5572,
        BaseId = 2015430,
        Position = new(357.669f, 45.766f, -554.308f),
        Destination = new(357.311f, 45.094f, -556.975f),
        DeadRadius = 3f
    };

    private static readonly AethernetData SuspendedMasonry = new()
    {
        Id = 5573,
        BaseId = 2015431,
        Position = new(-547.247f, 67.998f, 594.404f),
        Destination = new(-549.244f, 67.178f, 596.183f),
        DeadRadius = 3f
    };

    private static readonly AethernetData MolderingOutskirts = new()
    {
        Id = 5574,
        BaseId = 2015432,
        Position = new(-388.573f, 41.221f, -440.52f),
        Destination = new(-386.639f, 39.293f, -438.26f),
        DeadRadius = 3.55f
    };

    private static readonly AethernetData UnhallowedHamlet = new()
    {
        Id = 5575,
        BaseId = 2015433,
        Position = new(-13.364f, 3.145f, -40.512f),
        Destination = new(-15.738f, 2.065f, -44.486f),
        DeadRadius = 4.75f
    };

    protected override uint BasecampPlaceNameId
    {
        get => 5571;
    }

    public override AethernetData GetMainAetheryte() => BaseCamp;

    public override Vector3 GetAetherytePosition() => BaseCamp.Position;

    public override Vector3 GetStartingPosition() => BaseCamp.Destination;

    public override List<AethernetData> GetAetherytes() =>
    [
        BaseCamp,
        TheCrownOfKarnak,
        SinkingSanctuary,
        SuspendedMasonry,
        MolderingOutskirts,
        UnhallowedHamlet
    ];

    public override List<AethernetData> GetAethernetShards() =>
    [
        TheCrownOfKarnak,
        SinkingSanctuary,
        SuspendedMasonry,
        MolderingOutskirts,
        UnhallowedHamlet
    ];

    protected override ushort GetForkedTowerEventId() => 64;

    public override List<ActivityData> GetNormalFateData() =>
    [
        new(2081, new(-440f, 47.02659f, -790f), PreferredAethernetId: MolderingOutskirts.Id), // A Rotten Affair
        new(2078, new(-402.0002f, 29.76808f, -252.9997f)), // Allure of the Occult
        new(2075, new(510f, 16.76658f, -29.99999f), PreferredAethernetId: TheCrownOfKarnak.Id), // Eye to Eye (not Unhallowed — water gap)
        new(2082, new(-855.7433f, 70.67716f, 482.1518f)), // Gale-force Encounter
        new(2079, new(-170f, 30f, -500f)), // Inconstant Gardener
        new(2074, new(724f, 70f, 220f)), // Raging Thrall
        new(2083, new(-661.0049f, 87f, -54.00021f), PreferredAethernetId: MolderingOutskirts.Id), // Scale Model
        new(2076, new(95f, 10f, 470f)), // Shoreline Showdown
        new(2080, new(-90f, 67.47852f, 865.9999f)), // Territorial Dispute
        new(2084, new(140f, 37f, -708f)), // Thunderregnum
        new(2077, new(330f, 0f, -250f), PreferredAethernetId: SinkingSanctuary.Id) // Waved Away
    ];

    public override List<ActivityData> GetPotFateData() =>
    [
        new(2072, new(233f, 7.729229f, -470f), PreferredAethernetId: SinkingSanctuary.Id), // Daylight Pottery (North)
        new(2073, new(-505.2822f, 53.14409f, 244.041f), PreferredAethernetId: SuspendedMasonry.Id) // In a Pot of Bother (South)
    ];

    // Staging + AreaShape. Registration size/centre come from live LGB MapRange, not these rows.
    public override List<ActivityData> GetCriticalEncounterData() =>
    [
        new(56, new(237.91f, 15f, 351.69f), AreaShape: ActivityAreaShape.Square), // A Beast Unleashed
        new(63, new(500f, 56f, -310f)), // Accept No Imitators
        new(62, new(-82f, 12f, 485f)), // Ahead of the Competition
        new(59, new(807f, 61f, -562f)), // Appalling Behavior
        new(53, new(-688f, 90f, 150f), AreaShape: ActivityAreaShape.Square), // Cursed Resurgence
        new(57, new(224f, 52f, -860f), AreaShape: ActivityAreaShape.Square), // Dark Artistry
        new(50, new(-215f, 18f, -65f)), // Doubled Trouble
        new(58, new(-390f, 68f, 700f)), // Familiar Tactics
        new(52, new(659f, 132f, 659f), PreferredAethernetId: TheCrownOfKarnak.Id), // Forbidden Folios
        new(54, new(765f, 70f, 0f)), // Imbalanced Diet
        // Circle (LGB TriggerBoxShape can report box for this one).
        new(61, new(-150f, 70f, -860f), PreferredAethernetId: MolderingOutskirts.Id), // Lost on the Wind
        new(49, new(-870f, 20f, -560f)), // Many Mouths to Feed
        new(51, new(-519f, 48f, -641f)), // Quarried Away
        new(60, new(152f, 70f, 716f)), // Tiny Terror
        new(55, new(170f, 4f, -136f)) // Web of Terror
    ];

    public override BuffZone? GetBuffZone() =>
        new(new Vector3(885.009f, 258.500f, 874.735f), 2.5f, 4.5f);

    public override List<Vector3> GetAuthoredKnowledgeCrystalCenters() =>
    [
        // Forked Tower: Magic — first knowledge crystal (manual buff inside the tower).
        new(-893f, 780f, -981.803f),
    ];

    public override TreasureRoutePolicy GetTreasureRoutePolicy() =>
        new();

    public override List<TreasureData> GetTreasureData() =>
    [
        new(2006, 25, new(383.314f, 33.000f, -175.648f)), // SinkingSanctuary_8
        new(2007, 27, new(-2.306f, 66.691f, -814.905f)), // SinkingSanctuary_3
        new(2008, 30, new(-22.669f, 42.087f, 628.995f)), // CrownofKarnak_1
        new(2009, 35, new(-633.696f, 82.718f, -146.005f)), // MolderingOutskirts_16
        new(2010, 43, new(634.792f, 60.515f, -831.787f)), // SinkingSanctuary_12
        new(2011, 41, new(-645.440f, 160.099f, 967.944f)), // SuspendedMasonry_5
        new(2012, 45, new(-815.808f, -21.835f, -699.370f)), // MolderingOutskirts_9
        new(2013, 48, new(223.653f, -161.864f, -30.644f)), // UnhallowedHamlet_9
        new(2014, 20, new(676.997f, 190.978f, 957.447f)), // BaseCamp_1
        new(2015, 20, new(812.000f, 192.000f, 669.000f)), // BaseCamp_2
        new(2016, 21, new(673.740f, 161.165f, 729.666f)), // BaseCamp_3
        new(2017, 21, new(758.147f, 130.000f, 506.813f)), // BaseCamp_4
        new(2018, 23, new(246.227f, 66.542f, 676.666f)), // CrownofKarnak_2
        new(2019, 23, new(719.348f, 69.655f, 268.304f)), // BaseCamp_5
        new(2020, 24, new(449.408f, 0.147f, 105.235f)), // BaseCamp_6
        new(2021, 24, new(649.544f, 46.245f, -157.774f)), // SinkingSanctuary_7
        new(2022, 25, new(478.451f, 12.422f, -202.971f)), // SinkingSanctuary_6
        new(2023, 25, new(254.744f, 36.932f, -605.000f)), // SinkingSanctuary_4
        new(2024, 25, new(-26.000f, 0.232f, -437.688f)), // MolderingOutskirts_3
        new(2025, 36, new(-265.761f, 30.171f, -439.519f)), // MolderingOutskirts_1
        new(2026, 27, new(-232.419f, 53.237f, -719.972f)), // MolderingOutskirts_4
        new(2027, 27, new(147.869f, 61.000f, -868.752f)), // SinkingSanctuary_2
        new(2028, 28, new(658.809f, 66.126f, -364.676f)), // SinkingSanctuary_9
        new(2029, 28, new(950.201f, 74.000f, -358.976f)), // SinkingSanctuary_16
        new(2030, 43, new(658.723f, 60.520f, -552.306f)), // SinkingSanctuary_10
        new(2031, 29, new(389.536f, 60.682f, -733.018f)), // SinkingSanctuary_1
        new(2032, 30, new(77.070f, 21.200f, 536.270f)), // CrownofKarnak_3
        new(2033, 30, new(-12.099f, 66.651f, 773.863f)), // CrownofKarnak_4
        new(2034, 31, new(-278.056f, 47.784f, 567.973f)), // SuspendedMasonry_2
        new(2035, 33, new(-436.442f, 0.203f, 166.219f)), // SuspendedMasonry_13
        new(2036, 32, new(-256.947f, 100.667f, 812.197f)), // SuspendedMasonry_3
        new(2037, 32, new(-504.091f, 85.753f, 758.321f)), // SuspendedMasonry_4
        new(2038, 33, new(-612.214f, 66.990f, 578.548f)), // SuspendedMasonry_1
        new(2039, 34, new(-775.894f, 70.719f, 377.153f)), // SuspendedMasonry_10
        new(2040, 34, new(-631.779f, 78.255f, 240.000f)), // SuspendedMasonry_12
        new(2041, 34, new(-923.142f, 113.265f, 197.948f)), // SuspendedMasonry_11
        new(2042, 35, new(-590.208f, 87.979f, -7.000f)), // MolderingOutskirts_17
        new(2043, 35, new(-878.967f, 13.135f, -314.202f)), // MolderingOutskirts_13
        new(2044, 36, new(-581.489f, 40.914f, -257.411f)), // MolderingOutskirts_15
        new(2045, 36, new(-254.141f, 1.821f, -266.312f)), // MolderingOutskirts_2
        new(2046, 37, new(-707.376f, 41.586f, -396.989f)), // MolderingOutskirts_14
        new(2047, 45, new(-697.271f, 34.898f, -565.022f)), // MolderingOutskirts_12
        new(2048, 38, new(-439.551f, 43.044f, -558.449f)), // MolderingOutskirts_5
        new(2049, 38, new(-525.781f, 46.857f, -783.468f)), // MolderingOutskirts_6
        new(2050, 39, new(85.598f, 3.303f, -281.140f)), // UnhallowedHamlet_2
        new(2051, 39, new(43.782f, 2.454f, -108.192f)), // UnhallowedHamlet_1
        new(2052, 39, new(-168.204f, 3.380f, -153.458f)), // UnhallowedHamlet_3
        new(2053, 40, new(-162.042f, 3.590f, 98.450f)), // UnhallowedHamlet_4
        new(2054, 43, new(633.132f, 60.642f, -910.227f)), // SinkingSanctuary_13
        new(2055, 43, new(639.049f, 60.625f, -698.726f)), // SinkingSanctuary_11
        new(2056, 44, new(815.444f, 60.554f, -657.314f)), // SinkingSanctuary_15
        new(2057, 44, new(865.457f, 70.215f, -874.087f)), // SinkingSanctuary_14
        new(2058, 41, new(-592.000f, 160.101f, 767.669f)), // SuspendedMasonry_7
        new(2059, 41, new(-699.837f, 160.000f, 926.379f)), // SuspendedMasonry_6
        new(2060, 42, new(-857.793f, 159.850f, 772.237f)), // SuspendedMasonry_8
        new(2061, 42, new(-800.397f, 157.800f, 633.387f)), // SuspendedMasonry_9
        new(2062, 45, new(-857.599f, -12.235f, -609.817f)), // MolderingOutskirts_11
        new(2063, 45, new(-928.626f, -11.228f, -744.956f)), // MolderingOutskirts_10
        new(2064, 46, new(-736.024f, 21.035f, -881.486f)), // MolderingOutskirts_8
        new(2065, 46, new(-416.774f, 45.937f, -945.431f)), // MolderingOutskirts_7
        new(2066, 47, new(-144.726f, -129.796f, 304.938f)), // UnhallowedHamlet_6
        new(2067, 47, new(41.233f, -140.771f, 168.502f)), // UnhallowedHamlet_7
        new(2068, 48, new(161.000f, -151.760f, 16.000f)), // UnhallowedHamlet_8
        new(2069, 48, new(313.919f, -139.530f, 180.071f)), // UnhallowedHamlet_10
        new(2070, 23, new(447.886f, 62.906f, 463.345f)), // CrownofKarnak_5
        new(2071, 26, new(279.093f, 143.000f, -356.148f)), // SinkingSanctuary_5
        new(2072, 40, new(-287.741f, -92.000f, 125.666f)), // UnhallowedHamlet_5
        new(2073, 30, new(222.912f, 90.400f, 913.629f)) // CrownofKarnak_6
    ];

    public override Dictionary<int, List<PotChestData>> GetPotChestData() =>
        new()
        {
            // Daylight Pottery (North) — Fate 2072
            {
                2072, [
                    new(new(927.0178f, 54f, -155.2175f), 99),
                    new(new(929.4178f, 54f, -1.817501f), 99),
                    new(new(939.2178f, 80.269966f, -273.1175f), 99),
                    new(new(912.2978f, 61.18964f, -461.5099f), 99),
                    new(new(385f, 33f, -177f), 99),
                    new(new(-536.1014f, 87.01824f, 149.8447f), 99),
                    new(new(830.0979f, 77.75924f, -148.9099f), 99),
                    new(new(-530f, 67.77658f, -58f), 99),
                    new(new(-251.781f, 65.949005f, -864.3828f), 99),
                    new(new(889.2178f, 53.999996f, 155.9825f), 99),
                    new(new(-596f, 41.869873f, -285f), 99),
                    new(new(-223.8233f, 10.891144f, -353.9438f), 99),
                    new(new(-190f, 61.75258f, -763f), 99),
                    new(new(-498.7f, 11.051006f, 128.9f), 99),
                    new(new(-86f, 60.596237f, -737f), 99),
                    new(new(32.4f, 56.835186f, -777.3f), 99),
                    new(new(948.5978f, 63.594563f, -567.0099f), 99),
                    new(new(-252.1626f, 66.55432f, -879.5855f), 99),
                    new(new(546.56f, 36.120197f, 143.3104f), 99),
                    new(new(321.198f, 59.85f, -889.8872f), 99), // Map ~27.9, 3.7 (Sinking Sanctuary)
                    new(new(928.8978f, 74.0003f, -332.8099f), 99),
                    new(new(593f, 39.622505f, 34f), 99),
                    new(new(782.4979f, 70.34123f, -56.4099f), 99),
                    new(new(810.8979f, 78.39757f, -278.8099f), 99),
                    new(new(1.768392f, 71.555756f, -872.2798f), 99),
                    new(new(440.298f, 60.615795f, -926.5872f), 99),
                    new(new(452.6f, 57.10005f, -310.3f), 99),
                    new(new(151.9998f, 61.106945f, -842.0175f), 99),
                    new(new(714.698f, 69.24771f, 262.6901f), 99),
                    new(new(-455.989f, 39.688915f, -365.5418f), 99),
                ]
            },
            // In a Pot of Bother (South) — Fate 2073
            {
                2073, [
                    new(new(-113.4943f, 5.0879984f, -74.15943f), 99),
                    new(new(-960f, 48f, -425.8f), 99),
                    // Map ~4.8, 10.1 (was ~4.8, 9.7 / <-834, -587.4>; Eureka Linker ~4.7, 9.7)
                    new(new(-834f, 18.913685f, -569f), 99),
                    new(new(-853.493f, 58f, -323.8983f), 99),
                    new(new(-586.3f, 47.81013f, -715.2f), 99),
                    new(new(71.10001f, 81.074875f, 942.3f), 99),
                    new(new(93.4f, 3.7155468f, -114.3f), 99),
                    new(new(210f, 98.400055f, 916f), 99),
                    new(new(28.10088f, 3.9999995f, -16.69861f), 99),
                    new(new(0.9425046f, 41.80327f, 623.2599f), 99),
                    new(new(-628.4385f, 49.07533f, -449.5009f), 99),
                    new(new(11.98766f, 68.15505f, 795.707f), 99),
                    new(new(-339.8588f, 85.47024f, 861.5197f), 99),
                    new(new(-88.43135f, 2.400001f, 4.891054f), 99),
                    new(new(-127f, 71.47446f, 808.4f), 99),
                    new(new(-184.5137f, 71.1816f, 667.8036f), 99),
                    new(new(52f, 25.316154f, 552f), 99),
                    new(new(-109.5452f, 8.047999f, -210.1855f), 99),
                    new(new(194.2296f, -0.3000001f, 352.9844f), 99),
                    new(new(-330f, 42f, -628f), 99),
                    new(new(190.3622f, 3.880325f, -204.7095f), 99),
                    new(new(237.9156f, -0.29999995f, 309.4334f), 99),
                    new(new(-512f, 41.999996f, -389f), 99),
                    new(new(-975.4507f, 17.57744f, -526.2878f), 99),
                    new(new(47.6f, 3.8843424f, -218.3f), 99),
                    new(new(-269.6122f, 107.93719f, 875.6997f), 99),
                    new(new(-15.89468f, 4.0000005f, -20.29277f), 99),
                    // Map ~6.1, 11.4 (was ~6.5, 11.6 / <-747.4, -492.1>; Eureka Linker)
                    new(new(-769.25f, 28.97f, -504.51f), 99),
                    // Map ~15.3, 21.5 — next to carrot #1; EurekaTracker counter 74. Missing from
                    // earlier lists so the farm swept nearby pads and never walked here.
                    new(new(-259.6f, 3.6823246f, 56.9f), 99),
                    // Map ~16.8, 22.3 — old Y≈3–6 points were off-mesh and stuck the farm (#176).
                    new(new(-184f, 53.15f, 91f), 99),
                    new(new(-172.6f, 53.15f, 103.2f), 99),
                    // Map ~5.9, 35.2 — high island (Linker ~4.6, 36.3 is a different pad, already in reroll).
                    new(new(-779f, 160f, 686f), 99),
                ]
            }
        };

    public override List<PotChestData> GetRerollPotChestData() =>
    [
        new(new(782.8808f, 60.390976f, -611.7695f), 99),
        new(new(925.6533f, 70.21527f, -906.2195f), 99),
        new(new(909f, 97.05797f, -961.8f), 99),
        new(new(-661f, 160f, 937f), 99),
        new(new(-527f, 160.1012f, 834f), 99),
        new(new(-631.9453f, 160f, 808.8979f), 99),
        new(new(-809f, 6.3495464f, -879f), 99),
        new(new(671.2f, 60.99496f, -550.1f), 99),
        new(new(701f, 59.999992f, -945f), 99),
        new(new(-623f, 160f, 883f), 99),
        new(new(-585f, 160f, 842f), 99),
        new(new(-656.9f, 23.036425f, -799.3f), 99),
        new(new(-839.9977f, 160f, 740f), 99),
        // Map ~5.9, 35.2 — high-island 2nd-chance pad (Linker listed the neighbor at ~4.6, 36.3).
        new(new(-779f, 160f, 686f), 99),
        new(new(-487.8f, 48.000015f, -953.2f), 99),
        new(new(-603f, 32f, -869f), 99),
        new(new(-637.2283f, 32f, -950.4841f), 99),
        new(new(-866f, -41.01304f, -775f), 99),
        new(new(626.3f, 61.119125f, -844.9f), 99),
        new(new(943.4631f, 70.21487f, -879.5159f), 99),
        new(new(-449.6f, 45.6567f, -967.0001f), 99),
    ];

    public override List<CarrotData> GetCarrotData() =>
    [
        // Keep in sync with BOCCHI.Treasure/Data/NorthHorn/carrot_locations.json (worker-accepted).
        new(1, new(-258.7481f, 3.588304f, 53.59217f), 40),
        new(2, new(-254f, 54.388798f, -739f), 27),
        new(3, new(-581f, 160f, 791f), 41),
        new(4, new(287.2872f, 142.99992f, -366.9024f), 26),
        new(5, new(756.858f, 68.92707f, -79.33746f), 24),
        new(6, new(-847.9f, 114f, 196.6f), 34),
        new(7, new(-560.9f, 50.74249f, -447f), 37),
        new(8, new(625.8f, 61.06923f, -846.3f), 43),
        new(9, new(7.60699f, 4.3169565f, -35.67316f), 39),
        new(10, new(-608.8f, 59.286507f, 373.9f), 34),
        new(11, new(-857.4f, 71.45287f, 379.6f), 34),
        new(12, new(226f, 90.400055f, 904f), 30),
        new(13, new(-814.6948f, 5.6813054f, -561.0853f), 45),
        new(14, new(-500f, 48.000004f, -867.6f), 38),
        new(15, new(108f, 22.332209f, -556f), 25),
        new(16, new(-808f, 6.3495464f, -879f), 46),
        new(17, new(960f, 97.05797f, -879f), 44),
        new(18, new(-124f, 76.75548f, 777f), 30),
        new(19, new(-35f, 72.89336f, -860f), 27),
        new(20, new(-604f, 160.05638f, 939.1f), 41),
        new(21, new(882.1526f, 53.999996f, 115.9092f), 20),
        new(22, new(923f, 80.26997f, -277f), 28),
        new(23, new(-129.7795f, 8.029996f, -171.18f), 39),
        new(24, new(853.9f, 70.20017f, -343.3f), 28),
        new(25, new(-956.1f, 157.8f, 720.2f), 42),
    ];

}
