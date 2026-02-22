using ClaudePermissionAnalyzer.Api.Models;
using System.Text.Json;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Models;

public class HookModelsTests
{
    [Fact]
    public void HookInput_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = """
        {
          "hookEventName": "PermissionRequest",
          "sessionId": "abc123",
          "toolName": "Bash",
          "toolInput": {
            "command": "git status"
          },
          "cwd": "/home/user/project"
        }
        """;

        // Act
        var input = JsonSerializer.Deserialize<HookInput>(json);

        // Assert
        Assert.NotNull(input);
        Assert.Equal("PermissionRequest", input.HookEventName);
        Assert.Equal("abc123", input.SessionId);
        Assert.Equal("Bash", input.ToolName);
        Assert.Equal("/home/user/project", input.Cwd);
    }

    [Fact]
    public void HookOutput_ShouldSerializeToJson()
    {
        // Arrange
        var output = new HookOutput
        {
            AutoApprove = true,
            SafetyScore = 95,
            Reasoning = "Safe command",
            Category = "safe"
        };

        // Act
        var json = JsonSerializer.Serialize(output);

        // Assert
        Assert.Contains("\"autoApprove\":true", json);
        Assert.Contains("\"safetyScore\":95", json);
    }
}
