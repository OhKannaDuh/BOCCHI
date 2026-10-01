namespace BOCCHI.Common.Data.Zones;

public enum ZoneId : ushort
{
    Unknown = 0,

    SouthHorn = 1252,

    NorthHorn = 1346,
}

public static class ZoneIdExtensions
{
    public static string TreasureDataFolder(this ZoneId zoneId) => zoneId switch
    {
        ZoneId.SouthHorn => "SouthHorn",
        ZoneId.NorthHorn => "NorthHorn",
        var _ => throw new NotSupportedException($"No treasure data folder for zone {zoneId}")
    };
}
