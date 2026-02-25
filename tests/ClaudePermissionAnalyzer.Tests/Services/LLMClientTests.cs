using ClaudePermissionAnalyzer.Api.Services;
using ClaudePermissionAnalyzer.Api.Models;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class LLMClientTests
{
    [Fact]
    public async Task ParseLLMResponse_ShouldExtractSafetyScore()
    {
        // Arrange
        var response = """
        Here's my analysis:

        {
          "safetyScore": 95,
          "reasoning": "Safe command",
          "category": "safe"
        }
        """;

        // Act
        var result = ClaudeCliClient.ParseResponse(response);

        // Assert
        Assert.Equal(95, result.SafetyScore);
        Assert.Equal("Safe command", result.Reasoning);
        Assert.Equal("safe", result.Category);
    }

    [Fact]
    public void ParseLLMResponse_ShouldHandleInvalidJson()
    {
        // Arrange
        var response = "This is not valid JSON";

        // Act
        var result = ClaudeCliClient.ParseResponse(response);

        // Assert
        Assert.Equal(0, result.SafetyScore);
        Assert.Contains("No JSON object found", result.Reasoning);
    }
}
