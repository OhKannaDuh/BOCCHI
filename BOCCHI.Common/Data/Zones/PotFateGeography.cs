namespace BOCCHI.Common.Data.Zones;

public static class PotFateGeography
{
    public enum Side
    {
        North,
        South,
    }

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
