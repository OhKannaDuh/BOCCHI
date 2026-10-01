namespace BOCCHI.Treasure.Services;

public enum CarrotHuntPhase
{
    Idle = 0,
    Pathing = 1,
    UsingItem = 2,
    WaitingForBunny = 3,
    OpeningBunny = 4,
    ApproachingAetheryte = 5,
    Teleporting = 6,
    Returning = 7
}

public interface ICarrotHunter
{
    bool Running { get; }

    bool Paused { get; }

    CarrotHuntPhase Phase { get; }

    TimeSpan Elapsed { get; }

    int FortuneCarrotsRemaining { get; }

    bool IsVnavAvailable { get; }

    bool IsVnavReady { get; }

    void Toggle();

    void Pause();

    void Resume();

    bool UseFortuneCarrot();
}
