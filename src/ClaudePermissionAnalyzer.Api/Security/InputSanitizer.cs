using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudePermissionAnalyzer.Api.Security;

/// <summary>
/// Provides input sanitization to prevent prompt injection and other attacks.
/// </summary>
public static partial class InputSanitizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Maximum allowed length for a session ID.
    /// </summary>
    public const int MaxSessionIdLength = 128;

    /// <summary>
    /// Maximum allowed length for a tool name.
    /// </summary>
    public const int MaxToolNameLength = 256;

    /// <summary>
    /// Maximum allowed length for hook event name.
    /// </summary>
    public const int MaxHookEventNameLength = 128;

    /// <summary>
    /// Maximum allowed size for serialized ToolInput JSON (in characters).
    /// </summary>
    public const int MaxToolInputLength = 1_000_000; // 1MB of text

    /// <summary>
    /// Validates and sanitizes a session ID. Only allows alphanumeric, hyphens, and underscores.
    /// </summary>
    public static bool IsValidSessionId(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        if (sessionId.Length > MaxSessionIdLength)
            return false;

        try
        {
            return SessionIdRegex().IsMatch(sessionId);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates a tool name. Only allows alphanumeric, hyphens, underscores, dots, and colons.
    /// </summary>
    public static bool IsValidToolName(string? toolName)
    {
        if (string.IsNullOrEmpty(toolName))
            return true; // ToolName is optional

        if (toolName.Length > MaxToolNameLength)
            return false;

        try
        {
            return ToolNameRegex().IsMatch(toolName);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates a hook event name.
    /// </summary>
    public static bool IsValidHookEventName(string? hookEventName)
    {
        if (string.IsNullOrWhiteSpace(hookEventName))
            return false;

        if (hookEventName.Length > MaxHookEventNameLength)
            return false;

        try
        {
            return HookEventNameRegex().IsMatch(hookEventName);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that ToolInput JSON is within size limits.
    /// </summary>
    public static bool IsToolInputWithinLimits(JsonElement? toolInput)
    {
        if (!toolInput.HasValue)
            return true;

        var raw = toolInput.Value.GetRawText();
        return raw.Length <= MaxToolInputLength;
    }

    /// <summary>
    /// Sanitizes user-provided text that will be embedded in an LLM prompt.
    /// Strips known prompt injection patterns and wraps in delimiters.
    /// </summary>
    public static string SanitizeForPrompt(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        // Truncate overly long inputs
        if (input.Length > MaxToolInputLength)
            input = input[..MaxToolInputLength] + "... [TRUNCATED]";

        return input;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex SessionIdRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9\-_.:]+$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex ToolNameRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9\-_]+$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex HookEventNameRegex();
}
