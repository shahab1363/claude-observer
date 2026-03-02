using Leash.Api.Models;
using Leash.Api.Services.Tray;
using Microsoft.Extensions.Logging.Abstractions;

namespace Leash.Tests.Services;

public class PendingDecisionServiceTests
{
    private readonly PendingDecisionService _service;

    public PendingDecisionServiceTests()
    {
        _service = new PendingDecisionService(NullLogger<PendingDecisionService>.Instance);
    }

    private static NotificationInfo CreateInfo(string tool = "Bash") => new()
    {
        Title = "Test",
        Body = "Test body",
        ToolName = tool,
        SafetyScore = 50,
        Level = NotificationLevel.Warning
    };

    [Fact]
    public void CreatePending_ReturnsIdAndTask()
    {
        var (id, task) = _service.CreatePending(CreateInfo(), TimeSpan.FromSeconds(10));

        Assert.NotNull(id);
        Assert.NotEmpty(id);
        Assert.False(task.IsCompleted);
    }

    [Fact]
    public async Task TryResolve_Approve_CompletesTask()
    {
        var (id, task) = _service.CreatePending(CreateInfo(), TimeSpan.FromSeconds(10));

        var resolved = _service.TryResolve(id, TrayDecision.Approve);

        Assert.True(resolved);
        Assert.True(task.IsCompleted);
        Assert.Equal(TrayDecision.Approve, await task);
    }

    [Fact]
    public async Task TryResolve_Deny_CompletesTask()
    {
        var (id, task) = _service.CreatePending(CreateInfo(), TimeSpan.FromSeconds(10));

        var resolved = _service.TryResolve(id, TrayDecision.Deny);

        Assert.True(resolved);
        Assert.True(task.IsCompleted);
        Assert.Equal(TrayDecision.Deny, await task);
    }

    [Fact]
    public void TryResolve_InvalidId_ReturnsFalse()
    {
        var resolved = _service.TryResolve("nonexistent", TrayDecision.Approve);
        Assert.False(resolved);
    }

    [Fact]
    public void TryResolve_AlreadyResolved_ReturnsFalse()
    {
        var (id, _) = _service.CreatePending(CreateInfo(), TimeSpan.FromSeconds(10));

        _service.TryResolve(id, TrayDecision.Approve);
        var secondResolve = _service.TryResolve(id, TrayDecision.Deny);

        Assert.False(secondResolve);
    }

    [Fact]
    public async Task Cancel_CompletesTaskWithNull()
    {
        var (id, task) = _service.CreatePending(CreateInfo(), TimeSpan.FromSeconds(10));

        var cancelled = _service.Cancel(id);

        Assert.True(cancelled);
        Assert.True(task.IsCompleted);
        Assert.Null(await task);
    }

    [Fact]
    public void Cancel_InvalidId_ReturnsFalse()
    {
        var cancelled = _service.Cancel("nonexistent");
        Assert.False(cancelled);
    }

    [Fact]
    public async Task Timeout_CompletesTaskWithNull()
    {
        var (_, task) = _service.CreatePending(CreateInfo(), TimeSpan.FromMilliseconds(50));

        var result = await task;

        Assert.Null(result);
    }

    [Fact]
    public void GetPending_ReturnsAllPending()
    {
        _service.CreatePending(CreateInfo("Bash"), TimeSpan.FromSeconds(10));
        _service.CreatePending(CreateInfo("Read"), TimeSpan.FromSeconds(10));

        var pending = _service.GetPending();

        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public void GetPending_ExcludesResolved()
    {
        var (id1, _) = _service.CreatePending(CreateInfo("Bash"), TimeSpan.FromSeconds(10));
        _service.CreatePending(CreateInfo("Read"), TimeSpan.FromSeconds(10));

        _service.TryResolve(id1, TrayDecision.Approve);
        var pending = _service.GetPending();

        Assert.Single(pending);
        Assert.Equal("Read", pending[0].Info.ToolName);
    }

    [Fact]
    public void ConcurrentAccess_ThreadSafe()
    {
        var ids = new List<string>();
        for (int i = 0; i < 100; i++)
        {
            var (id, _) = _service.CreatePending(CreateInfo($"Tool{i}"), TimeSpan.FromSeconds(30));
            ids.Add(id);
        }

        // Resolve all concurrently
        var results = new bool[100];
        Parallel.For(0, 100, i =>
        {
            results[i] = _service.TryResolve(ids[i], TrayDecision.Approve);
        });

        // All should resolve exactly once
        Assert.All(results, r => Assert.True(r));

        // None should be pending
        Assert.Empty(_service.GetPending());
    }
}
