using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace ClaudePermissionAnalyzer.Api.Services;

public class AdaptiveThresholdService
{
    private readonly string _dataFilePath;
    private readonly ILogger<AdaptiveThresholdService>? _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private AdaptiveThresholdData _data = new();
    private const int MaxOverrideHistory = 500;
    private const int MinSamplesForSuggestion = 5;

    public AdaptiveThresholdService(string storageDir, ILogger<AdaptiveThresholdService>? logger = null)
    {
        _dataFilePath = Path.Combine(storageDir, "adaptive-thresholds.json");
        _logger = logger;
    }

    public async Task LoadAsync()
    {
        if (File.Exists(_dataFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_dataFilePath);
                _data = JsonSerializer.Deserialize<AdaptiveThresholdData>(json) ?? new AdaptiveThresholdData();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load adaptive threshold data, starting fresh");
                _data = new AdaptiveThresholdData();
            }
        }
    }

    public async Task RecordOverrideAsync(string toolName, string originalDecision, string userAction,
        int safetyScore, int threshold, string sessionId)
    {
        var overrideRecord = new ThresholdOverride
        {
            ToolName = toolName,
            OriginalDecision = originalDecision,
            UserAction = userAction,
            SafetyScore = safetyScore,
            Threshold = threshold,
            SessionId = sessionId
        };

        _data.Overrides.Add(overrideRecord);

        // Trim old overrides
        while (_data.Overrides.Count > MaxOverrideHistory)
        {
            _data.Overrides.RemoveAt(0);
        }

        // Update tool stats
        UpdateToolStats(toolName, originalDecision, userAction, safetyScore);

        await SaveAsync();

        _logger?.LogInformation("Recorded override for {Tool}: {Original} -> {UserAction} (score: {Score})",
            toolName, originalDecision, userAction, safetyScore);
    }

    public async Task RecordDecisionAsync(string toolName, int safetyScore, string decision)
    {
        if (!_data.ToolStats.TryGetValue(toolName, out var stats))
        {
            stats = new ToolThresholdStats { ToolName = toolName };
            _data.ToolStats[toolName] = stats;
        }

        stats.TotalDecisions++;

        // Running average
        stats.AverageSafetyScore =
            ((stats.AverageSafetyScore * (stats.TotalDecisions - 1)) + safetyScore) / stats.TotalDecisions;

        // Recalculate suggestion periodically
        if (stats.TotalDecisions % 10 == 0)
        {
            RecalculateSuggestedThreshold(toolName);
        }

        // Save periodically to avoid excessive IO
        if (stats.TotalDecisions % 5 == 0)
        {
            await SaveAsync();
        }
    }

    public int? GetSuggestedThreshold(string toolName)
    {
        if (_data.ToolStats.TryGetValue(toolName, out var stats) && stats.SuggestedThreshold.HasValue)
        {
            return stats.SuggestedThreshold;
        }
        return null;
    }

    public AdaptiveThresholdData GetData() => _data;

    public Dictionary<string, ToolThresholdStats> GetToolStats() => _data.ToolStats;

    public List<ThresholdOverride> GetRecentOverrides(int count = 20)
    {
        return _data.Overrides.TakeLast(count).Reverse().ToList();
    }

    private void UpdateToolStats(string toolName, string originalDecision, string userAction, int safetyScore)
    {
        if (!_data.ToolStats.TryGetValue(toolName, out var stats))
        {
            stats = new ToolThresholdStats { ToolName = toolName };
            _data.ToolStats[toolName] = stats;
        }

        stats.OverrideCount++;

        // False positive: system denied but user approved (threshold too high)
        if (originalDecision == "denied" && userAction == "approved")
        {
            stats.FalsePositives++;
        }
        // False negative: system approved but user denied (threshold too low)
        else if (originalDecision == "auto-approved" && userAction == "denied")
        {
            stats.FalseNegatives++;
        }

        RecalculateSuggestedThreshold(toolName);
    }

    private void RecalculateSuggestedThreshold(string toolName)
    {
        if (!_data.ToolStats.TryGetValue(toolName, out var stats))
            return;

        if (stats.TotalDecisions < MinSamplesForSuggestion)
        {
            stats.ConfidenceLevel = 0;
            return;
        }

        // Get recent overrides for this tool
        var toolOverrides = _data.Overrides
            .Where(o => o.ToolName == toolName)
            .TakeLast(50)
            .ToList();

        if (toolOverrides.Count == 0)
        {
            stats.ConfidenceLevel = Math.Min(1.0, stats.TotalDecisions / 100.0);
            return;
        }

        // Calculate suggested threshold based on override patterns
        // If many false positives (user approved denied items), lower the threshold
        // If many false negatives (user denied approved items), raise the threshold
        var fpOverrides = toolOverrides.Where(o => o.OriginalDecision == "denied" && o.UserAction == "approved").ToList();
        var fnOverrides = toolOverrides.Where(o => o.OriginalDecision == "auto-approved" && o.UserAction == "denied").ToList();

        if (fpOverrides.Count > 0 || fnOverrides.Count > 0)
        {
            // Use the average safety score of false positives as the lower bound
            // and false negatives as the upper bound
            int? lowerBound = fpOverrides.Count > 0 ? (int)fpOverrides.Average(o => o.SafetyScore) : null;
            int? upperBound = fnOverrides.Count > 0 ? (int)fnOverrides.Average(o => o.SafetyScore) : null;

            if (lowerBound.HasValue && upperBound.HasValue)
            {
                // Set threshold midway between the bounds
                stats.SuggestedThreshold = (lowerBound.Value + upperBound.Value) / 2;
            }
            else if (lowerBound.HasValue)
            {
                // Too many false positives - suggest lowering threshold
                stats.SuggestedThreshold = Math.Max(50, lowerBound.Value - 5);
            }
            else if (upperBound.HasValue)
            {
                // Too many false negatives - suggest raising threshold
                stats.SuggestedThreshold = Math.Min(100, upperBound.Value + 5);
            }
        }

        // Confidence based on sample size and override ratio
        double overrideRatio = (double)stats.OverrideCount / Math.Max(1, stats.TotalDecisions);
        stats.ConfidenceLevel = Math.Min(1.0,
            (stats.TotalDecisions / 50.0) * (1.0 - Math.Min(0.5, overrideRatio)));

        _data.LastCalculated = DateTime.UtcNow;
    }

    private async Task SaveAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var dir = Path.GetDirectoryName(_dataFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_dataFilePath, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save adaptive threshold data");
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
