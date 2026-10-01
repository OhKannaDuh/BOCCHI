namespace BOCCHI.Treasure.Hunt;

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
