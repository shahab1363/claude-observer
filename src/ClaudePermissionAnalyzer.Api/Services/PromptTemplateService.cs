using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ClaudePermissionAnalyzer.Api.Services;

public class PromptTemplateService
{
    private readonly string _promptsDir;
    private readonly ConcurrentDictionary<string, string> _templateCache = new();
    private readonly ILogger<PromptTemplateService> _logger;
    private FileSystemWatcher? _watcher;

    public PromptTemplateService(string promptsDir, ILogger<PromptTemplateService> logger)
    {
        _promptsDir = promptsDir;
        _logger = logger;

        if (!Directory.Exists(_promptsDir))
        {
            try
            {
                Directory.CreateDirectory(_promptsDir);
                _logger.LogInformation("Created prompts directory: {PromptsDir}", _promptsDir);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not create prompts directory: {PromptsDir}", _promptsDir);
            }
        }

        LoadAllTemplates();
        StartWatching();
    }

    public string? GetTemplate(string templateName)
    {
        if (string.IsNullOrEmpty(templateName))
            return null;

        // Normalize: ensure .txt extension
        if (!templateName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            templateName += ".txt";

        if (_templateCache.TryGetValue(templateName, out var template))
            return template;

        // Try loading on demand
        var filePath = Path.Combine(_promptsDir, templateName);
        if (File.Exists(filePath))
        {
            try
            {
                var content = File.ReadAllText(filePath);
                _templateCache[templateName] = content;
                return content;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load prompt template {TemplateName}", templateName);
            }
        }

        return null;
    }

    public Dictionary<string, string> GetAllTemplates()
    {
        return new Dictionary<string, string>(_templateCache);
    }

    public List<string> GetTemplateNames()
    {
        return _templateCache.Keys.OrderBy(k => k).ToList();
    }

    public bool SaveTemplate(string templateName, string content)
    {
        if (string.IsNullOrEmpty(templateName))
            return false;

        if (!templateName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            templateName += ".txt";

        // Validate filename to prevent path traversal
        if (templateName.Contains("..") || templateName.Contains('/') || templateName.Contains('\\'))
            return false;

        try
        {
            var filePath = Path.Combine(_promptsDir, templateName);
            File.WriteAllText(filePath, content);
            _templateCache[templateName] = content;
            _logger.LogInformation("Saved prompt template {TemplateName}", templateName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save prompt template {TemplateName}", templateName);
            return false;
        }
    }

    private void LoadAllTemplates()
    {
        if (!Directory.Exists(_promptsDir))
            return;

        try
        {
            foreach (var file in Directory.GetFiles(_promptsDir, "*.txt"))
            {
                try
                {
                    var name = Path.GetFileName(file);
                    var content = File.ReadAllText(file);
                    _templateCache[name] = content;
                    _logger.LogDebug("Loaded prompt template: {TemplateName}", name);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load prompt template: {File}", file);
                }
            }

            _logger.LogInformation("Loaded {Count} prompt templates from {Dir}", _templateCache.Count, _promptsDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate prompt templates in {Dir}", _promptsDir);
        }
    }

    private void StartWatching()
    {
        if (!Directory.Exists(_promptsDir))
            return;

        try
        {
            _watcher = new FileSystemWatcher(_promptsDir, "*.txt")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnTemplateFileChanged;
            _watcher.Created += OnTemplateFileChanged;
            _watcher.Deleted += OnTemplateFileDeleted;

            _logger.LogInformation("Watching for prompt template changes in {Dir}", _promptsDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start prompt template file watcher");
        }
    }

    private void OnTemplateFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Small delay to let the file write complete
            Thread.Sleep(100);
            var content = File.ReadAllText(e.FullPath);
            var name = Path.GetFileName(e.FullPath);
            _templateCache[name] = content;
            _logger.LogInformation("Prompt template updated: {TemplateName}", name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload prompt template: {File}", e.FullPath);
        }
    }

    private void OnTemplateFileDeleted(object sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.FullPath);
        _templateCache.TryRemove(name, out _);
        _logger.LogInformation("Prompt template removed: {TemplateName}", name);
    }
}
