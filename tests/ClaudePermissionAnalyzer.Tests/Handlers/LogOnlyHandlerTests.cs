using ClaudePermissionAnalyzer.Api.Handlers;
using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Handlers;

public class LogOnlyHandlerTests
{
    [Fact]
    public async Task HandleAsync_ShouldReturnLoggedCategory()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<LogOnlyHandler>>();
        var handler = new LogOnlyHandler(mockLogger.Object);
        var input = new HookInput
        {
            HookEventName = "PreToolUse",
            ToolName = "Bash",
            SessionId = "test-123"
        };
        var config = new HandlerConfig
        {
            Name = "pre-tool-logger",
            Mode = "log-only"
        };

        // Act
        var output = await handler.HandleAsync(input, config, "");

        // Assert
        Assert.False(output.AutoApprove);
        Assert.Equal(0, output.SafetyScore);
        Assert.Equal("logged", output.Category);
        Assert.Equal("Log-only handler - no decision made", output.Reasoning);
    }

    [Fact]
    public async Task HandleAsync_ShouldUseConfiguredLogLevel()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<LogOnlyHandler>>();
        var handler = new LogOnlyHandler(mockLogger.Object);
        var input = new HookInput
        {
            HookEventName = "Stop",
            SessionId = "test-456"
        };
        var config = new HandlerConfig
        {
            Name = "stop-logger",
            Mode = "log-only",
            Config = new Dictionary<string, object> { { "logLevel", "debug" } }
        };

        // Act
        var output = await handler.HandleAsync(input, config, "");

        // Assert
        Assert.Equal("logged", output.Category);
    }

    [Fact]
    public async Task HandleAsync_ShouldHandleNullToolName()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<LogOnlyHandler>>();
        var handler = new LogOnlyHandler(mockLogger.Object);
        var input = new HookInput
        {
            HookEventName = "UserPromptSubmit",
            SessionId = "test-789"
        };
        var config = new HandlerConfig { Mode = "log-only" };

        // Act
        var output = await handler.HandleAsync(input, config, "");

        // Assert
        Assert.Equal("logged", output.Category);
    }
}
