namespace BOCCHI.Common.Data.Mobs;

/// <summary>
///     Authored mob metadata. Runtime currently uses aggro for Mob Farmer debug rings only;
///     weakness / CC / level / spawn fields are kept for reference and future filters.
/// </summary>
public static class MobProfiles
{
    /// <summary>
    ///     OC trash starts detecting around 8.8y (kuru). 9y is generally safe; sight can be closer from behind.
    /// </summary>
    public const float DetectionRange = 9f;

    private static readonly Dictionary<Mob, MobProfile> Profiles = BuildProfiles();

    public static MobAggro GetAggro(Mob mob) =>
        Profiles.TryGetValue(mob, out MobProfile profile) ? profile.Aggro : MobAggro.Unknown;

    public static MobAggro GetAggro(uint nameId) =>
        MobData.TryFromNameId(nameId, out Mob mob) ? GetAggro(mob) : MobAggro.Unknown;

    private static Dictionary<Mob, MobProfile> BuildProfiles()
    {
        Dictionary<Mob, MobProfile> profiles = new();

        AddSouthHorn(profiles);
        AddNorthHorn(profiles);

        return profiles;
    }

    private static void AddSouthHorn(Dictionary<Mob, MobProfile> p)
    {
        // Columns: doom, ashkin, paralysis, stop, slow, blind, stun, frog, sleep, heavy, bind + elements.
        // Empty stun/CC cells treated as false. Crescent sheet rows map to Crescent NameIds.
        p[Mob.Bandersnatch] = P(1, MobElement.Fire, S(doom: true, paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.FlyingLizard] = P(1, MobElement.Ice, S(doom: true, paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Garula2] = P(1, MobElement.Fire, S(doom: true, paralysis: true, stop: true, slow: true, blind: true));
        p[Mob.Chaochu] = P(2, MobElement.Fire, S(doom: true, stop: true, slow: true, stun: true));
        p[Mob.Gaelicat] = P(2, MobElement.Wind, S(doom: true, paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Snapweed] = P(3, MobElement.Fire, S(doom: true, stop: true, stun: true));
        p[Mob.Leshy] = P(3, MobElement.Fire, S(doom: true, stop: true, stun: true));
        p[Mob.Tormentor] = P(4, MobElement.Ice, S(doom: true, paralysis: true, stop: true, slow: true, blind: true));
        p[Mob.Marolith] = P(4, MobElement.Thunder, S(doom: true, stop: true, stun: true));
        p[Mob.Fan] = P(5, MobElement.Thunder, S(doom: true, paralysis: true, slow: true, blind: true, stun: true));
        p[Mob.Goobbue2] = P(6, MobElement.Fire, S(doom: true, paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Karlabos] = P(6, MobElement.Thunder, S(doom: true, paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Cetus] = P(7, MobElement.Ice, S(doom: true, paralysis: true, stop: true, blind: true, stun: true));
        p[Mob.Petalodite] = P(7, MobElement.Thunder, S(doom: true, paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Uragnite] = P(8, MobElement.Thunder, S(doom: true, paralysis: true, slow: true, blind: true, stun: true));
        p[Mob.Golem] = P(8, MobElement.Thunder, S(doom: true, stop: true));
        p[Mob.Catoblepas] = P(9, MobElement.Fire, S(doom: true, paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Meraevis] = P(9, MobElement.Wind, S(doom: true, paralysis: true, stop: true, blind: true));
        p[Mob.Triceratops] = P(10, MobElement.Ice, S(doom: true, paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Brachiosaur] = P(10, MobElement.Ice, S(doom: true, slow: true, blind: true));
        p[Mob.Rosebear] = P(11, MobElement.Fire, S(paralysis: true, slow: true));
        p[Mob.Harpuia] = P(11, MobElement.Wind, S(paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Foper] = P(11, MobElement.Fire, S(doom: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Aetherscab] = P(12, MobElement.Fire, S(paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Taurus2] = P(12, MobElement.Ice, S(doom: true, paralysis: true, slow: true, blind: true, stun: true));
        p[Mob.Byblos] = P(13, MobElement.Fire, S(doom: true, paralysis: true, stop: true, slow: true, blind: true));
        p[Mob.Panther] = P(14, MobElement.Fire, S(doom: true, stop: true, blind: true, stun: true));
        p[Mob.Echos] = P(14, MobElement.Fire, S(doom: true, paralysis: true, slow: true, blind: true, stun: true));
        p[Mob.Monk] = P(15, MobElement.Thunder, S(doom: true, paralysis: true, stop: true, blind: true));
        p[Mob.BloodDemon] = P(15, MobElement.Ice, S(paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Headstone2] = P(16, MobElement.Thunder, S(doom: true, stop: true));
        p[Mob.Blackguard] = P(16, MobElement.Wind, S(slow: true, blind: true, stun: true));
        p[Mob.DemonPawn] = P(17, MobElement.Wind, S(paralysis: true, slow: true, blind: true));
        p[Mob.Bachelor] = P(17, MobElement.Ice, S(doom: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Diplocaulus] = P(18, MobElement.Ice, S(doom: true, paralysis: true, stop: true, blind: true, stun: true));
        p[Mob.VoidViper2] = P(18, MobElement.Ice, S(doom: true, slow: true, blind: true, stun: true));
        p[Mob.Collagen] = P(19, MobElement.Ice, S(doom: true, stop: true));
        p[Mob.Claw] = P(19, MobElement.Thunder, S(doom: true, paralysis: true, slow: true, blind: true));
        p[Mob.Inkstain] = P(20, MobElement.Fire, S(doom: true, paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.LionStatant] = P(20, MobElement.Thunder, S(doom: true, paralysis: true, stop: true, slow: true, blind: true));
        p[Mob.Sculpture] = P(21, MobElement.Thunder, S(slow: true, blind: true, stun: true));
        p[Mob.DirtyEye] = P(21, MobElement.Wind, S(paralysis: true, stop: true, slow: true, blind: true, stun: true));
        p[Mob.Zaghnal] = P(22, MobElement.Fire, S(doom: true, slow: true, blind: true, stun: true));
        p[Mob.Flame] = P(22, MobElement.Ice, S(doom: true, stop: true, slow: true, stun: true));
        p[Mob.Apa] = P(23, MobElement.Thunder, S(paralysis: true, stop: true, slow: true, blind: true));
        p[Mob.Zaratan] = P(23, MobElement.Thunder, S(doom: true, paralysis: true, stop: true, blind: true, stun: true));
        p[Mob.Haagenti] = P(24, MobElement.Ice, S(paralysis: true, slow: true));
        p[Mob.Foobar] = P(25, MobElement.Thunder, S(doom: true, paralysis: true, stop: true, blind: true));
        p[Mob.Zangbeto] = P(25, MobElement.Ice, S(doom: true, paralysis: true, slow: true, blind: true));
        p[Mob.Chimera] = P(26, MobElement.Wind, S(doom: true, paralysis: true, stop: true, slow: true, blind: true));
        p[Mob.Satana] = P(27, MobElement.Wind, S(doom: true, stop: true, blind: true, stun: true));
        p[Mob.OccultGolem] = P(27, MobElement.Thunder, S(doom: true, stop: true, slow: true));
        p[Mob.Isleblazer] = P(28, MobElement.Ice, S(doom: true, stop: true));
        p[Mob.Zirnitra] = P(28, MobElement.Ice, S(doom: true, stop: true, blind: true, stun: true));

        // Ashkin / weather / special — variable levels → Level 0
        p[Mob.Ghost] = P(0, MobElement.Wind, S(ashkin: true, blind: true, stun: true), MobSpawnCondition.Night);
        p[Mob.Armor] = P(0, MobElement.Thunder, S(ashkin: true, stop: true), MobSpawnCondition.Night);
        p[Mob.Geshunpest] = P(0, MobElement.None, S(ashkin: true, slow: true, blind: true), MobSpawnCondition.Night);
        p[Mob.Caoineag] = P(0, MobElement.Wind, S(ashkin: true, stop: true, blind: true), MobSpawnCondition.Night);
        p[Mob.Troubadour] = P(0, MobElement.Fire, S(ashkin: true, paralysis: true, blind: true, stun: true), MobSpawnCondition.Night);
        p[Mob.Gourmand] = P(0, MobElement.Fire, S(ashkin: true, paralysis: true, slow: true), MobSpawnCondition.Night);
        p[Mob.Dullahan] = P(0, MobElement.Thunder, S(ashkin: true, stop: true), MobSpawnCondition.Night);
        p[Mob.Mousse] = P(0, MobElement.None, S(doom: true, paralysis: true, blind: true), MobSpawnCondition.Rain);
        p[Mob.Dhruva] = P(0, MobElement.Wind, S(doom: true, stop: true, slow: true), MobSpawnCondition.Clouds, MobAggro.Magic);
        p[Mob.Mimic] = P(0, MobElement.Thunder, S(stop: true, blind: true), MobSpawnCondition.AtmosphericPhantasms);
        p[Mob.Bomb] = P(0, MobElement.Ice, S(doom: true, stop: true, slow: true, stun: true), MobSpawnCondition.ClearSkies);
        p[Mob.Fool] = P(0, MobElement.None, S(paralysis: true, slow: true, blind: true), aggro: MobAggro.Proximity);
    }

    private static void AddNorthHorn(Dictionary<Mob, MobProfile> p)
    {
        // Susceptibilities not authored for NH yet — elements / levels / spawn only.
        p[Mob.Bicephalus] = P(0, MobElement.Fire, spawn: MobSpawnCondition.Night);
        p[Mob.Glutton] = P(0, MobElement.Wind, spawn: MobSpawnCondition.Night, aggro: MobAggro.Proximity);
        p[Mob.Ankou] = P(0, MobElement.Ice, spawn: MobSpawnCondition.Night);

        p[Mob.Weapon] = P(20, MobElement.Fire);
        p[Mob.Cliffkite] = P(20, MobElement.Thunder);
        p[Mob.Dhara] = P(21, MobElement.Thunder);
        p[Mob.Urolith] = P(21, MobElement.Thunder);
        p[Mob.Cingulata] = P(22, MobElement.Fire);
        p[Mob.Bibliotaph] = P(22, MobElement.Fire);
        p[Mob.Worm] = P(23, MobElement.Ice);
        p[Mob.Zu] = P(23, MobElement.Wind);
        p[Mob.Tomato] = P(24, MobElement.Fire);
        p[Mob.Onion] = P(24, MobElement.Fire);
        p[Mob.Vinegaroon] = P(24, MobElement.Ice);
        p[Mob.Soblyn] = P(25, MobElement.Thunder | MobElement.Ice);
        p[Mob.Opken] = P(25, MobElement.Thunder);
        p[Mob.Melia] = P(26, MobElement.Fire);
        p[Mob.Sapria] = P(26, MobElement.Fire);
        p[Mob.Belladonna] = P(27, MobElement.Ice | MobElement.Fire);
        p[Mob.Wraith] = P(27, MobElement.Wind);
        p[Mob.SaltSwallow] = P(28, MobElement.Thunder);
        p[Mob.Lorelei] = P(28, MobElement.Wind);
        p[Mob.Stoneshell] = P(29, MobElement.Thunder);
        p[Mob.Huwasi] = P(29, MobElement.Thunder);
        p[Mob.Medusa] = P(30, MobElement.Wind | MobElement.Fire);
        p[Mob.Elftoad] = P(30, MobElement.Ice);
        p[Mob.Kargas] = P(31, MobElement.Fire);
        p[Mob.SandSerpent] = P(31, MobElement.Ice);
        p[Mob.Coeurl] = P(32, MobElement.Fire);
        p[Mob.Anila] = P(32, MobElement.Thunder);
        p[Mob.Kaluk] = P(33, MobElement.Fire);
        p[Mob.Harpeia] = P(33, MobElement.Wind);
        p[Mob.Woolback] = P(34, MobElement.Fire);
        p[Mob.BigHorn] = P(34, MobElement.Fire);
        p[Mob.Ratel] = P(35, MobElement.Fire);
        p[Mob.Adamantoise] = P(35, MobElement.Ice);
        p[Mob.Gremlin] = P(36, MobElement.Fire);
        p[Mob.Craklaw] = P(36, MobElement.Thunder);
        p[Mob.Rock] = P(36, MobElement.Thunder);
        p[Mob.Regolith] = P(36, MobElement.Thunder);
        p[Mob.RotEyes] = P(37, MobElement.Fire);
        p[Mob.MossFungus] = P(38, MobElement.Fire);
        p[Mob.Banemite] = P(38, MobElement.Ice);
        p[Mob.Hellhound] = P(39, MobElement.Ice);
        p[Mob.Blackguard2] = P(39, MobElement.Wind);
        p[Mob.Parthenope] = P(40, MobElement.Ice);
        p[Mob.Gargoyle] = P(40, MobElement.Wind);
        // Aggro types from kuru (sight / magic / proximity; detect ~8.8y, 9y generally safe).
        p[Mob.BirdOfTheCrescent] = P(41, MobElement.Wind, aggro: MobAggro.Sight);
        p[Mob.Gazellehawk] = P(42, MobElement.Ice | MobElement.Wind, aggro: MobAggro.Sight);
        p[Mob.Zirnitra2] = P(42, MobElement.Ice, aggro: MobAggro.Sight);
        p[Mob.Necrodium] = P(42, MobElement.Wind);
        p[Mob.Flame2] = P(42, MobElement.Wind, aggro: MobAggro.Magic);
        p[Mob.Haunt] = P(43, MobElement.Thunder, aggro: MobAggro.Proximity);
        p[Mob.Bile] = P(43, MobElement.Thunder, aggro: MobAggro.Sight);
        p[Mob.Nanka] = P(44, MobElement.Ice, aggro: MobAggro.Sight);
        p[Mob.Accursed] = P(44, MobElement.Wind, aggro: MobAggro.Sight);
        p[Mob.Arioch] = P(45, MobElement.Wind, aggro: MobAggro.Proximity);
        p[Mob.OiseauRare] = P(45, MobElement.Wind, aggro: MobAggro.Sight);
        p[Mob.Skatene] = P(45, MobElement.None, aggro: MobAggro.Sight);
        p[Mob.Carrier] = P(46, MobElement.Fire, aggro: MobAggro.Proximity);
        p[Mob.Wamoura] = P(46, MobElement.None, aggro: MobAggro.Sight);
        p[Mob.Gusion] = P(47, MobElement.Fire, aggro: MobAggro.Sight);
        p[Mob.Succubus] = P(47, MobElement.Wind, aggro: MobAggro.Sight);
        p[Mob.Jester] = P(48, MobElement.Wind, aggro: MobAggro.Sight);
        p[Mob.Geshunpest2] = P(48, MobElement.Wind, aggro: MobAggro.Proximity);
        p[Mob.Bombadeel] = P(48, MobElement.Ice, aggro: MobAggro.Proximity);
    }

    private static MobProfile P(
        byte level,
        MobElement weaknesses,
        MobSusceptibility susceptible = MobSusceptibility.None,
        MobSpawnCondition spawn = MobSpawnCondition.None,
        MobAggro aggro = MobAggro.Unknown) =>
        new(weaknesses, level, spawn, susceptible, aggro);

    private static MobSusceptibility S(
        bool doom = false,
        bool ashkin = false,
        bool paralysis = false,
        bool stop = false,
        bool slow = false,
        bool blind = false,
        bool stun = false,
        bool frog = false,
        bool sleep = false,
        bool heavy = false,
        bool bind = false)
    {
        MobSusceptibility flags = MobSusceptibility.None;
        if (doom)
        {
            flags |= MobSusceptibility.Doom;
        }

        if (ashkin)
        {
            flags |= MobSusceptibility.Ashkin;
        }

        if (paralysis)
        {
            flags |= MobSusceptibility.Paralysis;
        }

        if (stop)
        {
            flags |= MobSusceptibility.Stop;
        }

        if (slow)
        {
            flags |= MobSusceptibility.Slow;
        }

        if (blind)
        {
            flags |= MobSusceptibility.Blind;
        }

        if (stun)
        {
            flags |= MobSusceptibility.Stun;
        }

        if (frog)
        {
            flags |= MobSusceptibility.Frog;
        }

        if (sleep)
        {
            flags |= MobSusceptibility.Sleep;
        }

        if (heavy)
        {
            flags |= MobSusceptibility.Heavy;
        }

        if (bind)
        {
            flags |= MobSusceptibility.Bind;
        }

        return flags;
    }
}
