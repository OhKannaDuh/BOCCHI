namespace BOCCHI.Treasure.Services;

public interface ITreasureTracker
{
    IReadOnlyList<TreasureCoffer> Treasures { get; }

    bool CountInitialised { get; }

    DateTime LastCountUpdateUtc { get; }

    int BronzeChests { get; }

    int SilverChests { get; }

    int SurveyRevision { get; }
}
