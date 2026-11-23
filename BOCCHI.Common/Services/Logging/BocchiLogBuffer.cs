using System.Text;

namespace BOCCHI.Common.Services.Logging;

public enum BocchiLogLevel
{
    Verbose = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
}

public sealed class BocchiLogEntry
{
    public DateTime Timestamp { get; init; }

    public DateTime LastOccurrence { get; set; }

    public BocchiLogLevel Level { get; init; }

    public string Message { get; init; } = string.Empty;

    public int Count { get; set; } = 1;

    public string Key => $"{(int)Level}|{Message}";
}

public interface IBocchiLogBuffer
{
    int Version { get; }

    IReadOnlyList<BocchiLogEntry> Snapshot();

    void Append(BocchiLogLevel level, string message);

    void Clear();

    string FormatAllForClipboard(string header);
}

public interface IBocchiLogClipboard
{
    void CopyAll(bool announceInChat = false);
}

public sealed class BocchiLogBuffer : IBocchiLogBuffer
{
    private const int MaxLogCount = 10_000;

    private static readonly TimeSpan ConsolidationWindow = TimeSpan.FromMilliseconds(250);

    private readonly object gate = new();

    private readonly List<BocchiLogEntry> logs = [];

    private readonly Dictionary<string, BocchiLogEntry> recent = new(StringComparer.Ordinal);

    private int version;

    public int Version => Volatile.Read(ref version);

    public IReadOnlyList<BocchiLogEntry> Snapshot()
    {
        lock (gate)
        {
            return logs.ToList();
        }
    }

    public void Append(BocchiLogLevel level, string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        DateTime now = DateTime.Now;
        string key = $"{(int)level}|{message}";

        lock (gate)
        {
            version++;
            if (recent.TryGetValue(key, out BocchiLogEntry? recentEntry))
            {
                if (now - recentEntry.LastOccurrence <= ConsolidationWindow)
                {
                    recentEntry.Count++;
                    recentEntry.LastOccurrence = now;
                    return;
                }

                recent.Remove(key);
            }

            if (logs.Count > 0)
            {
                BocchiLogEntry last = logs[^1];
                if (last.Key == key)
                {
                    last.Count++;
                    last.LastOccurrence = now;
                    recent[key] = last;
                    return;
                }
            }

            var entry = new BocchiLogEntry
            {
                Timestamp = now,
                LastOccurrence = now,
                Level = level,
                Message = message,
                Count = 1,
            };
            logs.Add(entry);
            recent[key] = entry;

            CleanupRecent(now);

            while (logs.Count > MaxLogCount)
            {
                logs.RemoveAt(0);
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            version++;
            logs.Clear();
            recent.Clear();
        }
    }

    public string FormatAllForClipboard(string header)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(header))
        {
            sb.AppendLine(header.TrimEnd());
            sb.AppendLine();
        }

        List<BocchiLogEntry> copy;
        lock (gate)
        {
            copy = logs.ToList();
        }

        foreach (BocchiLogEntry log in copy)
        {
            if (log.Level == BocchiLogLevel.Verbose)
            {
                continue;
            }

            string countSuffix = log.Count > 1 ? $" (x{log.Count})" : string.Empty;
            string time = log.Count > 1
                ? $"{log.Timestamp:HH:mm:ss} - {log.LastOccurrence:HH:mm:ss}"
                : $"{log.Timestamp:HH:mm:ss}";
            sb.AppendLine($"[{time}] [{log.Level}] {log.Message}{countSuffix}");
        }

        return sb.ToString();
    }

    private void CleanupRecent(DateTime now)
    {
        List<string> stale = recent
            .Where(kvp => now - kvp.Value.LastOccurrence > ConsolidationWindow)
            .Select(kvp => kvp.Key)
            .ToList();
        foreach (string key in stale)
        {
            recent.Remove(key);
        }
    }
}
