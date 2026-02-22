using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class EnforcementServiceTests : IDisposable
{
    private readonly Mock<ILogger<EnforcementService>> _mockLogger;
    private readonly List<string> _tempFiles = new();

    public EnforcementServiceTests()
    {
        _mockLogger = new Mock<ILogger<EnforcementService>>();
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }

    private (EnforcementService service, ConfigurationManager configManager) CreateService(
        bool enforcementEnabled = false)
    {
        // Use a temp file path so that UpdateAsync/SaveAsync can write to disk
        var tempPath = Path.Combine(Path.GetTempPath(), $"enforcement-test-{Guid.NewGuid()}.json");
        _tempFiles.Add(tempPath);

        var configManager = new ConfigurationManager(tempPath);
        // Modify the default configuration's EnforcementEnabled before creating the service
        var config = configManager.GetConfiguration();
        config.EnforcementEnabled = enforcementEnabled;

        var service = new EnforcementService(configManager, _mockLogger.Object);
        return (service, configManager);
    }

    [Fact]
    public void DefaultState_MatchesConfigEnforcementEnabled_WhenFalse()
    {
        // Arrange & Act
        var (service, _) = CreateService(enforcementEnabled: false);

        // Assert
        Assert.False(service.IsEnforced);
    }

    [Fact]
    public void DefaultState_MatchesConfigEnforcementEnabled_WhenTrue()
    {
        // Arrange & Act
        var (service, _) = CreateService(enforcementEnabled: true);

        // Assert
        Assert.True(service.IsEnforced);
    }

    [Fact]
    public async Task SetEnforcedAsync_True_SetsIsEnforcedToTrue()
    {
        // Arrange
        var (service, _) = CreateService(enforcementEnabled: false);
        Assert.False(service.IsEnforced);

        // Act
        await service.SetEnforcedAsync(true);

        // Assert
        Assert.True(service.IsEnforced);
    }

    [Fact]
    public async Task SetEnforcedAsync_False_SetsIsEnforcedToFalse()
    {
        // Arrange
        var (service, _) = CreateService(enforcementEnabled: true);
        Assert.True(service.IsEnforced);

        // Act
        await service.SetEnforcedAsync(false);

        // Assert
        Assert.False(service.IsEnforced);
    }

    [Fact]
    public async Task ToggleAsync_FlipsStateFromFalseToTrue()
    {
        // Arrange
        var (service, _) = CreateService(enforcementEnabled: false);
        Assert.False(service.IsEnforced);

        // Act
        await service.ToggleAsync();

        // Assert
        Assert.True(service.IsEnforced);
    }

    [Fact]
    public async Task ToggleAsync_FlipsStateFromTrueToFalse()
    {
        // Arrange
        var (service, _) = CreateService(enforcementEnabled: true);
        Assert.True(service.IsEnforced);

        // Act
        await service.ToggleAsync();

        // Assert
        Assert.False(service.IsEnforced);
    }

    [Fact]
    public async Task ToggleAsync_Twice_ReturnsToOriginalState()
    {
        // Arrange
        var (service, _) = CreateService(enforcementEnabled: false);
        Assert.False(service.IsEnforced);

        // Act
        await service.ToggleAsync();
        Assert.True(service.IsEnforced);

        await service.ToggleAsync();

        // Assert
        Assert.False(service.IsEnforced);
    }

    [Fact]
    public async Task SetEnforcedAsync_PersistsToConfig_ViaConfigurationManager()
    {
        // Arrange
        var (service, configManager) = CreateService(enforcementEnabled: false);

        // Act
        await service.SetEnforcedAsync(true);

        // Assert - verify the underlying configuration was updated
        var config = configManager.GetConfiguration();
        Assert.True(config.EnforcementEnabled);
    }

    [Fact]
    public async Task SetEnforcedAsync_PersistsToConfig_WhenSetToFalse()
    {
        // Arrange
        var (service, configManager) = CreateService(enforcementEnabled: true);

        // Act
        await service.SetEnforcedAsync(false);

        // Assert - verify the underlying configuration was updated
        var config = configManager.GetConfiguration();
        Assert.False(config.EnforcementEnabled);
    }

    [Fact]
    public async Task ToggleAsync_PersistsToConfig()
    {
        // Arrange
        var (service, configManager) = CreateService(enforcementEnabled: false);

        // Act
        await service.ToggleAsync();

        // Assert
        var config = configManager.GetConfiguration();
        Assert.True(config.EnforcementEnabled);
    }

    [Fact]
    public async Task SetEnforcedAsync_SameValue_DoesNotThrow()
    {
        // Arrange
        var (service, _) = CreateService(enforcementEnabled: true);

        // Act - setting to the same value should be idempotent
        await service.SetEnforcedAsync(true);

        // Assert
        Assert.True(service.IsEnforced);
    }

    [Fact]
    public async Task MultipleSetEnforcedAsync_Calls_FinalStateIsCorrect()
    {
        // Arrange
        var (service, configManager) = CreateService(enforcementEnabled: false);

        // Act
        await service.SetEnforcedAsync(true);
        await service.SetEnforcedAsync(false);
        await service.SetEnforcedAsync(true);
        await service.SetEnforcedAsync(false);

        // Assert
        Assert.False(service.IsEnforced);
        Assert.False(configManager.GetConfiguration().EnforcementEnabled);
    }
}
