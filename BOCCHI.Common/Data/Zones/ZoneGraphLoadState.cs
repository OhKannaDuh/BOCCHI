namespace BOCCHI.Common.Data.Zones;

public enum ZoneGraphSource
{
    None = 0,
    Cache = 1,
    Shipped = 2,
    Built = 3,
}

public enum ZoneGraphLoadState
{
    Idle = 0,

    Loading = 1,

    Building = 2,

    Ready = 3,
}
