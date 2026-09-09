namespace BOCCHI.Treasure.Hunt;

/// <summary>Debounce empty-pad confirmation so a one-frame miss does not skip a live pad.</summary>
public sealed class EmptyPadConfirm
{
    private long? candidateKey;
    private DateTime candidateSinceUtc = DateTime.MinValue;

    public bool Tick(long key, TimeSpan delay)
    {
        DateTime now = DateTime.UtcNow;
        if (candidateKey != key)
        {
            candidateKey = key;
            candidateSinceUtc = now;
            return false;
        }

        return now - candidateSinceUtc >= delay;
    }

    public void Clear()
    {
        candidateKey = null;
        candidateSinceUtc = DateTime.MinValue;
    }
}
