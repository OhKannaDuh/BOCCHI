namespace BOCCHI.Common.Data.Zones;

/// <summary>
///     Geographic north/south for each Occult Crescent pot FATE pair (within the zone, not Horn).
///     IDs match <c>GetPotFateData()</c> on South Horn / North Horn (lower world Z = north).
/// </summary>
public static class PotFateGeography
{
    public enum Side
    {
        North,
        South,
    }

    /// <summary>
    ///     South Horn: Persistent Pots (north), Pleading Pots (south).
    ///     North Horn: Daylight Pottery (north), In a Pot of Bother (south).
    /// </summary>
    public static bool TryGetSide(int fateId, out Side side)
    {
        switch (fateId)
        {
            case 1976: // Persistent Pots
            case 2072: // Daylight Pottery
                side = Side.North;
                return true;
            case 1977: // Pleading Pots
            case 2073: // In a Pot of Bother
                side = Side.South;
                return true;
            default:
                side = default;
                return false;
        }
    }
}
