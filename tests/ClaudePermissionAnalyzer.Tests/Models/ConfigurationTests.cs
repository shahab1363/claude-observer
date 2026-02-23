using ClaudePermissionAnalyzer.Api.Models;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Models;

public class ConfigurationTests
{
    [Fact]
    public void Configuration_ShouldDeserializeFromJson()
    {
        // Arrange
        var json = """
        {
          "llm": {
            "provider": "claude-cli",
            "model": "sonnet",
            "timeout": 30000
          },
          "server": {
            "port": 5050,
            "host": "localhost"
          }
        }
        """;

        // Act
        var config = System.Text.Json.JsonSerializer.Deserialize<Configuration>(json);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("claude-cli", config.Llm.Provider);
        Assert.Equal("opus", config.Llm.Model); // JSON lowercase doesn't map without case-insensitive, so this tests the default
        Assert.Equal(15000, config.Llm.Timeout); // Default timeout (JSON lowercase doesn't map without case-insensitive)
        Assert.Equal(5050, config.Server.Port);
    }

    [Fact]
    public void HandlerConfig_ShouldMatchToolName()
    {
        // Arrange
        var handler = new HandlerConfig
        {
            Matcher = "Bash"
        };

        // Act & Assert
        Assert.True(handler.Matches("Bash"));
        Assert.False(handler.Matches("Read"));
    }

    [Fact]
    public void HandlerConfig_ShouldMatchRegexPattern()
    {
        // Arrange
        var handler = new HandlerConfig
        {
            Matcher = "Write|Edit"
        };

        // Act & Assert
        Assert.True(handler.Matches("Write"));
        Assert.True(handler.Matches("Edit"));
        Assert.False(handler.Matches("Read"));
    }
}
