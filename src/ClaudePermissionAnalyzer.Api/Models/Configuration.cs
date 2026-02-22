using System.Text.RegularExpressions;

namespace ClaudePermissionAnalyzer.Api.Models;

public class Configuration
{
    public LlmConfig Llm { get; set; } = new();
    public ServerConfig Server { get; set; } = new();
    public Dictionary<string, HookEventConfig> HookHandlers { get; set; } = new();
    public SessionConfig Session { get; set; } = new();
    public SecurityConfig Security { get; set; } = new();
    public ProfileConfig Profiles { get; set; } = new();
    public bool EnforcementEnabled { get; set; } = false;

    /// <summary>
    /// When true, LLM analysis runs even in observe mode (so you see scores in logs).
    /// When false, observe mode skips LLM calls entirely (just logs events).
    /// </summary>
    public bool AnalyzeInObserveMode { get; set; } = true;
}

public class LlmConfig
{
    public string Provider { get; set; } = "claude-cli";
    public string Model { get; set; } = "sonnet";
    public int Timeout { get; set; } = 15000;

    /// <summary>
    /// Use a persistent claude process instead of spawning one per request.
    /// Requires claude CLI with --print and stream-json support.
    /// </summary>
    public bool PersistentProcess { get; set; } = true;

    /// <summary>
    /// Text prepended before each hook analysis prompt.
    /// Use this to set context or instructions for the LLM.
    /// </summary>
    public string? PromptPrefix { get; set; }

    /// <summary>
    /// Text appended after each hook analysis prompt.
    /// Use this to add reminders or output format instructions.
    /// </summary>
    public string? PromptSuffix { get; set; }

    /// <summary>
    /// System prompt sent to the claude CLI via --system-prompt.
    /// If null, the default system prompt is used.
    /// </summary>
    public string? SystemPrompt { get; set; } = "You are a security analyzer that evaluates the safety of operations. Always respond ONLY with valid JSON containing safetyScore (0-100), reasoning (string), and category (safe|cautious|risky|dangerous). Never include any text outside the JSON object.";
}

public class ServerConfig
{
    public int Port { get; set; } = 5050;
    public string Host { get; set; } = "localhost";
}

public class HookEventConfig
{
    public bool Enabled { get; set; } = true;
    public List<HandlerConfig> Handlers { get; set; } = new();
}

public class SessionConfig
{
    public int MaxHistoryPerSession { get; set; } = 50;
    public string StorageDir { get; set; } = "~/.claude-permission-analyzer/sessions";
}

public class SecurityConfig
{
    public string? ApiKey { get; set; }
    public int RateLimitPerMinute { get; set; } = 600;
}
