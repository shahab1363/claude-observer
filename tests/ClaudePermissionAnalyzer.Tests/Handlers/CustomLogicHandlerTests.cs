using ClaudePermissionAnalyzer.Api.Handlers;
using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Handlers;

public class CustomLogicHandlerTests : IDisposable
{
    private readonly string _testStorageDir;
    private readonly SessionManager _sessionManager;

    public CustomLogicHandlerTests()
    {
        _testStorageDir = Path.Combine(Path.GetTempPath(), $"test-sessions-{Guid.NewGuid():N}");
        _sessionManager = new SessionManager(_testStorageDir, 50, new MemoryCache(new MemoryCacheOptions()));
    }

    [Fact]
    public async Task HandleAsync_SessionStart_ShouldCreateSession()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<CustomLogicHandler>>();
        var handler = new CustomLogicHandler(_sessionManager, mockLogger.Object);
        var input = new HookInput
        {
            HookEventName = "SessionStart",
            SessionId = "session-start-test",
            Cwd = Path.GetTempPath()
        };
        var config = new HandlerConfig
        {
            Name = "session-initializer",
            Mode = "custom-logic",
            Config = new Dictionary<string, object>
            {
                { "loadProjectContext", false },
                { "checkGitStatus", false }
            }
        };

        // Act
        var output = await handler.HandleAsync(input, config, "");

        // Assert
        Assert.Equal("session-start", output.Category);
        Assert.Equal("Session initialized", output.Reasoning);
    }

    [Fact]
    public async Task HandleAsync_SessionEnd_ShouldArchiveSession()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<CustomLogicHandler>>();
        var handler = new CustomLogicHandler(_sessionManager, mockLogger.Object);
        var input = new HookInput
        {
            HookEventName = "SessionEnd",
            SessionId = "session-end-test"
        };
        var config = new HandlerConfig
        {
            Name = "session-cleanup",
            Mode = "custom-logic",
            Config = new Dictionary<string, object>
            {
                { "archiveSession", true }
            }
        };

        // Act
        var output = await handler.HandleAsync(input, config, "");

        // Assert
        Assert.Equal("session-end", output.Category);
        Assert.Equal("Session cleanup complete", output.Reasoning);
    }

    [Fact]
    public async Task HandleAsync_UnknownEvent_ShouldReturnCustomCategory()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<CustomLogicHandler>>();
        var handler = new CustomLogicHandler(_sessionManager, mockLogger.Object);
        var input = new HookInput
        {
            HookEventName = "UnknownEvent",
            SessionId = "test-unknown"
        };
        var config = new HandlerConfig { Mode = "custom-logic" };

        // Act
        var output = await handler.HandleAsync(input, config, "");

        // Assert
        Assert.Equal("custom", output.Category);
    }

    public void Dispose()
    {
        _sessionManager.Dispose();
        try
        {
            if (Directory.Exists(_testStorageDir))
                Directory.Delete(_testStorageDir, true);
        }
        catch { }
    }
}
