using ClaudePermissionAnalyzer.Api.Services;
using ClaudePermissionAnalyzer.Api.Models;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class ConfigurationManagerTests
{
    [Fact]
    public async Task LoadConfiguration_ShouldCreateDefaultConfig_WhenFileNotExists()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"test-config-{Guid.NewGuid()}.json");
        var manager = new ConfigurationManager(tempPath);

        // Act
        var config = await manager.LoadAsync();

        // Assert
        Assert.NotNull(config);
        Assert.Equal("claude-cli", config.Llm.Provider);
        Assert.Equal(5050, config.Server.Port);

        // Cleanup
        File.Delete(tempPath);
    }

    [Fact]
    public void GetHandlersForHook_ShouldReturnMatchingHandlers()
    {
        // Arrange
        var config = new Configuration
        {
            HookHandlers = new Dictionary<string, HookEventConfig>
            {
                ["PermissionRequest"] = new HookEventConfig
                {
                    Enabled = true,
                    Handlers = new List<HandlerConfig>
                    {
                        new HandlerConfig { Name = "bash-analyzer", Matcher = "Bash" },
                        new HandlerConfig { Name = "file-read", Matcher = "Read" }
                    }
                }
            }
        };
        var manager = new ConfigurationManager(config);

        // Act
        var handlers = manager.GetHandlersForHook("PermissionRequest");

        // Assert
        Assert.Equal(2, handlers.Count);
        Assert.Contains(handlers, h => h.Name == "bash-analyzer");
    }

    [Fact]
    public void FindMatchingHandler_ShouldReturnCorrectHandler()
    {
        // Arrange
        var config = new Configuration
        {
            HookHandlers = new Dictionary<string, HookEventConfig>
            {
                ["PermissionRequest"] = new HookEventConfig
                {
                    Handlers = new List<HandlerConfig>
                    {
                        new HandlerConfig { Name = "bash", Matcher = "Bash" },
                        new HandlerConfig { Name = "write", Matcher = "Write|Edit" }
                    }
                }
            }
        };
        var manager = new ConfigurationManager(config);

        // Act
        var handler = manager.FindMatchingHandler("PermissionRequest", "Edit");

        // Assert
        Assert.NotNull(handler);
        Assert.Equal("write", handler.Name);
    }
}
