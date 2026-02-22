using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudePermissionAnalyzer.Api.Models;
using ClaudePermissionAnalyzer.Api.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace ClaudePermissionAnalyzer.Tests.Services;

public class HookInstallerTests : IDisposable
{
    private readonly Mock<ILogger<HookInstaller>> _mockLogger;
    private readonly string _tempDir;
    private readonly string _tempSettingsPath;
    private readonly string _tempConfigPath;

    public HookInstallerTests()
    {
        _mockLogger = new Mock<ILogger<HookInstaller>>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"hook-installer-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
        _tempSettingsPath = Path.Combine(_tempDir, "settings.json");
        _tempConfigPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    private HookInstaller CreateInstaller(string serviceUrl = "http://localhost:5050", Configuration? config = null)
    {
        config ??= CreateDefaultConfig();
        var configManager = new ConfigurationManager(config);
        var installer = new HookInstaller(_mockLogger.Object, configManager, serviceUrl);

        // Redirect _settingsPath to temp file
        var field = typeof(HookInstaller).GetField("_settingsPath", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field!.SetValue(installer, _tempSettingsPath);

        return installer;
    }

    private static Configuration CreateDefaultConfig()
    {
        return new Configuration
        {
            HookHandlers = new Dictionary<string, HookEventConfig>
            {
                ["PermissionRequest"] = new HookEventConfig
                {
                    Enabled = true,
                    Handlers = new List<HandlerConfig>
                    {
                        new() { Name = "bash-analyzer", Matcher = "Bash", Mode = "llm-analysis", Threshold = 95 },
                        new() { Name = "file-read-analyzer", Matcher = "Read", Mode = "llm-analysis", Threshold = 93 },
                    }
                },
                ["PreToolUse"] = new HookEventConfig
                {
                    Enabled = true,
                    Handlers = new List<HandlerConfig>
                    {
                        new() { Name = "pre-tool-logger", Matcher = "*", Mode = "log-only" }
                    }
                },
                ["Stop"] = new HookEventConfig
                {
                    Enabled = true,
                    Handlers = new List<HandlerConfig>
                    {
                        new() { Name = "stop-logger", Mode = "log-only" }
                    }
                }
            }
        };
    }

    [Fact]
    public void Install_CreatesHooksFromAppConfig()
    {
        var installer = CreateInstaller();
        installer.Install();

        Assert.True(File.Exists(_tempSettingsPath));
        var doc = JsonNode.Parse(File.ReadAllText(_tempSettingsPath))!;
        var hooks = doc["hooks"]!;

        // Config has PermissionRequest (2 handlers), PreToolUse (1), Stop (1)
        Assert.NotNull(hooks["PermissionRequest"]);
        Assert.Equal(2, hooks["PermissionRequest"]!.AsArray().Count);
        Assert.NotNull(hooks["PreToolUse"]);
        Assert.Single(hooks["PreToolUse"]!.AsArray());
        Assert.NotNull(hooks["Stop"]);
        Assert.Single(hooks["Stop"]!.AsArray());
    }

    [Fact]
    public void Install_DoesNotDuplicate_WhenCalledTwice()
    {
        var installer = CreateInstaller();
        installer.Install();
        installer.Install(); // Second call should NOT duplicate

        var doc = JsonNode.Parse(File.ReadAllText(_tempSettingsPath))!;
        var permReq = doc["hooks"]!["PermissionRequest"]!.AsArray();
        Assert.Equal(2, permReq.Count); // Still 2, not 4
    }

    [Fact]
    public void Install_ReflectsConfigChanges_OnReinstall()
    {
        // First install with 2 PermissionRequest handlers
        var config = CreateDefaultConfig();
        var configManager = new ConfigurationManager(config);
        var installer = new HookInstaller(_mockLogger.Object, configManager, "http://localhost:5050");
        typeof(HookInstaller).GetField("_settingsPath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(installer, _tempSettingsPath);

        installer.Install();
        var doc1 = JsonNode.Parse(File.ReadAllText(_tempSettingsPath))!;
        Assert.Equal(2, doc1["hooks"]!["PermissionRequest"]!.AsArray().Count);

        // Add a third handler to the config
        config.HookHandlers["PermissionRequest"].Handlers.Add(
            new HandlerConfig { Name = "wildcard", Matcher = "*", Mode = "llm-analysis", Threshold = 85 });

        // Reinstall - should now have 3, not 5
        installer.Install();
        var doc2 = JsonNode.Parse(File.ReadAllText(_tempSettingsPath))!;
        Assert.Equal(3, doc2["hooks"]!["PermissionRequest"]!.AsArray().Count);
    }

    [Fact]
    public void Install_PreservesUserHooks()
    {
        var installer = CreateInstaller();

        // Write a settings file with user's own hook
        var existingDoc = new JsonObject
        {
            ["hooks"] = new JsonObject
            {
                ["PermissionRequest"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["matcher"] = "CustomTool",
                        ["hooks"] = new JsonArray
                        {
                            new JsonObject { ["type"] = "command", ["command"] = "echo 'user hook'" }
                        }
                    }
                }
            }
        };
        File.WriteAllText(_tempSettingsPath, existingDoc.ToJsonString());

        installer.Install();

        var doc = JsonNode.Parse(File.ReadAllText(_tempSettingsPath))!;
        var permReq = doc["hooks"]!["PermissionRequest"]!.AsArray();

        // 1 user hook + 2 from config = 3
        Assert.Equal(3, permReq.Count);

        // Verify user hook is still there
        bool userHookFound = false;
        foreach (var entry in permReq)
        {
            var cmd = entry?["hooks"]?[0]?["command"]?.GetValue<string>();
            if (cmd == "echo 'user hook'") userHookFound = true;
        }
        Assert.True(userHookFound);
    }

    [Fact]
    public void Install_PreservesOtherSettings()
    {
        var installer = CreateInstaller();
        var existing = new JsonObject { ["someOtherSetting"] = "preserved", ["anotherKey"] = 42 };
        File.WriteAllText(_tempSettingsPath, existing.ToJsonString());

        installer.Install();

        var doc = JsonNode.Parse(File.ReadAllText(_tempSettingsPath))!;
        Assert.Equal("preserved", doc["someOtherSetting"]?.GetValue<string>());
        Assert.Equal(42, doc["anotherKey"]?.GetValue<int>());
    }

    [Fact]
    public void Install_SkipsDisabledEventTypes()
    {
        var config = CreateDefaultConfig();
        config.HookHandlers["PermissionRequest"].Enabled = false;
        var installer = CreateInstaller(config: config);

        installer.Install();

        var doc = JsonNode.Parse(File.ReadAllText(_tempSettingsPath))!;
        // PermissionRequest is disabled, should not appear
        Assert.Null(doc["hooks"]?["PermissionRequest"]);
        // PreToolUse and Stop should still be there
        Assert.NotNull(doc["hooks"]?["PreToolUse"]);
        Assert.NotNull(doc["hooks"]?["Stop"]);
    }

    [Fact]
    public void IsInstalled_ReturnsFalse_WhenNoSettingsFile()
    {
        var installer = CreateInstaller();
        Assert.False(installer.IsInstalled());
    }

    [Fact]
    public void IsInstalled_ReturnsTrue_AfterInstall()
    {
        var installer = CreateInstaller();
        installer.Install();
        Assert.True(installer.IsInstalled());
    }

    [Fact]
    public void Uninstall_RemovesOurHooks_KeepsUserHooks()
    {
        var installer = CreateInstaller();
        installer.Install();

        // Add a user hook
        var json = File.ReadAllText(_tempSettingsPath);
        var doc = JsonNode.Parse(json)!;
        doc["hooks"]!.AsObject()["CustomEvent"] = new JsonArray
        {
            new JsonObject
            {
                ["hooks"] = new JsonArray
                {
                    new JsonObject { ["type"] = "command", ["command"] = "echo 'user'" }
                }
            }
        };
        doc.AsObject()["userSetting"] = "keep";
        File.WriteAllText(_tempSettingsPath, doc.ToJsonString());

        installer.Uninstall();

        var result = JsonNode.Parse(File.ReadAllText(_tempSettingsPath))!;
        Assert.Equal("keep", result["userSetting"]?.GetValue<string>());
        Assert.NotNull(result["hooks"]?["CustomEvent"]);
        Assert.False(installer.IsInstalled());
    }

    [Fact]
    public void InstallThenUninstall_LeavesFileClean()
    {
        var installer = CreateInstaller();
        installer.Install();
        Assert.True(installer.IsInstalled());

        installer.Uninstall();
        Assert.False(installer.IsInstalled());

        var doc = JsonNode.Parse(File.ReadAllText(_tempSettingsPath))!;
        Assert.Null(doc["hooks"]);
    }

    [Fact]
    public void Install_ContainsCorrectServiceUrl()
    {
        var installer = CreateInstaller("http://localhost:9999");
        installer.Install();

        var json = File.ReadAllText(_tempSettingsPath);
        Assert.Contains("http://localhost:9999", json);
        Assert.Contains("# claude-analyzer", json);
    }
}
