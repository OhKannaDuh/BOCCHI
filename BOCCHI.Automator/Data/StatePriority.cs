namespace BOCCHI.Automator.Data;

public enum StatePriority
{
    Never = int.MinValue,

    Always = int.MaxValue,

    Lowest = -300,

    VeryLow = -200,

    Low = -100,

    BelowNormal = -10,

    Normal = 0,

    AboveNormal = 10,

    MediumHigh = 100,

    High = 200,

    VeryHigh = 300,

    Critical = 1000,
}
