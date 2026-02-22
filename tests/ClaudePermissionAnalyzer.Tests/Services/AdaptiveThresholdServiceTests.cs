using ClaudePermissionAnalyzer.Api.Services;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class AdaptiveThresholdServiceTests
{
    [Fact]
    public async Task RecordDecision_ShouldTrackToolStats()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"adaptive-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var service = new AdaptiveThresholdService(tempDir);

        // Act
        await service.RecordDecisionAsync("Bash", 90, "auto-approved");
        await service.RecordDecisionAsync("Bash", 85, "auto-approved");
        await service.RecordDecisionAsync("Bash", 70, "denied");

        // Assert
        var stats = service.GetToolStats();
        Assert.True(stats.ContainsKey("Bash"));
        Assert.Equal(3, stats["Bash"].TotalDecisions);
        Assert.True(stats["Bash"].AverageSafetyScore > 0);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task RecordOverride_ShouldTrackFalsePositives()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"adaptive-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var service = new AdaptiveThresholdService(tempDir);

        // Act - system denied but user approved (false positive)
        await service.RecordOverrideAsync("Bash", "denied", "approved", 80, 85, "session-1");

        // Assert
        var stats = service.GetToolStats();
        Assert.Equal(1, stats["Bash"].FalsePositives);
        Assert.Equal(0, stats["Bash"].FalseNegatives);
        Assert.Equal(1, stats["Bash"].OverrideCount);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task RecordOverride_ShouldTrackFalseNegatives()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"adaptive-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var service = new AdaptiveThresholdService(tempDir);

        // Act - system approved but user denied (false negative)
        await service.RecordOverrideAsync("Write", "auto-approved", "denied", 92, 85, "session-1");

        // Assert
        var stats = service.GetToolStats();
        Assert.Equal(0, stats["Write"].FalsePositives);
        Assert.Equal(1, stats["Write"].FalseNegatives);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task GetRecentOverrides_ShouldReturnInReverseOrder()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"adaptive-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var service = new AdaptiveThresholdService(tempDir);

        await service.RecordOverrideAsync("Bash", "denied", "approved", 80, 85, "session-1");
        await service.RecordOverrideAsync("Write", "denied", "approved", 75, 85, "session-2");

        // Act
        var overrides = service.GetRecentOverrides();

        // Assert
        Assert.Equal(2, overrides.Count);
        Assert.Equal("Write", overrides[0].ToolName);
        Assert.Equal("Bash", overrides[1].ToolName);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task LoadAsync_ShouldPersistData()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"adaptive-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        // Write some data
        var service1 = new AdaptiveThresholdService(tempDir);
        await service1.RecordOverrideAsync("Bash", "denied", "approved", 80, 85, "session-1");

        // Load in new instance
        var service2 = new AdaptiveThresholdService(tempDir);
        await service2.LoadAsync();

        // Assert
        var overrides = service2.GetRecentOverrides();
        Assert.Single(overrides);
        Assert.Equal("Bash", overrides[0].ToolName);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task GetSuggestedThreshold_ShouldReturnNullWithoutData()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"adaptive-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var service = new AdaptiveThresholdService(tempDir);

        // Act
        var suggestion = service.GetSuggestedThreshold("Bash");

        // Assert
        Assert.Null(suggestion);

        // Cleanup
        Directory.Delete(tempDir, true);
    }
}
