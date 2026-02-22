using ClaudePermissionAnalyzer.Api.Handlers;
using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Handlers;

public class ContextInjectionHandlerTests
{
    [Fact]
    public async Task HandleAsync_ShouldReturnContextInjectionCategory()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextInjectionHandler>>();
        var handler = new ContextInjectionHandler(mockLogger.Object);
        var input = new HookInput
        {
            HookEventName = "UserPromptSubmit",
            SessionId = "test-123"
        };
        var config = new HandlerConfig
        {
            Name = "context-injector",
            Mode = "context-injection"
        };

        // Act
        var output = await handler.HandleAsync(input, config, "");

        // Assert
        Assert.Equal("context-injection", output.Category);
    }

    [Fact]
    public async Task HandleAsync_ShouldExtractRecentErrors_WhenConfigured()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextInjectionHandler>>();
        var handler = new ContextInjectionHandler(mockLogger.Object);
        var input = new HookInput
        {
            HookEventName = "UserPromptSubmit",
            SessionId = "test-123"
        };
        var config = new HandlerConfig
        {
            Name = "context-injector",
            Mode = "context-injection",
            Config = new Dictionary<string, object>
            {
                { "injectRecentErrors", true }
            }
        };
        var sessionContext = "Line 1\nSome error occurred here\nAnother line\nBuild failed on step 3";

        // Act
        var output = await handler.HandleAsync(input, config, sessionContext);

        // Assert
        Assert.NotNull(output.SystemMessage);
        Assert.Contains("Recent Errors", output.SystemMessage);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnNullSystemMessage_WhenNoContextToInject()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<ContextInjectionHandler>>();
        var handler = new ContextInjectionHandler(mockLogger.Object);
        var input = new HookInput
        {
            HookEventName = "UserPromptSubmit",
            SessionId = "test-123"
        };
        var config = new HandlerConfig
        {
            Name = "context-injector",
            Mode = "context-injection"
        };

        // Act
        var output = await handler.HandleAsync(input, config, "All good, no issues");

        // Assert
        Assert.Null(output.SystemMessage);
    }
}
