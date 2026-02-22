using ClaudePermissionAnalyzer.Api.Security;
using System.Text.Json;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Security;

public class InputSanitizerTests
{
    [Theory]
    [InlineData("abc123", true)]
    [InlineData("test-session-456", true)]
    [InlineData("session_with_underscores", true)]
    [InlineData("ABC-123-def", true)]
    public void IsValidSessionId_ShouldAcceptValidIds(string sessionId, bool expected)
    {
        Assert.Equal(expected, InputSanitizer.IsValidSessionId(sessionId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../../../etc/passwd")]
    [InlineData("session/with/slashes")]
    [InlineData("session\\with\\backslashes")]
    [InlineData("session with spaces")]
    [InlineData("session;drop table")]
    [InlineData("session<script>alert(1)</script>")]
    public void IsValidSessionId_ShouldRejectInvalidIds(string? sessionId)
    {
        Assert.False(InputSanitizer.IsValidSessionId(sessionId));
    }

    [Fact]
    public void IsValidSessionId_ShouldRejectOverlongIds()
    {
        var longId = new string('a', InputSanitizer.MaxSessionIdLength + 1);
        Assert.False(InputSanitizer.IsValidSessionId(longId));
    }

    [Fact]
    public void IsValidSessionId_ShouldAcceptMaxLengthId()
    {
        var maxId = new string('a', InputSanitizer.MaxSessionIdLength);
        Assert.True(InputSanitizer.IsValidSessionId(maxId));
    }

    [Theory]
    [InlineData(null, true)]      // Optional field
    [InlineData("", true)]        // Optional field
    [InlineData("Bash", true)]
    [InlineData("file.read", true)]
    [InlineData("mcp:tool-name", true)]
    [InlineData("Tool_Name-123", true)]
    public void IsValidToolName_ShouldAcceptValidNames(string? toolName, bool expected)
    {
        Assert.Equal(expected, InputSanitizer.IsValidToolName(toolName));
    }

    [Theory]
    [InlineData("tool with spaces")]
    [InlineData("tool;injection")]
    [InlineData("tool<script>")]
    [InlineData("tool/path/traversal")]
    public void IsValidToolName_ShouldRejectInvalidNames(string toolName)
    {
        Assert.False(InputSanitizer.IsValidToolName(toolName));
    }

    [Theory]
    [InlineData("PermissionRequest", true)]
    [InlineData("PreToolUse", true)]
    [InlineData("hook-event-123", true)]
    public void IsValidHookEventName_ShouldAcceptValidNames(string hookEventName, bool expected)
    {
        Assert.Equal(expected, InputSanitizer.IsValidHookEventName(hookEventName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("event with spaces")]
    [InlineData("event;injection")]
    public void IsValidHookEventName_ShouldRejectInvalidNames(string? hookEventName)
    {
        Assert.False(InputSanitizer.IsValidHookEventName(hookEventName));
    }

    [Fact]
    public void IsToolInputWithinLimits_ShouldAcceptNull()
    {
        Assert.True(InputSanitizer.IsToolInputWithinLimits(null));
    }

    [Fact]
    public void IsToolInputWithinLimits_ShouldAcceptSmallInput()
    {
        var json = JsonDocument.Parse("{\"command\": \"git status\"}");
        Assert.True(InputSanitizer.IsToolInputWithinLimits(json.RootElement));
    }

    [Fact]
    public void SanitizeForPrompt_ShouldHandleNull()
    {
        Assert.Equal(string.Empty, InputSanitizer.SanitizeForPrompt(null));
    }

    [Fact]
    public void SanitizeForPrompt_ShouldTruncateLongInput()
    {
        var longInput = new string('x', InputSanitizer.MaxToolInputLength + 100);
        var result = InputSanitizer.SanitizeForPrompt(longInput);
        Assert.Contains("TRUNCATED", result);
    }

    [Fact]
    public void SanitizeForPrompt_ShouldPreserveNormalInput()
    {
        var input = "git status";
        Assert.Equal(input, InputSanitizer.SanitizeForPrompt(input));
    }
}
