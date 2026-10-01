using System.Numerics;

namespace BOCCHI.Automator.Services.PotTreasure;

public enum PotTreasureDirection
{
    Unknown = 0,
    North = 1,
    Northeast = 2,
    East = 3,
    Southeast = 4,
    South = 5,
    Southwest = 6,
    West = 7,
    Northwest = 8,
}

public enum PotTreasureHintKind
{
    None,
    Hint,
    CofferReveal,
    ElixirPrompt,
    BonusOffer,
}

public enum PotTreasureDistanceBucket
{
    Unknown,
    Immediate,
    Close,
    Far,
    BeyondFar,
}

public sealed class PotTreasureHintEvent
{
    public required PotTreasureHintKind Kind { get; init; }

    public uint LogMessageId { get; init; }

    public PotTreasureDirection Direction { get; init; }

    public PotTreasureDistanceBucket Distance { get; init; }

    public int Revision { get; set; }

    public Vector3? Origin { get; init; }
}

public static class PotTreasureIds
{
    public const uint TreasureBuffStatusId = 1531;

    public const uint MagicalElixirItemId = 2003296;

    public const uint CofferRevealLogMessageId = 10985;

    public const uint HintImmediateLogMessageId = 10986;

    public const uint HintCloseLogMessageId = 10987;

    public const uint HintFarLogMessageId = 10988;

    public const uint HintBeyondFarLogMessageId = 10989;

    public const uint ElixirPromptLogMessageId = 10990;

    public const uint BonusOfferLogMessageId = 10994;

    public static readonly uint[] RevealCofferBaseIds = [2014741, 2014742, 2014743];

    public static PotTreasureDirection MapDirection(int value) => value switch
    {
        1 => PotTreasureDirection.North,
        2 => PotTreasureDirection.Northeast,
        3 => PotTreasureDirection.East,
        4 => PotTreasureDirection.Southeast,
        5 => PotTreasureDirection.South,
        6 => PotTreasureDirection.Southwest,
        7 => PotTreasureDirection.West,
        8 => PotTreasureDirection.Northwest,
        _ => PotTreasureDirection.Unknown,
    };

    public static PotTreasureDistanceBucket MapDistance(uint logMessageId) => logMessageId switch
    {
        HintImmediateLogMessageId => PotTreasureDistanceBucket.Immediate,
        HintCloseLogMessageId => PotTreasureDistanceBucket.Close,
        HintFarLogMessageId => PotTreasureDistanceBucket.Far,
        HintBeyondFarLogMessageId => PotTreasureDistanceBucket.BeyondFar,
        _ => PotTreasureDistanceBucket.Unknown,
    };

    public static float RefineStep(PotTreasureDistanceBucket bucket) => bucket switch
    {
        PotTreasureDistanceBucket.Immediate => 8f,
        PotTreasureDistanceBucket.Close => 20f,
        PotTreasureDistanceBucket.Far => 40f,
        PotTreasureDistanceBucket.BeyondFar => 100f,
        _ => 20f,
    };
}
