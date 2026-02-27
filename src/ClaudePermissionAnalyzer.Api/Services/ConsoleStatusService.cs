using System.Text;

namespace ClaudePermissionAnalyzer.Api.Services;

/// <summary>
/// Tracks aggregated hook event stats and renders a fixed multi-line console block
/// using ANSI escape codes for in-place updates. Refreshes on a 500ms timer to
/// avoid flickering from rapid event bursts.
/// </summary>
public class ConsoleStatusService : IDisposable
{
    private const int DisplayLines = 4;

    private readonly EnforcementService _enforcementService;
    private readonly object _writeLock = new();
    private readonly Timer _renderTimer;
    private bool _dirty;
    private bool _rendered;

    private long _totalEvents;
    private long _approved;
    private long _denied;
    private long _passthrough;
    private long _scoredEvents;
    private long _totalScore;

    private readonly Dictionary<string, long> _toolCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _countsLock = new();

    public ConsoleStatusService(EnforcementService enforcementService)
    {
        _enforcementService = enforcementService;
        _renderTimer = new Timer(_ => FlushRender(), null, 500, 500);
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

        var tool = toolName ?? "other";
        lock (_countsLock)
        {
            _toolCounts.TryGetValue(tool, out var count);
            _toolCounts[tool] = count + 1;
        }

        _dirty = true;
    }

    private void FlushRender()
    {
        if (!_dirty) return;
        _dirty = false;
        Render();
    }

    private void Render()
    {
        var total = Interlocked.Read(ref _totalEvents);
        var approved = Interlocked.Read(ref _approved);
        var denied = Interlocked.Read(ref _denied);
        var passthrough = Interlocked.Read(ref _passthrough);
        var scored = Interlocked.Read(ref _scoredEvents);
        var avgScore = scored > 0 ? Interlocked.Read(ref _totalScore) / scored : 0;
        var mode = _enforcementService.IsEnforced ? "ENFORCE" : "OBSERVE";

        // Build tool breakdown pairs
        KeyValuePair<string, long>[] tools;
        lock (_countsLock)
        {
            tools = _toolCounts.OrderByDescending(kv => kv.Value).ToArray();
        }

        // Line 1: mode + summary
        var line1 = $"  {mode} | {total} events | approved:{approved}  denied:{denied}  pass:{passthrough}";
        if (scored > 0) line1 += $"  avg-score:{avgScore}";

        // Line 2-3: tool breakdown (wrap at ~70 chars)
        var toolLines = new List<string>();
        var current = new StringBuilder("  Tools: ");
        foreach (var kv in tools)
        {
            var entry = $"{kv.Key}:{kv.Value}  ";
            if (current.Length + entry.Length > 78 && current.Length > 10)
            {
                toolLines.Add(current.ToString());
                current = new StringBuilder("         ");
            }
            current.Append(entry);
        }
        if (current.Length > 10) toolLines.Add(current.ToString());

        // Assemble output block (always exactly DisplayLines lines)
        var lines = new string[DisplayLines];
        lines[0] = line1;
        for (int i = 0; i < DisplayLines - 1; i++)
            lines[i + 1] = i < toolLines.Count ? toolLines[i] : "";

        lock (_writeLock)
        {
            var sb = new StringBuilder();

            // Move cursor up to overwrite previous block
            if (_rendered)
                sb.Append($"\x1b[{DisplayLines}A");

            for (int i = 0; i < DisplayLines; i++)
            {
                sb.Append("\x1b[2K"); // Clear entire line
                sb.AppendLine(lines[i]);
            }

            _rendered = true;
            Console.Write(sb);
        }
    }

    public void Dispose()
    {
        _renderTimer.Dispose();
    }
}
