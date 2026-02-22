# Claude Permission Analyzer Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build an intelligent permission automation system for Claude Code that uses LLM-based safety analysis to auto-approve safe operations while providing comprehensive logging and session tracking.

**Architecture:** Multi-tier system with Python hook scripts forwarding to a C# ASP.NET Core background service that performs LLM-based safety analysis, stores session history, and serves a web UI for monitoring. System tray integration for easy access.

**Tech Stack:** C# (.NET 8+), ASP.NET Core Minimal APIs, Python 3.8+, Vanilla JavaScript, SignalR, JSON file storage

---

## Phase 1: Core Models and Configuration

### Task 1: Configuration Models

**Files:**
- Create: `src/ClaudePermissionAnalyzer.Api/Models/Configuration.cs`
- Create: `src/ClaudePermissionAnalyzer.Api/Models/HandlerConfig.cs`
- Test: `tests/ClaudePermissionAnalyzer.Tests/Models/ConfigurationTests.cs`

**Step 1: Write the failing test**

```csharp
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
        Assert.Equal("sonnet", config.Llm.Model);
        Assert.Equal(30000, config.Llm.Timeout);
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
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ConfigurationTests"`
Expected: FAIL with "type Configuration not found"

**Step 3: Write minimal implementation**

Create `src/ClaudePermissionAnalyzer.Api/Models/Configuration.cs`:

```csharp
using System.Text.RegularExpressions;

namespace ClaudePermissionAnalyzer.Api.Models;

public class Configuration
{
    public LlmConfig Llm { get; set; } = new();
    public ServerConfig Server { get; set; } = new();
    public Dictionary<string, HookEventConfig> HookHandlers { get; set; } = new();
    public SessionConfig Session { get; set; } = new();
}

public class LlmConfig
{
    public string Provider { get; set; } = "claude-cli";
    public string Model { get; set; } = "sonnet";
    public int Timeout { get; set; } = 30000;
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
```

Create `src/ClaudePermissionAnalyzer.Api/Models/HandlerConfig.cs`:

```csharp
using System.Text.RegularExpressions;

namespace ClaudePermissionAnalyzer.Api.Models;

public class HandlerConfig
{
    public string Name { get; set; } = string.Empty;
    public string? Matcher { get; set; }
    public string Mode { get; set; } = "log-only";
    public string? PromptTemplate { get; set; }
    public int Threshold { get; set; } = 85;
    public bool AutoApprove { get; set; } = false;
    public Dictionary<string, object> Config { get; set; } = new();

    public bool Matches(string toolName)
    {
        if (string.IsNullOrEmpty(Matcher) || Matcher == "*")
            return true;

        try
        {
            return Regex.IsMatch(toolName, Matcher, RegexOptions.IgnoreCase);
        }
        catch
        {
            return Matcher.Equals(toolName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ConfigurationTests"`
Expected: PASS (3 tests)

**Step 5: Commit**

```bash
git add src/ClaudePermissionAnalyzer.Api/Models/*.cs tests/ClaudePermissionAnalyzer.Tests/Models/ConfigurationTests.cs
git commit -m "feat: add configuration models with JSON deserialization

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 2: Hook Input/Output Models

**Files:**
- Create: `src/ClaudePermissionAnalyzer.Api/Models/HookInput.cs`
- Create: `src/ClaudePermissionAnalyzer.Api/Models/HookOutput.cs`
- Test: `tests/ClaudePermissionAnalyzer.Tests/Models/HookModelsTests.cs`

**Step 1: Write the failing test**

```csharp
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
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~HookModelsTests"`
Expected: FAIL with "type HookInput not found"

**Step 3: Write minimal implementation**

Create `src/ClaudePermissionAnalyzer.Api/Models/HookInput.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudePermissionAnalyzer.Api.Models;

public class HookInput
{
    [JsonPropertyName("hookEventName")]
    public string HookEventName { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    [JsonPropertyName("toolInput")]
    public JsonElement? ToolInput { get; set; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
```

Create `src/ClaudePermissionAnalyzer.Api/Models/HookOutput.cs`:

```csharp
using System.Text.Json.Serialization;

namespace ClaudePermissionAnalyzer.Api.Models;

public class HookOutput
{
    [JsonPropertyName("autoApprove")]
    public bool AutoApprove { get; set; }

    [JsonPropertyName("safetyScore")]
    public int SafetyScore { get; set; }

    [JsonPropertyName("reasoning")]
    public string Reasoning { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = "unknown";

    [JsonPropertyName("threshold")]
    public int Threshold { get; set; }

    [JsonPropertyName("systemMessage")]
    public string? SystemMessage { get; set; }

    [JsonPropertyName("interrupt")]
    public bool Interrupt { get; set; }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~HookModelsTests"`
Expected: PASS (2 tests)

**Step 5: Commit**

```bash
git add src/ClaudePermissionAnalyzer.Api/Models/Hook*.cs tests/ClaudePermissionAnalyzer.Tests/Models/HookModelsTests.cs
git commit -m "feat: add hook input/output models with JSON serialization

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

### Task 3: Session Data Models

**Files:**
- Create: `src/ClaudePermissionAnalyzer.Api/Models/SessionData.cs`
- Test: `tests/ClaudePermissionAnalyzer.Tests/Models/SessionDataTests.cs`

**Step 1: Write the failing test**

```csharp
using ClaudePermissionAnalyzer.Api.Models;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Models;

public class SessionDataTests
{
    [Fact]
    public void SessionData_ShouldInitializeWithDefaults()
    {
        // Act
        var session = new SessionData("test-session-123");

        // Assert
        Assert.Equal("test-session-123", session.SessionId);
        Assert.NotNull(session.ConversationHistory);
        Assert.Empty(session.ConversationHistory);
        Assert.True(session.StartTime <= DateTime.UtcNow);
    }

    [Fact]
    public void SessionEvent_ShouldStorePermissionRequest()
    {
        // Arrange
        var evt = new SessionEvent
        {
            Type = "permission-request",
            ToolName = "Bash",
            Decision = "auto-approved",
            SafetyScore = 96
        };

        // Assert
        Assert.Equal("permission-request", evt.Type);
        Assert.Equal("Bash", evt.ToolName);
        Assert.Equal(96, evt.SafetyScore);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SessionDataTests"`
Expected: FAIL with "type SessionData not found"

**Step 3: Write minimal implementation**

Create `src/ClaudePermissionAnalyzer.Api/Models/SessionData.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudePermissionAnalyzer.Api.Models;

public class SessionData
{
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; }

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("lastActivity")]
    public DateTime LastActivity { get; set; }

    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    [JsonPropertyName("conversationHistory")]
    public List<SessionEvent> ConversationHistory { get; set; }

    public SessionData(string sessionId)
    {
        SessionId = sessionId;
        StartTime = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
        ConversationHistory = new List<SessionEvent>();
    }
}

public class SessionEvent
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string? ToolName { get; set; }

    [JsonPropertyName("toolInput")]
    public JsonElement? ToolInput { get; set; }

    [JsonPropertyName("decision")]
    public string? Decision { get; set; }

    [JsonPropertyName("safetyScore")]
    public int? SafetyScore { get; set; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~SessionDataTests"`
Expected: PASS (2 tests)

**Step 5: Commit**

```bash
git add src/ClaudePermissionAnalyzer.Api/Models/SessionData.cs tests/ClaudePermissionAnalyzer.Tests/Models/SessionDataTests.cs
git commit -m "feat: add session data models for conversation tracking

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Phase 2: Configuration Management

### Task 4: Configuration Manager Service

**Files:**
- Create: `src/ClaudePermissionAnalyzer.Api/Services/ConfigurationManager.cs`
- Test: `tests/ClaudePermissionAnalyzer.Tests/Services/ConfigurationManagerTests.cs`

**Step 1: Write the failing test**

```csharp
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
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~ConfigurationManagerTests"`
Expected: FAIL with "type ConfigurationManager not found"

**Step 3: Write minimal implementation**

Create `src/ClaudePermissionAnalyzer.Api/Services/ConfigurationManager.cs`:

```csharp
using ClaudePermissionAnalyzer.Api.Models;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Services;

public class ConfigurationManager
{
    private readonly string _configPath;
    private Configuration _configuration;

    public ConfigurationManager(string? configPath = null)
    {
        _configPath = configPath ?? GetDefaultConfigPath();
        _configuration = CreateDefaultConfiguration();
    }

    public ConfigurationManager(Configuration configuration)
    {
        _configPath = string.Empty;
        _configuration = configuration;
    }

    public async Task<Configuration> LoadAsync()
    {
        if (File.Exists(_configPath))
        {
            var json = await File.ReadAllTextAsync(_configPath);
            _configuration = JsonSerializer.Deserialize<Configuration>(json)
                ?? CreateDefaultConfiguration();
        }
        else
        {
            _configuration = CreateDefaultConfiguration();
            await SaveAsync();
        }

        return _configuration;
    }

    public async Task SaveAsync()
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(_configuration, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(_configPath, json);
    }

    public List<HandlerConfig> GetHandlersForHook(string hookEventName)
    {
        if (_configuration.HookHandlers.TryGetValue(hookEventName, out var hookConfig))
        {
            return hookConfig.Enabled ? hookConfig.Handlers : new List<HandlerConfig>();
        }
        return new List<HandlerConfig>();
    }

    public HandlerConfig? FindMatchingHandler(string hookEventName, string? toolName)
    {
        var handlers = GetHandlersForHook(hookEventName);
        return handlers.FirstOrDefault(h => h.Matches(toolName ?? string.Empty));
    }

    private static string GetDefaultConfigPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude-permission-analyzer", "config.json");
    }

    private static Configuration CreateDefaultConfiguration()
    {
        return new Configuration
        {
            Llm = new LlmConfig
            {
                Provider = "claude-cli",
                Model = "sonnet",
                Timeout = 30000
            },
            Server = new ServerConfig
            {
                Port = 5050,
                Host = "localhost"
            },
            Session = new SessionConfig
            {
                MaxHistoryPerSession = 50,
                StorageDir = "~/.claude-permission-analyzer/sessions"
            },
            HookHandlers = new Dictionary<string, HookEventConfig>()
        };
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~ConfigurationManagerTests"`
Expected: PASS (3 tests)

**Step 5: Commit**

```bash
git add src/ClaudePermissionAnalyzer.Api/Services/ConfigurationManager.cs tests/ClaudePermissionAnalyzer.Tests/Services/ConfigurationManagerTests.cs
git commit -m "feat: add configuration manager with file persistence

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Phase 3: Session Management

### Task 5: Session Manager Service

**Files:**
- Create: `src/ClaudePermissionAnalyzer.Api/Services/SessionManager.cs`
- Test: `tests/ClaudePermissionAnalyzer.Tests/Services/SessionManagerTests.cs`

**Step 1: Write the failing test**

```csharp
using ClaudePermissionAnalyzer.Api.Services;
using ClaudePermissionAnalyzer.Api.Models;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class SessionManagerTests
{
    [Fact]
    public async Task GetOrCreateSession_ShouldCreateNewSession_WhenNotExists()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"sessions-{Guid.NewGuid()}");
        var manager = new SessionManager(tempDir, maxHistorySize: 50);

        // Act
        var session = await manager.GetOrCreateSessionAsync("test-123");

        // Assert
        Assert.NotNull(session);
        Assert.Equal("test-123", session.SessionId);
        Assert.Empty(session.ConversationHistory);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task RecordEvent_ShouldAddEventToHistory()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"sessions-{Guid.NewGuid()}");
        var manager = new SessionManager(tempDir, maxHistorySize: 50);
        var session = await manager.GetOrCreateSessionAsync("test-123");

        var evt = new SessionEvent
        {
            Type = "permission-request",
            ToolName = "Bash",
            Decision = "auto-approved"
        };

        // Act
        await manager.RecordEventAsync("test-123", evt);
        var updatedSession = await manager.GetOrCreateSessionAsync("test-123");

        // Assert
        Assert.Single(updatedSession.ConversationHistory);
        Assert.Equal("Bash", updatedSession.ConversationHistory[0].ToolName);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task BuildContext_ShouldReturnRecentHistory()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"sessions-{Guid.NewGuid()}");
        var manager = new SessionManager(tempDir, maxHistorySize: 5);

        for (int i = 0; i < 10; i++)
        {
            await manager.RecordEventAsync("test-123", new SessionEvent
            {
                Type = "test",
                Content = $"Event {i}"
            });
        }

        // Act
        var context = await manager.BuildContextAsync("test-123", maxEvents: 3);

        // Assert
        Assert.Contains("Event 9", context);
        Assert.Contains("Event 8", context);
        Assert.Contains("Event 7", context);
        Assert.DoesNotContain("Event 0", context);

        // Cleanup
        Directory.Delete(tempDir, true);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SessionManagerTests"`
Expected: FAIL with "type SessionManager not found"

**Step 3: Write minimal implementation**

Create `src/ClaudePermissionAnalyzer.Api/Services/SessionManager.cs`:

```csharp
using ClaudePermissionAnalyzer.Api.Models;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Services;

public class SessionManager
{
    private readonly string _storageDir;
    private readonly int _maxHistorySize;
    private readonly ConcurrentDictionary<string, SessionData> _sessionCache;

    public SessionManager(string storageDir, int maxHistorySize = 50)
    {
        _storageDir = ExpandPath(storageDir);
        _maxHistorySize = maxHistorySize;
        _sessionCache = new ConcurrentDictionary<string, SessionData>();

        if (!Directory.Exists(_storageDir))
        {
            Directory.CreateDirectory(_storageDir);
        }
    }

    public async Task<SessionData> GetOrCreateSessionAsync(string sessionId)
    {
        if (_sessionCache.TryGetValue(sessionId, out var cached))
        {
            return cached;
        }

        var filePath = GetSessionFilePath(sessionId);
        SessionData session;

        if (File.Exists(filePath))
        {
            var json = await File.ReadAllTextAsync(filePath);
            session = JsonSerializer.Deserialize<SessionData>(json)
                ?? new SessionData(sessionId);
        }
        else
        {
            session = new SessionData(sessionId);
            await SaveSessionAsync(session);
        }

        _sessionCache[sessionId] = session;
        return session;
    }

    public async Task RecordEventAsync(string sessionId, SessionEvent evt)
    {
        var session = await GetOrCreateSessionAsync(sessionId);

        session.ConversationHistory.Add(evt);
        session.LastActivity = DateTime.UtcNow;

        // Trim history if exceeds max size
        while (session.ConversationHistory.Count > _maxHistorySize)
        {
            session.ConversationHistory.RemoveAt(0);
        }

        await SaveSessionAsync(session);
    }

    public async Task<string> BuildContextAsync(string sessionId, int maxEvents = 10)
    {
        var session = await GetOrCreateSessionAsync(sessionId);
        var recentEvents = session.ConversationHistory
            .TakeLast(maxEvents)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("RECENT SESSION HISTORY:");

        foreach (var evt in recentEvents)
        {
            sb.AppendLine($"[{evt.Timestamp:HH:mm:ss}] {evt.Type}");

            if (!string.IsNullOrEmpty(evt.ToolName))
            {
                sb.AppendLine($"  Tool: {evt.ToolName}");
            }

            if (!string.IsNullOrEmpty(evt.Decision))
            {
                sb.AppendLine($"  Decision: {evt.Decision} (Score: {evt.SafetyScore})");
            }

            if (!string.IsNullOrEmpty(evt.Content))
            {
                sb.AppendLine($"  Content: {evt.Content}");
            }
        }

        return sb.ToString();
    }

    private async Task SaveSessionAsync(SessionData session)
    {
        var filePath = GetSessionFilePath(session.SessionId);
        var json = JsonSerializer.Serialize(session, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(filePath, json);
    }

    private string GetSessionFilePath(string sessionId)
    {
        return Path.Combine(_storageDir, $"{sessionId}.json");
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith("~/"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.Substring(2));
        }
        return path;
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~SessionManagerTests"`
Expected: PASS (3 tests)

**Step 5: Commit**

```bash
git add src/ClaudePermissionAnalyzer.Api/Services/SessionManager.cs tests/ClaudePermissionAnalyzer.Tests/Services/SessionManagerTests.cs
git commit -m "feat: add session manager with history tracking and persistence

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Phase 4: LLM Integration

### Task 6: LLM Client Interface and Implementation

**Files:**
- Create: `src/ClaudePermissionAnalyzer.Api/Services/ILLMClient.cs`
- Create: `src/ClaudePermissionAnalyzer.Api/Services/ClaudeCliClient.cs`
- Test: `tests/ClaudePermissionAnalyzer.Tests/Services/LLMClientTests.cs`

**Step 1: Write the failing test**

```csharp
using ClaudePermissionAnalyzer.Api.Services;
using ClaudePermissionAnalyzer.Api.Models;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class LLMClientTests
{
    [Fact]
    public async Task ParseLLMResponse_ShouldExtractSafetyScore()
    {
        // Arrange
        var client = new ClaudeCliClient(new LlmConfig());
        var response = """
        Here's my analysis:

        {
          "safetyScore": 95,
          "reasoning": "Safe command",
          "category": "safe"
        }
        """;

        // Act
        var result = client.ParseResponse(response);

        // Assert
        Assert.Equal(95, result.SafetyScore);
        Assert.Equal("Safe command", result.Reasoning);
        Assert.Equal("safe", result.Category);
    }

    [Fact]
    public void ParseLLMResponse_ShouldHandleInvalidJson()
    {
        // Arrange
        var client = new ClaudeCliClient(new LlmConfig());
        var response = "This is not valid JSON";

        // Act
        var result = client.ParseResponse(response);

        // Assert
        Assert.Equal(0, result.SafetyScore);
        Assert.Contains("Failed to parse", result.Reasoning);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LLMClientTests"`
Expected: FAIL with "type ILLMClient not found"

**Step 3: Write minimal implementation**

Create `src/ClaudePermissionAnalyzer.Api/Services/ILLMClient.cs`:

```csharp
using ClaudePermissionAnalyzer.Api.Models;

namespace ClaudePermissionAnalyzer.Api.Services;

public interface ILLMClient
{
    Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default);
}

public class LLMResponse
{
    public int SafetyScore { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public string Category { get; set; } = "unknown";
    public bool Success { get; set; }
    public string? Error { get; set; }
}
```

Create `src/ClaudePermissionAnalyzer.Api/Services/ClaudeCliClient.cs`:

```csharp
using ClaudePermissionAnalyzer.Api.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudePermissionAnalyzer.Api.Services;

public class ClaudeCliClient : ILLMClient
{
    private readonly LlmConfig _config;

    public ClaudeCliClient(LlmConfig config)
    {
        _config = config;
    }

    public async Task<LLMResponse> QueryAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            var args = BuildCommandArgs(prompt);
            var output = await ExecuteCommandAsync("claude", args, _config.Timeout, cancellationToken);
            return ParseResponse(output);
        }
        catch (Exception ex)
        {
            return new LLMResponse
            {
                Success = false,
                Error = ex.Message,
                SafetyScore = 0,
                Reasoning = "LLM query failed"
            };
        }
    }

    public LLMResponse ParseResponse(string output)
    {
        try
        {
            // Try to find JSON in the output
            var jsonMatch = Regex.Match(output, @"\{[^{}]*""safetyScore""[^{}]*\}", RegexOptions.Singleline);

            if (!jsonMatch.Success)
            {
                return new LLMResponse
                {
                    Success = false,
                    SafetyScore = 0,
                    Reasoning = "Failed to parse LLM response: No valid JSON found"
                };
            }

            var json = jsonMatch.Value;
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new LLMResponse
            {
                Success = true,
                SafetyScore = root.GetProperty("safetyScore").GetInt32(),
                Reasoning = root.GetProperty("reasoning").GetString() ?? "No reasoning provided",
                Category = root.GetProperty("category").GetString() ?? "unknown"
            };
        }
        catch (Exception ex)
        {
            return new LLMResponse
            {
                Success = false,
                SafetyScore = 0,
                Reasoning = $"Failed to parse LLM response: {ex.Message}"
            };
        }
    }

    private string BuildCommandArgs(string prompt)
    {
        var escapedPrompt = prompt.Replace("\"", "\\\"");
        return $"--model {_config.Model} \"{escapedPrompt}\"";
    }

    private async Task<string> ExecuteCommandAsync(
        string command,
        string args,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var output = new StringBuilder();
        var error = new StringBuilder();

        process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new TimeoutException($"Command timed out after {timeoutMs}ms");
        }

        if (process.ExitCode != 0)
        {
            throw new Exception($"Command failed: {error}");
        }

        return output.ToString();
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~LLMClientTests"`
Expected: PASS (2 tests)

**Step 5: Commit**

```bash
git add src/ClaudePermissionAnalyzer.Api/Services/ILLMClient.cs src/ClaudePermissionAnalyzer.Api/Services/ClaudeCliClient.cs tests/ClaudePermissionAnalyzer.Tests/Services/LLMClientTests.cs
git commit -m "feat: add LLM client interface and Claude CLI implementation

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Phase 5: Hook Handlers

### Task 7: Hook Handler Interface and LLM Analysis Handler

**Files:**
- Create: `src/ClaudePermissionAnalyzer.Api/Handlers/IHookHandler.cs`
- Create: `src/ClaudePermissionAnalyzer.Api/Handlers/LLMAnalysisHandler.cs`
- Test: `tests/ClaudePermissionAnalyzer.Tests/Handlers/LLMAnalysisHandlerTests.cs`

**Step 1: Write the failing test**

```csharp
using ClaudePermissionAnalyzer.Api.Handlers;
using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using Moq;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Handlers;

public class LLMAnalysisHandlerTests
{
    [Fact]
    public async Task HandleAsync_ShouldAutoApprove_WhenScoreAboveThreshold()
    {
        // Arrange
        var mockLLM = new Mock<ILLMClient>();
        mockLLM.Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LLMResponse
            {
                Success = true,
                SafetyScore = 96,
                Reasoning = "Safe command",
                Category = "safe"
            });

        var handler = new LLMAnalysisHandler(mockLLM.Object, null);
        var input = new HookInput
        {
            HookEventName = "PermissionRequest",
            ToolName = "Bash",
            SessionId = "test-123"
        };
        var config = new HandlerConfig
        {
            Threshold = 95,
            AutoApprove = true
        };

        // Act
        var output = await handler.HandleAsync(input, config, "");

        // Assert
        Assert.True(output.AutoApprove);
        Assert.Equal(96, output.SafetyScore);
        Assert.Equal("Safe command", output.Reasoning);
    }

    [Fact]
    public async Task HandleAsync_ShouldDeny_WhenScoreBelowThreshold()
    {
        // Arrange
        var mockLLM = new Mock<ILLMClient>();
        mockLLM.Setup(x => x.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LLMResponse
            {
                Success = true,
                SafetyScore = 85,
                Reasoning = "Risky operation",
                Category = "risky"
            });

        var handler = new LLMAnalysisHandler(mockLLM.Object, null);
        var input = new HookInput { ToolName = "Bash", SessionId = "test-123" };
        var config = new HandlerConfig { Threshold = 90, AutoApprove = true };

        // Act
        var output = await handler.HandleAsync(input, config, "");

        // Assert
        Assert.False(output.AutoApprove);
        Assert.Equal(85, output.SafetyScore);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LLMAnalysisHandlerTests"`
Expected: FAIL with "type IHookHandler not found"

Add Moq package first:
```bash
dotnet add tests/ClaudePermissionAnalyzer.Tests/ClaudePermissionAnalyzer.Tests.csproj package Moq
```

**Step 3: Write minimal implementation**

Create `src/ClaudePermissionAnalyzer.Api/Handlers/IHookHandler.cs`:

```csharp
using ClaudePermissionAnalyzer.Api.Models;

namespace ClaudePermissionAnalyzer.Api.Handlers;

public interface IHookHandler
{
    Task<HookOutput> HandleAsync(HookInput input, HandlerConfig config, string sessionContext);
}
```

Create `src/ClaudePermissionAnalyzer.Api/Handlers/LLMAnalysisHandler.cs`:

```csharp
using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using System.Text;

namespace ClaudePermissionAnalyzer.Api.Handlers;

public class LLMAnalysisHandler : IHookHandler
{
    private readonly ILLMClient _llmClient;
    private readonly string? _promptTemplate;

    public LLMAnalysisHandler(ILLMClient llmClient, string? promptTemplate)
    {
        _llmClient = llmClient;
        _promptTemplate = promptTemplate;
    }

    public async Task<HookOutput> HandleAsync(HookInput input, HandlerConfig config, string sessionContext)
    {
        var prompt = BuildPrompt(input, config, sessionContext);
        var llmResponse = await _llmClient.QueryAsync(prompt);

        if (!llmResponse.Success)
        {
            return new HookOutput
            {
                AutoApprove = false,
                SafetyScore = 0,
                Reasoning = llmResponse.Error ?? "LLM query failed",
                Category = "error",
                Threshold = config.Threshold
            };
        }

        var autoApprove = config.AutoApprove && llmResponse.SafetyScore >= config.Threshold;

        return new HookOutput
        {
            AutoApprove = autoApprove,
            SafetyScore = llmResponse.SafetyScore,
            Reasoning = llmResponse.Reasoning,
            Category = llmResponse.Category,
            Threshold = config.Threshold,
            Interrupt = llmResponse.Category == "dangerous"
        };
    }

    private string BuildPrompt(HookInput input, HandlerConfig config, string sessionContext)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(_promptTemplate))
        {
            sb.AppendLine(_promptTemplate);
        }
        else
        {
            sb.AppendLine("Analyze the safety of this operation and provide a score from 0-100.");
        }

        sb.AppendLine();
        sb.AppendLine($"TOOL: {input.ToolName}");
        sb.AppendLine($"WORKING DIR: {input.Cwd}");

        if (input.ToolInput.HasValue)
        {
            sb.AppendLine($"TOOL INPUT: {input.ToolInput}");
        }

        if (!string.IsNullOrEmpty(sessionContext))
        {
            sb.AppendLine();
            sb.AppendLine(sessionContext);
        }

        sb.AppendLine();
        sb.AppendLine("Respond ONLY with valid JSON:");
        sb.AppendLine("{");
        sb.AppendLine("  \"safetyScore\": <number 0-100>,");
        sb.AppendLine("  \"reasoning\": \"<brief explanation>\",");
        sb.AppendLine("  \"category\": \"<safe|cautious|risky|dangerous>\"");
        sb.AppendLine("}");

        return sb.ToString();
    }
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~LLMAnalysisHandlerTests"`
Expected: PASS (2 tests)

**Step 5: Commit**

```bash
git add src/ClaudePermissionAnalyzer.Api/Handlers/*.cs tests/ClaudePermissionAnalyzer.Tests/Handlers/LLMAnalysisHandlerTests.cs
git commit -m "feat: add hook handler interface and LLM analysis handler

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Phase 6: API Endpoints

### Task 8: Analyze Controller

**Files:**
- Create: `src/ClaudePermissionAnalyzer.Api/Controllers/AnalyzeController.cs`
- Modify: `src/ClaudePermissionAnalyzer.Api/Program.cs`
- Test: Integration test via HTTP

**Step 1: Write the implementation**

Create `src/ClaudePermissionAnalyzer.Api/Controllers/AnalyzeController.cs`:

```csharp
using ClaudePermissionAnalyzer.Api.Handlers;
using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClaudePermissionAnalyzer.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AnalyzeController : ControllerBase
{
    private readonly ConfigurationManager _configManager;
    private readonly SessionManager _sessionManager;
    private readonly ILLMClient _llmClient;
    private readonly ILogger<AnalyzeController> _logger;

    public AnalyzeController(
        ConfigurationManager configManager,
        SessionManager sessionManager,
        ILLMClient llmClient,
        ILogger<AnalyzeController> logger)
    {
        _configManager = configManager;
        _sessionManager = sessionManager;
        _llmClient = llmClient;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Analyze([FromBody] HookInput input)
    {
        try
        {
            _logger.LogInformation("Received {HookType} hook for {ToolName}",
                input.HookEventName, input.ToolName);

            // Find matching handler
            var handler = _configManager.FindMatchingHandler(input.HookEventName, input.ToolName);

            if (handler == null || handler.Mode == "log-only")
            {
                // Just log the event
                await LogEventAsync(input, null);
                return Ok(new { autoApprove = false, message = "No handler configured" });
            }

            // Build session context
            var context = await _sessionManager.BuildContextAsync(input.SessionId);

            // Execute handler
            var handlerInstance = CreateHandler(handler);
            var output = await handlerInstance.HandleAsync(input, handler, context);

            // Record decision
            await LogEventAsync(input, output);

            return Ok(output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing hook");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private IHookHandler CreateHandler(HandlerConfig config)
    {
        return config.Mode switch
        {
            "llm-analysis" => new LLMAnalysisHandler(_llmClient, config.PromptTemplate),
            _ => throw new NotSupportedException($"Handler mode '{config.Mode}' not supported")
        };
    }

    private async Task LogEventAsync(HookInput input, HookOutput? output)
    {
        var evt = new SessionEvent
        {
            Type = input.HookEventName,
            ToolName = input.ToolName,
            ToolInput = input.ToolInput,
            Decision = output?.AutoApprove == true ? "auto-approved" : "denied",
            SafetyScore = output?.SafetyScore,
            Reasoning = output?.Reasoning,
            Category = output?.Category
        };

        await _sessionManager.RecordEventAsync(input.SessionId, evt);
    }
}
```

**Step 2: Update Program.cs**

Modify `src/ClaudePermissionAnalyzer.Api/Program.cs`:

```csharp
using ClaudePermissionAnalyzer.Api.Services;
using ClaudePermissionAnalyzer.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure application services
var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".claude-permission-analyzer",
    "config.json");

var configManager = new ConfigurationManager(configPath);
var config = await configManager.LoadAsync();

builder.Services.AddSingleton(configManager);
builder.Services.AddSingleton(new SessionManager(config.Session.StorageDir, config.Session.MaxHistoryPerSession));
builder.Services.AddSingleton<ILLMClient>(sp => new ClaudeCliClient(config.Llm));

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run($"http://{config.Server.Host}:{config.Server.Port}");
```

**Step 3: Test the API manually**

Run: `dotnet run --project src/ClaudePermissionAnalyzer.Api`
Test with curl or Postman:
```bash
curl -X POST http://localhost:5050/api/analyze \
  -H "Content-Type: application/json" \
  -d '{
    "hookEventName": "PermissionRequest",
    "sessionId": "test-123",
    "toolName": "Bash",
    "toolInput": {"command": "git status"},
    "cwd": "/home/user/project"
  }'
```

**Step 4: Commit**

```bash
git add src/ClaudePermissionAnalyzer.Api/Controllers/AnalyzeController.cs src/ClaudePermissionAnalyzer.Api/Program.cs
git commit -m "feat: add analyze API endpoint with handler integration

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Phase 7: Python Hook Scripts

### Task 9: Generic Hook Script Template

**Files:**
- Create: `hooks/bash-hook.py`
- Create: `hooks/file-read-hook.py`
- Create: `hooks/file-write-hook.py`

**Step 1: Create bash hook script**

Create `hooks/bash-hook.py`:

```python
#!/usr/bin/env python3
import json
import sys
import requests
from typing import Optional

SERVICE_URL = "http://localhost:5050/api/analyze"
TIMEOUT = 30

def main():
    try:
        # Read hook input from stdin
        hook_input = json.load(sys.stdin)

        # Add hook type
        hook_input["hookEventName"] = "PermissionRequest"

        # Send to service
        response = requests.post(
            SERVICE_URL,
            json=hook_input,
            timeout=TIMEOUT
        )

        if response.status_code != 200:
            # Service error - fall back to user decision
            sys.exit(0)

        result = response.json()
        output = format_permission_output(result)

        print(json.dumps(output))
        sys.exit(0)

    except requests.exceptions.ConnectionError:
        # Service not running - fall back
        sys.exit(0)
    except Exception as e:
        print(f"Hook error: {e}", file=sys.stderr)
        sys.exit(0)

def format_permission_output(result: dict) -> dict:
    output = {
        "hookSpecificOutput": {
            "hookEventName": "PermissionRequest",
            "decision": {
                "behavior": "allow" if result.get("autoApprove") else "deny"
            }
        }
    }

    if not result.get("autoApprove"):
        output["hookSpecificOutput"]["decision"]["message"] = (
            f"Safety score {result.get('safetyScore', 0)} below threshold "
            f"{result.get('threshold', 0)}. Reason: {result.get('reasoning', 'Unknown')}"
        )

        if result.get("interrupt"):
            output["hookSpecificOutput"]["decision"]["interrupt"] = True

    return output

if __name__ == "__main__":
    main()
```

**Step 2: Create file-read hook script**

Create `hooks/file-read-hook.py`:

```python
#!/usr/bin/env python3
import json
import sys
import requests

SERVICE_URL = "http://localhost:5050/api/analyze"
TIMEOUT = 30

def main():
    try:
        hook_input = json.load(sys.stdin)
        hook_input["hookEventName"] = "PermissionRequest"

        response = requests.post(SERVICE_URL, json=hook_input, timeout=TIMEOUT)

        if response.status_code != 200:
            sys.exit(0)

        result = response.json()
        output = {
            "hookSpecificOutput": {
                "hookEventName": "PermissionRequest",
                "decision": {
                    "behavior": "allow" if result.get("autoApprove") else "deny"
                }
            }
        }

        if not result.get("autoApprove"):
            output["hookSpecificOutput"]["decision"]["message"] = result.get("reasoning", "Denied")

        print(json.dumps(output))
        sys.exit(0)

    except:
        sys.exit(0)

if __name__ == "__main__":
    main()
```

**Step 3: Create file-write hook script**

Create `hooks/file-write-hook.py`:

```python
#!/usr/bin/env python3
import json
import sys
import requests

SERVICE_URL = "http://localhost:5050/api/analyze"
TIMEOUT = 30

def main():
    try:
        hook_input = json.load(sys.stdin)
        hook_input["hookEventName"] = "PermissionRequest"

        response = requests.post(SERVICE_URL, json=hook_input, timeout=TIMEOUT)

        if response.status_code != 200:
            sys.exit(0)

        result = response.json()
        output = {
            "hookSpecificOutput": {
                "hookEventName": "PermissionRequest",
                "decision": {
                    "behavior": "allow" if result.get("autoApprove") else "deny"
                }
            }
        }

        if not result.get("autoApprove"):
            output["hookSpecificOutput"]["decision"]["message"] = result.get("reasoning", "Denied")

        print(json.dumps(output))
        sys.exit(0)

    except:
        sys.exit(0)

if __name__ == "__main__":
    main()
```

**Step 4: Make scripts executable**

Run: `chmod +x hooks/*.py` (on Unix-like systems)

**Step 5: Test a hook manually**

```bash
echo '{"toolName":"Bash","toolInput":{"command":"ls"},"sessionId":"test","cwd":"/tmp"}' | python hooks/bash-hook.py
```

**Step 6: Commit**

```bash
git add hooks/*.py
git commit -m "feat: add Python hook scripts for Bash, Read, and Write operations

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Phase 8: Web UI

### Task 10: Dashboard HTML and CSS

**Files:**
- Create: `src/ClaudePermissionAnalyzer.Api/wwwroot/index.html`
- Create: `src/ClaudePermissionAnalyzer.Api/wwwroot/css/styles.css`

**Step 1: Create dashboard HTML**

Create `src/ClaudePermissionAnalyzer.Api/wwwroot/index.html`:

```html
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>Claude Permission Analyzer</title>
    <link rel="stylesheet" href="/css/styles.css">
</head>
<body>
    <div class="container">
        <header>
            <h1>Claude Permission Analyzer</h1>
            <div class="status">
                <span class="status-indicator active"></span>
                <span>Service Running</span>
            </div>
        </header>

        <nav>
            <a href="/" class="active">Dashboard</a>
            <a href="/logs.html">Live Logs</a>
            <a href="/config.html">Configuration</a>
        </nav>

        <main>
            <section class="stats">
                <div class="stat-card">
                    <h3>Auto-Approved Today</h3>
                    <div class="stat-value" id="autoApprovedCount">0</div>
                </div>
                <div class="stat-card">
                    <h3>Denied Today</h3>
                    <div class="stat-value" id="deniedCount">0</div>
                </div>
                <div class="stat-card">
                    <h3>Active Sessions</h3>
                    <div class="stat-value" id="activeSessionCount">0</div>
                </div>
                <div class="stat-card">
                    <h3>Avg Safety Score</h3>
                    <div class="stat-value" id="avgSafetyScore">0</div>
                </div>
            </section>

            <section class="recent-activity">
                <h2>Recent Activity</h2>
                <div id="activityList" class="activity-list">
                    <p class="empty-state">No recent activity</p>
                </div>
            </section>
        </main>
    </div>

    <script src="/js/dashboard.js"></script>
</body>
</html>
```

**Step 2: Create CSS styles**

Create `src/ClaudePermissionAnalyzer.Api/wwwroot/css/styles.css`:

```css
* {
    margin: 0;
    padding: 0;
    box-sizing: border-box;
}

body {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
    background: #f5f5f5;
    color: #333;
}

.container {
    max-width: 1200px;
    margin: 0 auto;
    padding: 20px;
}

header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 30px;
}

header h1 {
    font-size: 28px;
    color: #1a1a1a;
}

.status {
    display: flex;
    align-items: center;
    gap: 8px;
}

.status-indicator {
    width: 12px;
    height: 12px;
    border-radius: 50%;
    background: #ccc;
}

.status-indicator.active {
    background: #4CAF50;
    box-shadow: 0 0 8px #4CAF50;
}

nav {
    display: flex;
    gap: 20px;
    margin-bottom: 30px;
    border-bottom: 2px solid #e0e0e0;
}

nav a {
    padding: 10px 20px;
    text-decoration: none;
    color: #666;
    border-bottom: 2px solid transparent;
    margin-bottom: -2px;
}

nav a.active {
    color: #1976D2;
    border-bottom-color: #1976D2;
}

.stats {
    display: grid;
    grid-template-columns: repeat(auto-fit, minmax(250px, 1fr));
    gap: 20px;
    margin-bottom: 40px;
}

.stat-card {
    background: white;
    padding: 24px;
    border-radius: 8px;
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
}

.stat-card h3 {
    font-size: 14px;
    color: #666;
    margin-bottom: 12px;
}

.stat-value {
    font-size: 36px;
    font-weight: bold;
    color: #1976D2;
}

.recent-activity {
    background: white;
    padding: 24px;
    border-radius: 8px;
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
}

.recent-activity h2 {
    font-size: 20px;
    margin-bottom: 20px;
}

.activity-list {
    display: flex;
    flex-direction: column;
    gap: 12px;
}

.activity-item {
    padding: 12px;
    border-left: 4px solid #4CAF50;
    background: #f9f9f9;
    border-radius: 4px;
}

.activity-item.denied {
    border-left-color: #f44336;
}

.activity-time {
    font-size: 12px;
    color: #999;
}

.empty-state {
    text-align: center;
    color: #999;
    padding: 40px;
}
```

**Step 3: Commit**

```bash
git add src/ClaudePermissionAnalyzer.Api/wwwroot/
git commit -m "feat: add web UI dashboard with statistics display

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Phase 9: Installation and Documentation

### Task 11: Installation Script and README

**Files:**
- Create: `install.ps1`
- Create: `README.md`

**Step 1: Create installation script**

Create `install.ps1`:

```powershell
# Claude Permission Analyzer Installation Script

Write-Host "Installing Claude Permission Analyzer..." -ForegroundColor Green

# Create installation directory
$installDir = Join-Path $env:USERPROFILE ".claude-permission-analyzer"
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir | Out-Null
}

# Copy files
Write-Host "Copying files..."
Copy-Item "ClaudePermissionAnalyzer.exe" $installDir -Force
Copy-Item "hooks/*" (Join-Path $installDir "hooks") -Recurse -Force
Copy-Item "prompts/*" (Join-Path $installDir "prompts") -Recurse -Force
Copy-Item "wwwroot/*" (Join-Path $installDir "wwwroot") -Recurse -Force

# Install Python dependencies
Write-Host "Installing Python dependencies..."
pip install requests

# Create default config if not exists
$configPath = Join-Path $installDir "config.json"
if (-not (Test-Path $configPath)) {
    @"
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
"@ | Out-File $configPath -Encoding UTF8
}

Write-Host "`nInstallation complete!" -ForegroundColor Green
Write-Host "Run '$installDir\ClaudePermissionAnalyzer.exe' to start the service."
Write-Host "Access the web UI at http://localhost:5050"
```

**Step 2: Create README**

Create `README.md`:

```markdown
# Claude Permission Analyzer

An intelligent permission automation system for Claude Code that uses LLM-based safety analysis to automatically approve safe operations.

## Features

- **LLM-Based Safety Analysis**: Evaluates operations using Claude or GitHub Copilot CLI
- **Configurable Thresholds**: Per-hook safety thresholds with smart defaults
- **Session Tracking**: Contextual awareness from conversation history
- **Web Dashboard**: Real-time monitoring and configuration
- **Privacy Controls**: Configure what data is sent to LLM

## Installation

### Prerequisites

- .NET 8+ Runtime
- Python 3.8+
- Claude CLI (`claude`) or GitHub CLI (`gh`) configured
- Windows OS (Linux/Mac support coming soon)

### Quick Install

1. Extract the release package
2. Run installation script:
   ```powershell
   .\install.ps1
   ```
3. Start the service:
   ```powershell
   ClaudePermissionAnalyzer.exe
   ```
4. Open web UI: http://localhost:5050

## Configuration

Edit `~/.claude-permission-analyzer/config.json` to customize:

- LLM provider and model
- Safety thresholds per hook type
- Auto-approval settings
- Privacy controls

## Usage

Once installed and running, the service automatically:

1. Receives hook events from Claude Code
2. Analyzes safety using LLM
3. Auto-approves or denies based on threshold
4. Logs all decisions to session history

View activity in the web dashboard at http://localhost:5050.

## Development

### Build

```bash
dotnet build
```

### Test

```bash
dotnet test
```

### Run Locally

```bash
dotnet run --project src/ClaudePermissionAnalyzer.Api
```

## License

MIT
```

**Step 3: Commit**

```bash
git add install.ps1 README.md
git commit -m "feat: add installation script and documentation

Co-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"
```

---

## Summary

This implementation plan provides a complete roadmap for building the Claude Permission Analyzer following TDD principles. Each task is bite-sized (2-5 minutes per step) with:

- Clear test-first approach
- Minimal implementations
- Frequent commits
- Progressive feature building

The plan follows the architecture from the design document and implements all core components:

✅ Configuration management
✅ Session tracking
✅ LLM integration
✅ Hook handlers
✅ API endpoints
✅ Python hook scripts
✅ Web UI
✅ Installation tooling

Execute this plan using **@superpowers:executing-plans** or **@superpowers:subagent-driven-development** for best results.
