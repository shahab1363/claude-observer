using ClaudePermissionAnalyzer.Api.Services;
using ClaudePermissionAnalyzer.Api.Models;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class SessionManagerTests
{
    [Fact]
    public async Task GetOrCreateSession_ShouldCreateNewSession_WhenNotExists()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"sessions-{Guid.NewGuid()}");
        var manager = new SessionManager(tempDir, maxHistorySize: 50);

        // Act
        var session = await manager.GetOrCreateSessionAsync("test-123");

        // Assert
        Assert.NotNull(session);
        Assert.Equal("test-123", session.SessionId);
        Assert.Empty(session.ConversationHistory);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task RecordEvent_ShouldAddEventToHistory()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"sessions-{Guid.NewGuid()}");
        var manager = new SessionManager(tempDir, maxHistorySize: 50);
        var session = await manager.GetOrCreateSessionAsync("test-123");

        var evt = new SessionEvent
        {
            Type = "permission-request",
            ToolName = "Bash",
            Decision = "auto-approved"
        };

        // Act
        await manager.RecordEventAsync("test-123", evt);
        var updatedSession = await manager.GetOrCreateSessionAsync("test-123");

        // Assert
        Assert.Single(updatedSession.ConversationHistory);
        Assert.Equal("Bash", updatedSession.ConversationHistory[0].ToolName);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task BuildContext_ShouldReturnRecentHistory()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"sessions-{Guid.NewGuid()}");
        var manager = new SessionManager(tempDir, maxHistorySize: 5);

        for (int i = 0; i < 10; i++)
        {
            await manager.RecordEventAsync("test-123", new SessionEvent
            {
                Type = "test",
                Content = $"Event {i}"
            });
        }

        // Act
        var context = await manager.BuildContextAsync("test-123", maxEvents: 3);

        // Assert
        Assert.Contains("Event 9", context);
        Assert.Contains("Event 8", context);
        Assert.Contains("Event 7", context);
        Assert.DoesNotContain("Event 0", context);

        // Cleanup
        Directory.Delete(tempDir, true);
    }
}
