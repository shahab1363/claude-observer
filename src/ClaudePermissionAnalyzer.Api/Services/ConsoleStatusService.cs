using System.Text;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// Tracks aggregated hook event stats and renders an in-place console status line.
/// Thread-safe — called from concurrent HTTP request threads.
/// </summary>
public class ConsoleStatusService
{
    private readonly string _mode;
    private readonly object _writeLock = new();
    private long _totalEvents;
    private long _approved;
    private long _denied;
    private long _passthrough;
    private long _totalLatencyMs;
    private long _scoredEvents;
    private long _totalScore;

    // Per-event-type counters
    private readonly Dictionary<string, long> _eventTypeCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _countsLock = new();

    public ConsoleStatusService(string mode, string url)
    {
        _mode = mode;
    }

    public void RecordEvent(string? decision, string? toolName, int? safetyScore, long? elapsedMs)
    {
        Interlocked.Increment(ref _totalEvents);

        switch (decision)
        {
            case "auto-approved": Interlocked.Increment(ref _approved); break;
            case "denied": Interlocked.Increment(ref _denied); break;
            default: Interlocked.Increment(ref _passthrough); break;
        }

        if (safetyScore.HasValue)
        {
            Interlocked.Increment(ref _scoredEvents);
            Interlocked.Add(ref _totalScore, safetyScore.Value);
        }

        if (elapsedMs.HasValue && elapsedMs.Value > 0)
        {
            Interlocked.Add(ref _totalLatencyMs, elapsedMs.Value);
        }

        // Track per-tool counts
        var tool = toolName ?? "other";
        lock (_countsLock)
        {
            _eventTypeCounts.TryGetValue(tool, out var count);
            _eventTypeCounts[tool] = count + 1;
        }

        Render();
    }

    private void Render()
    {
        var total = Interlocked.Read(ref _totalEvents);
        var approved = Interlocked.Read(ref _approved);
        var denied = Interlocked.Read(ref _denied);
        var scored = Interlocked.Read(ref _scoredEvents);
        var avgScore = scored > 0 ? Interlocked.Read(ref _totalScore) / scored : 0;

        // Build tools breakdown: "Bash:5 Read:3 Write:1"
        string toolsBreakdown;
        lock (_countsLock)
        {
            toolsBreakdown = string.Join("  ", _eventTypeCounts
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Key}:{kv.Value}"));
        }

        var sb = new StringBuilder();
        sb.Append($"\r  {_mode} | {total} events");
        if (approved > 0) sb.Append($" | {approved} approved");
        if (denied > 0) sb.Append($" | {denied} denied");
        if (scored > 0) sb.Append($" | avg:{avgScore}");
        sb.Append($" | {toolsBreakdown}");

        // Pad to clear previous line remnants
        var status = sb.ToString();
        if (status.Length < 120)
            status = status.PadRight(120);

        lock (_writeLock)
        {
            Console.Write(status);
        }
    }
}
