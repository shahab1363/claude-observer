using ClaudePermissionAnalyzer.Api.Handlers;
using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using Moq;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Handlers;

public class LLMAnalysisHandlerTests
{
    [Fact]
    public async Task HandleAsync_ShouldAutoApprove_WhenScoreAboveThreshold()
    {
        // Arrange
        var mockLLM = new Mock<ILLMClient>();
        mockLLM.Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LLMResponse
            {
                Success = true,
                SafetyScore = 96,
                Reasoning = "Safe command",
                Category = "safe"
            });

        var handler = new LLMAnalysisHandler(mockLLM.Object, null);
        var input = new HookInput
        {
            HookEventName = "PermissionRequest",
            ToolName = "Bash",
            SessionId = "test-123"
        };
        var config = new HandlerConfig
        {
            Threshold = 95,
            AutoApprove = true
        };

        // Act
        var output = await handler.HandleAsync(input, config, "");

        // Assert
        Assert.True(output.AutoApprove);
        Assert.Equal(96, output.SafetyScore);
        Assert.Equal("Safe command", output.Reasoning);
    }

    [Fact]
    public async Task HandleAsync_ShouldDeny_WhenScoreBelowThreshold()
    {
        // Arrange
        var mockLLM = new Mock<ILLMClient>();
        mockLLM.Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LLMResponse
            {
                Success = true,
                SafetyScore = 85,
                Reasoning = "Risky operation",
                Category = "risky"
            });

        var handler = new LLMAnalysisHandler(mockLLM.Object, null);
        var input = new HookInput { ToolName = "Bash", SessionId = "test-123" };
        var config = new HandlerConfig { Threshold = 90, AutoApprove = true };

        // Act
        var output = await handler.HandleAsync(input, config, "");

        // Assert
        Assert.False(output.AutoApprove);
        Assert.Equal(85, output.SafetyScore);
    }
}
