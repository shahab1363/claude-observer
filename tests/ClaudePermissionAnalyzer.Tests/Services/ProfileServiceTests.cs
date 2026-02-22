using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class ProfileServiceTests
{
    [Fact]
    public void GetActiveProfile_ShouldReturnModerateByDefault()
    {
        // Arrange
        var config = new Configuration();
        var configManager = new ConfigurationManager(config);
        var service = new ProfileService(configManager);

        // Act
        var profile = service.GetActiveProfile();

        // Assert
        Assert.Equal("Moderate", profile.Name);
        Assert.Equal(85, profile.DefaultThreshold);
    }

    [Fact]
    public void GetThresholdForTool_ShouldReturnOverrideWhenAvailable()
    {
        // Arrange
        var config = new Configuration();
        var configManager = new ConfigurationManager(config);
        var service = new ProfileService(configManager);

        // Act - Moderate profile has Bash=90
        var bashThreshold = service.GetThresholdForTool("Bash");
        var unknownThreshold = service.GetThresholdForTool("UnknownTool");

        // Assert
        Assert.Equal(90, bashThreshold);
        Assert.Equal(85, unknownThreshold);
    }

    [Fact]
    public async Task SwitchProfile_ShouldChangeActiveProfile()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"profile-test-{Guid.NewGuid()}.json");
        var configManager = new ConfigurationManager(tempPath);
        await configManager.LoadAsync();
        var service = new ProfileService(configManager);

        // Act
        var success = await service.SwitchProfileAsync("strict");

        // Assert
        Assert.True(success);
        Assert.Equal("strict", service.GetActiveProfileKey());
        Assert.Equal("Strict", service.GetActiveProfile().Name);
        Assert.Equal(95, service.GetActiveProfile().DefaultThreshold);

        // Cleanup
        File.Delete(tempPath);
    }

    [Fact]
    public async Task SwitchProfile_ShouldReturnFalseForInvalidProfile()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), $"profile-test-{Guid.NewGuid()}.json");
        var configManager = new ConfigurationManager(tempPath);
        await configManager.LoadAsync();
        var service = new ProfileService(configManager);

        // Act
        var success = await service.SwitchProfileAsync("nonexistent");

        // Assert
        Assert.False(success);
        Assert.Equal("moderate", service.GetActiveProfileKey());

        // Cleanup
        File.Delete(tempPath);
    }

    [Fact]
    public void GetAllProfiles_ShouldReturnBuiltInProfiles()
    {
        // Arrange
        var config = new Configuration();
        var configManager = new ConfigurationManager(config);
        var service = new ProfileService(configManager);

        // Act
        var profiles = service.GetAllProfiles();

        // Assert
        Assert.True(profiles.ContainsKey("strict"));
        Assert.True(profiles.ContainsKey("moderate"));
        Assert.True(profiles.ContainsKey("permissive"));
        Assert.True(profiles.ContainsKey("lockdown"));
    }

    [Fact]
    public void IsAutoApproveEnabled_ShouldReflectProfile()
    {
        // Arrange
        var config = new Configuration();
        var configManager = new ConfigurationManager(config);
        var service = new ProfileService(configManager);

        // Act - Moderate profile has AutoApproveEnabled=true
        Assert.True(service.IsAutoApproveEnabled());
    }
}
