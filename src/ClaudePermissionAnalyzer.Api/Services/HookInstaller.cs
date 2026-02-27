using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudePermissionAnalyzer.Api.Models;

namespace ClaudePermissionAnalyzer.Api.Services;

public class HookInstaller
{
    private readonly ILogger<HookInstaller> _logger;
    private readonly ConfigurationManager _configManager;
    private readonly string _settingsPath;
    private readonly string _serviceUrl;

    // Marker used to identify our hooks vs user's own hooks
    private const string HookMarker = "# claude-analyzer";

    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = true
    };

    public HookInstaller(ILogger<HookInstaller> logger, ConfigurationManager configManager, string serviceUrl = "http://localhost:5050")
    {
        _logger = logger;
        _configManager = configManager;
        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            "settings.json");
        _serviceUrl = serviceUrl;
    }

    public bool IsInstalled()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return false;

            var json = File.ReadAllText(_settingsPath);
            var doc = JsonNode.Parse(json);
            var hooks = doc?["hooks"];
            if (hooks == null)
                return false;

            return ContainsOurHooks(hooks);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check hook installation status");
            return false;
        }
    }

    /// <summary>
    /// Installs hooks derived from the app's hookHandlers config.
    /// Always removes our old hooks first to prevent duplication.
    /// User's own hooks (without our marker) are preserved.
    /// </summary>
    public void Install()
    {
        var appConfig = _configManager.GetConfiguration();
        _logger.LogDebug("Syncing Claude hooks from app config ({Count} event types)", appConfig.HookHandlers.Count);

        var doc = LoadOrCreateSettingsDoc();
        var hooks = doc["hooks"]?.AsObject() ?? new JsonObject();

        // Step 1: Remove ALL our old hooks (by marker) to prevent duplication
        RemoveOurHooks(hooks);

        // Step 2: Add hooks derived from the app's hookHandlers config
        foreach (var (eventName, eventConfig) in appConfig.HookHandlers)
        {
            if (!eventConfig.Enabled || eventConfig.Handlers.Count == 0)
                continue;

            // Get or create the array for this event type
            var arr = hooks[eventName]?.AsArray() ?? new JsonArray();

            foreach (var handler in eventConfig.Handlers)
            {
                var matcher = handler.Matcher;
                var command = $"curl -sS -X POST \"{_serviceUrl}/api/hooks/claude?event={eventName}\" -H \"Content-Type: application/json\" -d @- {HookMarker}";

                var hookObj = new JsonObject
                {
                    ["hooks"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "command",
                            ["command"] = command
                        }
                    }
                };

                if (!string.IsNullOrEmpty(matcher) && matcher != "*")
                    hookObj["matcher"] = matcher;

                arr.Add(hookObj);
            }

            hooks[eventName] = arr;
        }

        doc["hooks"] = hooks;
        CleanupEmptyHooks(doc);

        File.WriteAllText(_settingsPath, doc.ToJsonString(s_writeOptions));
        _logger.LogDebug("Claude hooks synced successfully");
    }

    public void Uninstall()
    {
        _logger.LogDebug("Uninstalling Claude hooks");

        if (!File.Exists(_settingsPath))
        {
            _logger.LogDebug("Settings file not found, nothing to uninstall");
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var doc = JsonNode.Parse(json);
            if (doc == null) return;

            var hooks = doc["hooks"]?.AsObject();
            if (hooks == null) return;

            RemoveOurHooks(hooks);
            CleanupEmptyHooks(doc);

            File.WriteAllText(_settingsPath, doc.ToJsonString(s_writeOptions));
            _logger.LogDebug("Claude hooks uninstalled successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall hooks from {Path}", _settingsPath);
            throw;
        }
    }

    private JsonNode LoadOrCreateSettingsDoc()
    {
        if (File.Exists(_settingsPath))
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonNode.Parse(json) ?? new JsonObject();
        }

        var dir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        return new JsonObject();
    }

    private static void CleanupEmptyHooks(JsonNode doc)
    {
        var hooks = doc["hooks"]?.AsObject();
        if (hooks == null) return;

        var emptyKeys = new List<string>();
        foreach (var kvp in hooks)
        {
            if (kvp.Value is JsonArray arr && arr.Count == 0)
                emptyKeys.Add(kvp.Key);
        }
        foreach (var key in emptyKeys)
            hooks.Remove(key);

        if (hooks.Count == 0)
            doc.AsObject().Remove("hooks");
    }

    private bool ContainsOurHooks(JsonNode hooks)
    {
        if (hooks is not JsonObject obj) return false;

        foreach (var kvp in obj)
        {
            if (kvp.Value is not JsonArray arr) continue;
            foreach (var entry in arr)
            {
                if (IsOurHookEntry(entry))
                    return true;
            }
        }
        return false;
    }

    private void RemoveOurHooks(JsonObject hooks)
    {
        foreach (var kvp in hooks.ToList())
        {
            if (kvp.Value is not JsonArray arr) continue;

            var toRemove = new List<JsonNode>();
            foreach (var entry in arr)
            {
                if (IsOurHookEntry(entry) && entry != null)
                    toRemove.Add(entry);
            }

            foreach (var node in toRemove)
                arr.Remove(node);
        }
    }

    private static bool IsOurHookEntry(JsonNode? entry)
    {
        var innerHooks = entry?["hooks"]?.AsArray();
        if (innerHooks == null) return false;

        foreach (var h in innerHooks)
        {
            var cmd = h?["command"]?.GetValue<string>();
            if (cmd != null && cmd.Contains(HookMarker))
                return true;
        }
        return false;
    }
}
