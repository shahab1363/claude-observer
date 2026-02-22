using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Services;

public class SessionManager : IDisposable
{
    private readonly string _storageDir;
    private readonly int _maxHistorySize;
    private readonly IMemoryCache _sessionCache;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sessionLocks;
    private readonly ConcurrentBag<Task> _disposalTasks;
    private readonly SemaphoreSlim _cacheLoadLock;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(24);
    private readonly ILogger<SessionManager>? _logger;
    private int _disposed;

    // Cached serializer options to avoid repeated allocation
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    // Cache size limit to prevent unbounded memory growth
    private const int MaxCachedSessions = 1000;

    public SessionManager(string storageDir, int maxHistorySize = 50, IMemoryCache? memoryCache = null, ILogger<SessionManager>? logger = null)
    {
        _storageDir = ExpandPath(storageDir);
        _maxHistorySize = maxHistorySize;
        _sessionCache = memoryCache ?? new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = MaxCachedSessions
        });
        _sessionLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _disposalTasks = new ConcurrentBag<Task>();
        _cacheLoadLock = new SemaphoreSlim(1, 1);
        _logger = logger;
        _disposed = 0;

        if (!Directory.Exists(_storageDir))
        {
            try
            {
                Directory.CreateDirectory(_storageDir);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new StorageException(
                    $"Cannot create session storage directory '{_storageDir}': Permission denied. " +
                    $"Ensure the application has write access to this location or configure a different StorageDir.", ex);
            }
            catch (Exception ex)
            {
                throw new StorageException(
                    $"Cannot create session storage directory '{_storageDir}': {ex.Message}", ex);
            }
        }
    }

    public async Task<SessionData> GetOrCreateSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Fast path: check cache without lock
        if (_sessionCache.TryGetValue(sessionId, out var cachedObj) && cachedObj is SessionData cached)
        {
            return cached;
        }

        // Slow path: acquire global cache load lock
        await _cacheLoadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_sessionCache.TryGetValue(sessionId, out var existingObj) && existingObj is SessionData existing)
            {
                return existing;
            }

            var filePath = GetSessionFilePath(sessionId);
            SessionData session;

            if (File.Exists(filePath))
            {
                try
                {
                    // Use async stream reading for better memory efficiency
                    await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                    var deserialized = await JsonSerializer.DeserializeAsync<SessionData>(stream, s_readOptions, cancellationToken).ConfigureAwait(false);

                    if (deserialized == null)
                    {
                        _logger?.LogError("Session file {FilePath} contained null data, session history lost", filePath);
                        throw new StorageException(
                            $"Cannot load session {sessionId}: Session file is corrupted. " +
                            $"Session history has been lost. File: {filePath}");
                    }

                    // Validate deserialized data
                    if (deserialized.SessionId != sessionId)
                    {
                        _logger?.LogError("Session file {FilePath} has mismatched SessionId: expected {Expected}, found {Found}",
                            filePath, sessionId, deserialized.SessionId);
                        throw new StorageException(
                            $"Session file corruption detected: SessionId mismatch");
                    }

                    if (deserialized.ConversationHistory == null)
                    {
                        _logger?.LogWarning("Session {SessionId} has null ConversationHistory, initializing empty list", sessionId);
                        deserialized.ConversationHistory = new List<SessionEvent>();
                    }

                    session = deserialized;
                }
                catch (JsonException ex)
                {
                    _logger?.LogError(ex, "Failed to parse session file {FilePath} for session {SessionId}, session history lost",
                        filePath, sessionId);
                    throw new StorageException(
                        $"Cannot load session {sessionId}: Session file is corrupted. " +
                        $"Session history has been lost. File: {filePath}", ex);
                }
                catch (IOException ex)
                {
                    _logger?.LogError(ex, "Failed to read session file {FilePath} for session {SessionId}", filePath, sessionId);
                    throw new StorageException(
                        $"Cannot load session {sessionId}: File system error. " +
                        $"Check disk health and permissions. File: {filePath}", ex);
                }
            }
            else
            {
                session = new SessionData(sessionId);
                await SaveSessionInternalAsync(session, cancellationToken).ConfigureAwait(false);
            }

            // Cache with sliding expiration and size=1 for size-limited cache
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(_cacheExpiration)
                .SetSize(1)
                .RegisterPostEvictionCallback((key, value, reason, state) =>
                {
                    // Clean up session lock when evicted
                    if (key is string sessId && _sessionLocks.TryRemove(sessId, out var lockToDispose))
                    {
                        var disposalTask = Task.Run(async () =>
                        {
                            await Task.Delay(50).ConfigureAwait(false);
                            try
                            {
                                lockToDispose.Dispose();
                            }
                            catch (ObjectDisposedException)
                            {
                                // Lock already disposed, ignore
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogWarning(ex, "Failed to dispose session lock for {SessionId}", sessId);
                            }
                        });
                        _disposalTasks.Add(disposalTask);
                    }
                });

            _sessionCache.Set(sessionId, session, cacheOptions);
            return session;
        }
        finally
        {
            _cacheLoadLock.Release();
        }
    }

    public async Task RecordEventAsync(string sessionId, SessionEvent evt, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var session = await GetOrCreateSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);

        // Get or create a session-specific lock for thread-safe list modifications
        var sessionLock = _sessionLocks.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));

        await sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            session.ConversationHistory.Add(evt);
            session.LastActivity = DateTime.UtcNow;

            // Trim history if exceeds max size
            while (session.ConversationHistory.Count > _maxHistorySize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                session.ConversationHistory.RemoveAt(0);
            }
        }
        finally
        {
            sessionLock.Release();
        }

        await SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<SessionData>> GetAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var sessions = new List<SessionData>();
        if (!Directory.Exists(_storageDir))
            return sessions;

        var files = Directory.GetFiles(_storageDir, "*.json");
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, cancellationToken);
                var session = JsonSerializer.Deserialize<SessionData>(json);
                if (session != null)
                    sessions.Add(session);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load session file {File}", file);
            }
        }

        return sessions;
    }

    public Task<int> ClearAllSessionsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!Directory.Exists(_storageDir))
            return Task.FromResult(0);

        var files = Directory.GetFiles(_storageDir, "*.json");
        int deleted = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to delete session file {File}", file);
            }
        }

        _logger?.LogInformation("Cleared {Count} session files", deleted);
        return Task.FromResult(deleted);
    }

    public async Task<string> BuildContextAsync(string sessionId, int maxEvents = 10, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var session = await GetOrCreateSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var recentEvents = session.ConversationHistory
            .TakeLast(maxEvents)
            .ToList();

        var sb = new StringBuilder(256);
        sb.AppendLine("RECENT SESSION HISTORY:");

        foreach (var evt in recentEvents)
        {
            sb.Append('[').Append(evt.Timestamp.ToString("HH:mm:ss")).Append("] ").AppendLine(evt.Type);

            if (!string.IsNullOrEmpty(evt.ToolName))
            {
                sb.Append("  Tool: ").AppendLine(evt.ToolName);
            }

            if (!string.IsNullOrEmpty(evt.Decision))
            {
                sb.Append("  Decision: ").Append(evt.Decision).Append(" (Score: ").Append(evt.SafetyScore).AppendLine(")");
            }

            if (!string.IsNullOrEmpty(evt.Content))
            {
                sb.Append("  Content: ").AppendLine(evt.Content);
            }
        }

        return sb.ToString();
    }

    private async Task SaveSessionAsync(SessionData session, CancellationToken cancellationToken = default)
    {
        // Use per-session lock instead of global file lock to allow concurrent saves of different sessions
        var sessionLock = _sessionLocks.GetOrAdd(session.SessionId, _ => new SemaphoreSlim(1, 1));

        await sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveSessionInternalAsync(session, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sessionLock.Release();
        }
    }

    private async Task SaveSessionInternalAsync(SessionData session, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var filePath = GetSessionFilePath(session.SessionId);

        try
        {
            // Use buffered async file stream for efficient writes
            await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await JsonSerializer.SerializeAsync(stream, session, s_writeOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "Failed to save session {SessionId} to {FilePath}: Permission denied", session.SessionId, filePath);
            throw new StorageException($"Failed to save session {session.SessionId}: Permission denied. Check file system permissions.", ex);
        }
        catch (IOException ex)
        {
            _logger?.LogError(ex, "Failed to save session {SessionId} to {FilePath}", session.SessionId, filePath);
            throw new StorageException($"Failed to save session {session.SessionId}: {ex.Message}", ex);
        }
    }

    private string GetSessionFilePath(string sessionId)
    {
        // Validate session ID to prevent path traversal attacks
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session ID cannot be empty", nameof(sessionId));
        }

        // Check for path traversal characters
        var invalidChars = Path.GetInvalidFileNameChars();
        if (sessionId.Any(c => invalidChars.Contains(c) || c == '.' || c == '/' || c == '\\'))
        {
            throw new ArgumentException($"Session ID contains invalid characters: {sessionId}", nameof(sessionId));
        }

        var filePath = Path.Combine(_storageDir, $"{sessionId}.json");

        // Verify the resolved path is within storage directory (defense in depth)
        var fullPath = Path.GetFullPath(filePath);
        var fullStorageDir = Path.GetFullPath(_storageDir);

        if (!fullPath.StartsWith(fullStorageDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path traversal detected in session ID: {sessionId}");
        }

        return filePath;
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.Substring(2));
        }
        return path;
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        // Wait for all background disposal tasks to complete
        try
        {
            Task.WaitAll(_disposalTasks.ToArray(), TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex)
        {
            _logger?.LogWarning(ex, "Some disposal tasks did not complete within timeout");
        }

        // Dispose all session locks
        foreach (var lockEntry in _sessionLocks)
        {
            try
            {
                lockEntry.Value.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed
            }
        }

        _sessionLocks.Clear();

        // Dispose global lock
        _cacheLoadLock?.Dispose();

        GC.SuppressFinalize(this);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed != 0)
            throw new ObjectDisposedException(nameof(SessionManager));
    }
}
