using ClaudePermissionAnalyzer.Api.Models;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Models;

public class SessionDataTests
{
    [Fact]
    public void SessionData_ShouldInitializeWithDefaults()
    {
        // Act
        var session = new SessionData("test-session-123");

        // Assert
        Assert.Equal("test-session-123", session.SessionId);
        Assert.NotNull(session.ConversationHistory);
        Assert.Empty(session.ConversationHistory);
        Assert.True(session.StartTime <= DateTime.UtcNow);
    }

    [Fact]
    public void SessionEvent_ShouldStorePermissionRequest()
    {
        // Arrange
        var evt = new SessionEvent
        {
            Type = "permission-request",
            ToolName = "Bash",
            Decision = "auto-approved",
            SafetyScore = 96
        };

        // Assert
        Assert.Equal("permission-request", evt.Type);
        Assert.Equal("Bash", evt.ToolName);
        Assert.Equal(96, evt.SafetyScore);
    }
}
