namespace BOCCHI.Common.Data.Fates;

public class FateScore
{
    private readonly Dictionary<string, float> sources = [];

    public float Value => Math.Max(sources.Values.Where(f => f is not (float.MaxValue or float.MinValue)).Sum(), 0f);

    public void Add(string source, float value)
    {
        sources[source] = value;
    }

    public static implicit operator float(FateScore score) => score.Value;

    public override string ToString() => Value.ToString("F2");
}
