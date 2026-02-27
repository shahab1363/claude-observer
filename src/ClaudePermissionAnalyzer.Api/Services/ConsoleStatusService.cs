namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// Tracks aggregated hook event stats and renders an in-place console status line.
/// Thread-safe — called from concurrent HTTP request threads.
/// </summary>
public class ConsoleStatusService
{
    private long _totalEvents;
    private long _approved;
    private long _denied;
    private long _passthrough;
    private long _totalLatencyMs;
    private long _scoredEvents;
    private long _totalScore;
    private string? _lastTool;
    private long _lastLatencyMs;
    private readonly string _mode;
    private readonly string _url;
    private readonly object _writeLock = new();

    public ConsoleStatusService(string mode, string url)
    {
        _mode = mode;
        _url = url;
    }

    public void RecordEvent(string? decision, string? toolName, int? safetyScore, long? elapsedMs)
    {
        Interlocked.Increment(ref _totalEvents);

        switch (decision)
        {
            case "auto-approved":
                Interlocked.Increment(ref _approved);
                break;
            case "denied":
                Interlocked.Increment(ref _denied);
                break;
            default:
                Interlocked.Increment(ref _passthrough);
                break;
        }

        if (safetyScore.HasValue)
        {
            Interlocked.Increment(ref _scoredEvents);
            Interlocked.Add(ref _totalScore, safetyScore.Value);
        }

        if (elapsedMs.HasValue)
        {
            Interlocked.Add(ref _totalLatencyMs, elapsedMs.Value);
        }

        _lastTool = toolName ?? "unknown";
        _lastLatencyMs = elapsedMs ?? 0;

        Render();
    }

    public void Render()
    {
        var total = Interlocked.Read(ref _totalEvents);
        var approved = Interlocked.Read(ref _approved);
        var denied = Interlocked.Read(ref _denied);
        var passthrough = Interlocked.Read(ref _passthrough);
        var scored = Interlocked.Read(ref _scoredEvents);
        var avgScore = scored > 0 ? Interlocked.Read(ref _totalScore) / scored : 0;
        var avgLatency = total > 0 ? Interlocked.Read(ref _totalLatencyMs) / total : 0;

        var status = $"\r  {_mode} | {_url} | {total} events | {approved} approved | {denied} denied | {passthrough} pass";
        if (scored > 0)
            status += $" | avg score {avgScore}";
        if (total > 0)
            status += $" | avg {avgLatency}ms";
        if (_lastTool != null)
            status += $" | last: {_lastTool} {_lastLatencyMs}ms";

        // Pad to clear previous line remnants
        if (status.Length < 160)
            status = status.PadRight(160);

        lock (_writeLock)
        {
            Console.Write(status);
        }
    }
}
