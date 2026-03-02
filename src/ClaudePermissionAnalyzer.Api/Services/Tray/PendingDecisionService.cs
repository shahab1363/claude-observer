using System.Collections.Concurrent;
using ClaudePermissionAnalyzer.Api.Models;

namespace ClaudePermissionAnalyzer.Api.Services.Tray;

/// <summary>
/// Coordinates pending interactive decisions between the HTTP hook request
/// and the tray notification/web dashboard. Uses TaskCompletionSource to hold
/// the HTTP request open until the user responds or timeout occurs.
/// </summary>
public class PendingDecisionService
{
    private readonly ConcurrentDictionary<string, PendingDecision> _pending = new();
    private readonly ILogger<PendingDecisionService> _logger;

    public PendingDecisionService(ILogger<PendingDecisionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a pending decision that holds open until resolved or timed out.
    /// </summary>
    /// <returns>The decision ID and a Task that completes with the user's choice (or null on timeout).</returns>
    public (string Id, Task<TrayDecision?> Task) CreatePending(NotificationInfo info, TimeSpan timeout)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        var tcs = new TaskCompletionSource<TrayDecision?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var timeoutCts = new CancellationTokenSource(timeout);

        var decision = new PendingDecision
        {
            Id = id,
            Tcs = tcs,
            Info = info,
            TimeoutCts = timeoutCts
        };

        timeoutCts.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var expired))
            {
                expired.Tcs.TrySetResult(null);
                _logger.LogDebug("Pending decision {Id} timed out after {Timeout}s", id, timeout.TotalSeconds);
            }
        });

        _pending[id] = decision;
        _logger.LogDebug("Created pending decision {Id} for {Tool}", id, info.ToolName);

        return (id, tcs.Task);
    }

    /// <summary>
    /// Resolves a pending decision with the user's choice.
    /// Returns true if the decision was found and resolved.
    /// </summary>
    public bool TryResolve(string id, TrayDecision decision)
    {
        if (!_pending.TryRemove(id, out var pending))
            return false;

        pending.TimeoutCts.Cancel(); // cancel the timeout
        pending.TimeoutCts.Dispose();
        pending.Tcs.TrySetResult(decision);
        _logger.LogDebug("Resolved pending decision {Id} with {Decision}", id, decision);
        return true;
    }

    /// <summary>
    /// Cancels a pending decision (returns null to the waiting HTTP request).
    /// </summary>
    public bool Cancel(string id)
    {
        if (!_pending.TryRemove(id, out var pending))
            return false;

        pending.TimeoutCts.Cancel();
        pending.TimeoutCts.Dispose();
        pending.Tcs.TrySetResult(null);
        _logger.LogDebug("Cancelled pending decision {Id}", id);
        return true;
    }

    /// <summary>
    /// Gets all currently pending decisions (for web dashboard fallback).
    /// </summary>
    public IReadOnlyList<PendingDecision> GetPending()
    {
        return _pending.Values.ToList();
    }
}
