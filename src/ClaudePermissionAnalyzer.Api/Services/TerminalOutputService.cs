namespace ClaudePermissionAnalyzer.Api.Services;

public class TerminalLine
{
    public DateTime Timestamp { get; set; }
    public string Source { get; set; } = "";
    public string Level { get; set; } = "";
    public string Text { get; set; } = "";
    public long SequenceId { get; set; }
}

/// <summary>
/// Thread-safe ring buffer for real-time LLM subprocess output.
/// Stores up to 1000 lines and fires events for SSE consumers.
/// </summary>
public class TerminalOutputService
{
    private const int Capacity = 1000;
    private readonly TerminalLine[] _buffer = new TerminalLine[Capacity];
    private readonly object _lock = new();
    private int _head;
    private int _count;
    private long _sequenceCounter;

    public event EventHandler<TerminalLine>? LineReceived;

    public void Push(string source, string level, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        TerminalLine line;
        lock (_lock)
        {
            line = new TerminalLine
            {
                Timestamp = DateTime.UtcNow,
                Source = source,
                Level = level,
                Text = text,
                SequenceId = ++_sequenceCounter
            };

            _buffer[_head % Capacity] = line;
            _head++;
            if (_count < Capacity) _count++;
        }

        // Fire event outside the lock to avoid deadlocks
        LineReceived?.Invoke(this, line);
    }

    public IReadOnlyList<TerminalLine> GetBuffer()
    {
        lock (_lock)
        {
            var result = new List<TerminalLine>(_count);
            if (_count == 0) return result;

            int start = _count < Capacity ? 0 : _head % Capacity;
            for (int i = 0; i < _count; i++)
            {
                result.Add(_buffer[(start + i) % Capacity]);
            }
            return result;
        }
    }

    public IReadOnlyList<TerminalLine> GetBufferSince(long afterSequenceId)
    {
        lock (_lock)
        {
            var result = new List<TerminalLine>();
            if (_count == 0) return result;

            int start = _count < Capacity ? 0 : _head % Capacity;
            for (int i = 0; i < _count; i++)
            {
                var line = _buffer[(start + i) % Capacity];
                if (line.SequenceId > afterSequenceId)
                    result.Add(line);
            }
            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_buffer, 0, Capacity);
            _head = 0;
            _count = 0;
            // Don't reset _sequenceCounter — keeps SSE clients from getting stale data
        }
    }
}
