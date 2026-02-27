using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Services;

public class TranscriptWatcher : IDisposable
{
    private readonly string _claudeProjectsDir;
    private readonly ILogger<TranscriptWatcher> _logger;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, long> _filePositions = new();
    private int _disposed;

    public event EventHandler<TranscriptEventArgs>? TranscriptUpdated;

    public TranscriptWatcher(ILogger<TranscriptWatcher> logger)
    {
        _logger = logger;
        _claudeProjectsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects");
    }

    public void Start()
    {
        if (!Directory.Exists(_claudeProjectsDir))
        {
            _logger.LogDebug("Claude projects directory not found at {Dir}, transcript watching disabled", _claudeProjectsDir);
            return;
        }

        _logger.LogDebug("Starting transcript watcher for {Dir}", _claudeProjectsDir);

        // Watch the top-level projects directory for new project folders
        try
        {
            var topWatcher = new FileSystemWatcher(_claudeProjectsDir)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            topWatcher.Created += OnProjectDirectoryCreated;
            _watchers["__top__"] = topWatcher;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to watch top-level projects directory");
        }

        // Set up watchers for existing project directories
        foreach (var projectDir in GetProjectDirectories())
        {
            WatchProjectDirectory(projectDir);
        }
    }

    public List<ClaudeProject> GetProjects()
    {
        var projects = new List<ClaudeProject>();

        if (!Directory.Exists(_claudeProjectsDir))
            return projects;

        foreach (var projectDir in GetProjectDirectories())
        {
            var projectName = Path.GetFileName(projectDir);
            var sessions = GetSessionsForProject(projectDir);
            projects.Add(new ClaudeProject
            {
                Name = projectName,
                Path = projectDir,
                Sessions = sessions
            });
        }

        return projects;
    }

    public List<TranscriptEntry> GetTranscript(string sessionId)
    {
        var entries = new List<TranscriptEntry>();
        var file = FindTranscriptFile(sessionId);

        if (file == null || !File.Exists(file))
            return entries;

        try
        {
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<TranscriptEntry>(line);
                    if (entry != null)
                        entries.Add(entry);
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read transcript for session {SessionId}", sessionId);
        }

        return entries;
    }

    public string? FindTranscriptFile(string sessionId)
    {
        if (!Directory.Exists(_claudeProjectsDir))
            return null;

        foreach (var projectDir in GetProjectDirectories())
        {
            var jsonlFiles = Directory.GetFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly);
            foreach (var file in jsonlFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                if (fileName.Equals(sessionId, StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }

        return null;
    }

    private IEnumerable<string> GetProjectDirectories()
    {
        if (!Directory.Exists(_claudeProjectsDir))
            return Enumerable.Empty<string>();

        try
        {
            return Directory.GetDirectories(_claudeProjectsDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate project directories");
            return Enumerable.Empty<string>();
        }
    }

    private List<ClaudeSession> GetSessionsForProject(string projectDir)
    {
        var sessions = new List<ClaudeSession>();

        try
        {
            var jsonlFiles = Directory.GetFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly);
            foreach (var file in jsonlFiles)
            {
                var fileInfo = new FileInfo(file);
                sessions.Add(new ClaudeSession
                {
                    SessionId = Path.GetFileNameWithoutExtension(file),
                    FilePath = file,
                    LastModified = fileInfo.LastWriteTimeUtc,
                    SizeBytes = fileInfo.Length
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate sessions in {ProjectDir}", projectDir);
        }

        return sessions.OrderByDescending(s => s.LastModified).ToList();
    }

    private void WatchProjectDirectory(string projectDir)
    {
        if (_watchers.ContainsKey(projectDir))
            return;

        try
        {
            var watcher = new FileSystemWatcher(projectDir, "*.jsonl")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnTranscriptFileChanged;
            watcher.Created += OnTranscriptFileChanged;

            _watchers[projectDir] = watcher;
            _logger.LogDebug("Watching for transcript changes in {Dir}", projectDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to watch project directory {Dir}", projectDir);
        }
    }

    private void OnProjectDirectoryCreated(object sender, FileSystemEventArgs e)
    {
        if (Directory.Exists(e.FullPath))
        {
            WatchProjectDirectory(e.FullPath);
        }
    }

    private void OnTranscriptFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            var sessionId = Path.GetFileNameWithoutExtension(e.FullPath);
            var newEntries = ReadNewEntries(e.FullPath);

            if (newEntries.Count > 0)
            {
                TranscriptUpdated?.Invoke(this, new TranscriptEventArgs
                {
                    SessionId = sessionId,
                    NewEntries = newEntries
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error processing transcript file change: {File}", e.FullPath);
        }
    }

    private List<TranscriptEntry> ReadNewEntries(string filePath)
    {
        var entries = new List<TranscriptEntry>();

        var lastPos = _filePositions.GetOrAdd(filePath, 0);

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length <= lastPos)
                return entries;

            stream.Seek(lastPos, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<TranscriptEntry>(line);
                    if (entry != null)
                        entries.Add(entry);
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            _filePositions[filePath] = stream.Position;
        }
        catch (IOException ex)
        {
            _logger.LogDebug(ex, "Could not read new entries from {File}", filePath);
        }

        return entries;
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        foreach (var watcher in _watchers.Values)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error disposing file watcher");
            }
        }

        _watchers.Clear();
        GC.SuppressFinalize(this);
    }
}

public class TranscriptEventArgs : EventArgs
{
    public string SessionId { get; set; } = string.Empty;
    public List<TranscriptEntry> NewEntries { get; set; } = new();
}

public class TranscriptEntry
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string? Type { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("parentUuid")]
    public string? ParentUuid { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string? Version { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public JsonElement? Message { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    /// <summary>
    /// Extracts a display-friendly summary of the message content.
    /// </summary>
    public string? GetMessageSummary()
    {
        if (Message == null || Message.Value.ValueKind == JsonValueKind.Undefined)
            return null;

        try
        {
            var msg = Message.Value;
            // User/assistant messages have { role, content }
            if (msg.ValueKind == JsonValueKind.Object)
            {
                if (msg.TryGetProperty("content", out var content))
                {
                    if (content.ValueKind == JsonValueKind.String)
                        return content.GetString();
                    if (content.ValueKind == JsonValueKind.Array && content.GetArrayLength() > 0)
                    {
                        var first = content[0];
                        if (first.TryGetProperty("text", out var text))
                            return text.GetString();
                    }
                }
                if (msg.TryGetProperty("role", out var role))
                    return $"[{role.GetString()}]";
            }
            if (msg.ValueKind == JsonValueKind.String)
                return msg.GetString();
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Extracts role from message if present.
    /// </summary>
    public string? GetRole()
    {
        if (Message == null || Message.Value.ValueKind != JsonValueKind.Object)
            return null;
        try
        {
            if (Message.Value.TryGetProperty("role", out var role))
                return role.GetString();
        }
        catch { }
        return null;
    }
}

public class ClaudeProject
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public List<ClaudeSession> Sessions { get; set; } = new();
}

public class ClaudeSession
{
    public string SessionId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long SizeBytes { get; set; }
}
