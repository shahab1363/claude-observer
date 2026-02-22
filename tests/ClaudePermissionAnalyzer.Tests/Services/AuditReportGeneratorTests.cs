using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class AuditReportGeneratorTests
{
    private (AuditReportGenerator generator, SessionManager session, string tempDir) CreateServices()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"audit-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var session = new SessionManager(tempDir, maxHistorySize: 50);
        var adaptive = new AdaptiveThresholdService(tempDir);
        var config = new Configuration();
        var configManager = new ConfigurationManager(config);
        var profileService = new ProfileService(configManager);
        var generator = new AuditReportGenerator(session, adaptive, profileService);
        return (generator, session, tempDir);
    }

    [Fact]
    public async Task GenerateReport_ShouldReturnEmptyReportForNewSession()
    {
        // Arrange
        var (generator, session, tempDir) = CreateServices();

        // Act
        var report = await generator.GenerateReportAsync("new-session");

        // Assert
        Assert.Equal("new-session", report.SessionId);
        Assert.Equal(0, report.TotalDecisions);
        Assert.Equal(0, report.Approved);
        Assert.Equal(0, report.Denied);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task GenerateReport_ShouldCountDecisionsCorrectly()
    {
        // Arrange
        var (generator, session, tempDir) = CreateServices();

        await session.RecordEventAsync("test-session", new SessionEvent
        {
            Type = "permission-request",
            ToolName = "Bash",
            Decision = "auto-approved",
            SafetyScore = 95,
            Category = "safe"
        });
        await session.RecordEventAsync("test-session", new SessionEvent
        {
            Type = "permission-request",
            ToolName = "Write",
            Decision = "denied",
            SafetyScore = 60,
            Category = "risky"
        });
        await session.RecordEventAsync("test-session", new SessionEvent
        {
            Type = "permission-request",
            ToolName = "Read",
            Decision = "auto-approved",
            SafetyScore = 98,
            Category = "safe"
        });

        // Act
        var report = await generator.GenerateReportAsync("test-session");

        // Assert
        Assert.Equal(3, report.TotalDecisions);
        Assert.Equal(2, report.Approved);
        Assert.Equal(1, report.Denied);
        Assert.True(report.AverageSafetyScore > 0);
        Assert.Equal(2, report.RiskDistribution["safe"]);
        Assert.Equal(1, report.RiskDistribution["risky"]);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task GenerateReport_ShouldIdentifyFlaggedOperations()
    {
        // Arrange
        var (generator, session, tempDir) = CreateServices();

        await session.RecordEventAsync("test-session", new SessionEvent
        {
            Type = "permission-request",
            ToolName = "Bash",
            Decision = "denied",
            SafetyScore = 40,
            Category = "dangerous",
            Reasoning = "Potentially dangerous command"
        });

        // Act
        var report = await generator.GenerateReportAsync("test-session");

        // Assert
        Assert.Single(report.TopFlaggedOperations);
        Assert.Equal("Bash", report.TopFlaggedOperations[0].ToolName);
        Assert.Equal(40, report.TopFlaggedOperations[0].SafetyScore);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task RenderHtml_ShouldProduceValidHtml()
    {
        // Arrange
        var (generator, session, tempDir) = CreateServices();

        await session.RecordEventAsync("test-session", new SessionEvent
        {
            Type = "permission-request",
            ToolName = "Bash",
            Decision = "auto-approved",
            SafetyScore = 95,
            Category = "safe"
        });

        var report = await generator.GenerateReportAsync("test-session");

        // Act
        var html = generator.RenderHtml(report);

        // Assert
        Assert.Contains("<!DOCTYPE html>", html);
        Assert.Contains("Permission Audit Report", html);
        Assert.Contains("test-session", html);
        Assert.Contains("Bash", html);
        Assert.Contains("</html>", html);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task GenerateReport_ShouldProduceToolBreakdown()
    {
        // Arrange
        var (generator, session, tempDir) = CreateServices();

        for (int i = 0; i < 5; i++)
        {
            await session.RecordEventAsync("test-session", new SessionEvent
            {
                Type = "permission-request",
                ToolName = "Bash",
                Decision = "auto-approved",
                SafetyScore = 90 + i,
                Category = "safe"
            });
        }
        for (int i = 0; i < 3; i++)
        {
            await session.RecordEventAsync("test-session", new SessionEvent
            {
                Type = "permission-request",
                ToolName = "Write",
                Decision = "denied",
                SafetyScore = 60 + i,
                Category = "risky"
            });
        }

        // Act
        var report = await generator.GenerateReportAsync("test-session");

        // Assert
        Assert.Equal(2, report.ToolBreakdown.Count);
        var bashBreakdown = report.ToolBreakdown.First(t => t.ToolName == "Bash");
        Assert.Equal(5, bashBreakdown.TotalRequests);
        Assert.Equal(5, bashBreakdown.Approved);
        Assert.Equal(0, bashBreakdown.Denied);

        // Cleanup
        Directory.Delete(tempDir, true);
    }
}
