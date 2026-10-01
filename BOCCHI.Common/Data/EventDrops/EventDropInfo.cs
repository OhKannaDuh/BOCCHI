namespace BOCCHI.Common.Data.EventDrops;

public readonly record struct EventDropInfo(Demiatma? Demiatma, MonsterNote? Notes, SoulShard? SoulShard)
{
    public bool HasAny => Demiatma is not null || Notes is not null || SoulShard is not null;
}

public enum Demiatma : uint
{
    Azurite = 47744,
    Verdigris = 47745,
    Malachite = 47746,
    Realgar = 47747,
    CaputMortuum = 47748,
    Orpiment = 47749,
}

public enum SoulShard : uint
{
    Berserker = 47751,
    Ranger = 47752,
    Oracle = 47757,
}
