using ClaudePermissionAnalyzer.Api.Models;
using Microsoft.Extensions.Logging;

namespace ClaudePermissionAnalyzer.Api.Services;

public class InsightsEngine
{
    private readonly AdaptiveThresholdService _adaptiveService;
    private readonly SessionManager _sessionManager;
    private readonly ILogger<InsightsEngine>? _logger;
    private readonly List<Insight> _insights = new();
    private readonly HashSet<string> _dismissedIds = new();
    private DateTime _lastGenerated = DateTime.MinValue;
    private readonly TimeSpan _regenerateInterval = TimeSpan.FromMinutes(30);

    public InsightsEngine(
        AdaptiveThresholdService adaptiveService,
        SessionManager sessionManager,
        ILogger<InsightsEngine>? logger = null)
    {
        _adaptiveService = adaptiveService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public List<Insight> GetInsights(bool includeDiscussed = false)
    {
        if (DateTime.UtcNow - _lastGenerated > _regenerateInterval)
        {
            RegenerateInsights();
        }

        return includeDiscussed
            ? _insights.ToList()
            : _insights.Where(i => !i.Dismissed && !_dismissedIds.Contains(i.Id)).ToList();
    }

    public void DismissInsight(string insightId)
    {
        _dismissedIds.Add(insightId);
        var insight = _insights.FirstOrDefault(i => i.Id == insightId);
        if (insight != null)
        {
            insight.Dismissed = true;
        }
    }

    public void RegenerateInsights()
    {
        _insights.Clear();
        var stats = _adaptiveService.GetToolStats();
        var overrides = _adaptiveService.GetRecentOverrides(100);

        GenerateHighApprovalRateInsights(stats);
        GenerateHighOverrideRateInsights(stats);
        GenerateThresholdSuggestionInsights(stats);
        GenerateUnusualPatternInsights(stats, overrides);
        GenerateSafeListCandidateInsights(stats);

        _lastGenerated = DateTime.UtcNow;
        _logger?.LogDebug("Generated {Count} insights", _insights.Count);
    }

    private void GenerateHighApprovalRateInsights(Dictionary<string, ToolThresholdStats> stats)
    {
        foreach (var (tool, toolStats) in stats)
        {
            if (toolStats.TotalDecisions < 10) continue;

            // Calculate approval rate from override data
            double approvalRate = 1.0 - ((double)toolStats.FalseNegatives / toolStats.TotalDecisions);
            if (approvalRate >= 0.95 && toolStats.TotalDecisions >= 20)
            {
                _insights.Add(new Insight
                {
                    Type = "high-approval-rate",
                    Severity = "suggestion",
                    Title = $"High approval rate for {tool}",
                    Description = $"You approved {approvalRate:P0} of {tool} operations ({toolStats.TotalDecisions} total). " +
                                  "This tool appears consistently safe in your workflow.",
                    Recommendation = $"Consider adding {tool} to a safe list or lowering its threshold to reduce prompts.",
                    ToolName = tool,
                    DataPoints = new Dictionary<string, object>
                    {
                        ["approvalRate"] = Math.Round(approvalRate * 100, 1),
                        ["totalDecisions"] = toolStats.TotalDecisions,
                        ["avgScore"] = Math.Round(toolStats.AverageSafetyScore, 1)
                    }
                });
            }
        }
    }

    private void GenerateHighOverrideRateInsights(Dictionary<string, ToolThresholdStats> stats)
    {
        foreach (var (tool, toolStats) in stats)
        {
            if (toolStats.TotalDecisions < 5 || toolStats.OverrideCount < 3) continue;

            double overrideRate = (double)toolStats.OverrideCount / toolStats.TotalDecisions;
            if (overrideRate >= 0.3)
            {
                _insights.Add(new Insight
                {
                    Type = "high-override-rate",
                    Severity = "warning",
                    Title = $"Frequent overrides for {tool}",
                    Description = $"You've overridden {overrideRate:P0} of decisions for {tool} " +
                                  $"({toolStats.OverrideCount} of {toolStats.TotalDecisions}). " +
                                  "The current threshold may not match your preferences.",
                    Recommendation = toolStats.FalsePositives > toolStats.FalseNegatives
                        ? $"Threshold appears too strict. Consider lowering it."
                        : $"Threshold appears too lenient. Consider raising it.",
                    ToolName = tool,
                    DataPoints = new Dictionary<string, object>
                    {
                        ["overrideRate"] = Math.Round(overrideRate * 100, 1),
                        ["falsePositives"] = toolStats.FalsePositives,
                        ["falseNegatives"] = toolStats.FalseNegatives
                    }
                });
            }
        }
    }

    private void GenerateThresholdSuggestionInsights(Dictionary<string, ToolThresholdStats> stats)
    {
        foreach (var (tool, toolStats) in stats)
        {
            if (!toolStats.SuggestedThreshold.HasValue || toolStats.ConfidenceLevel < 0.5) continue;

            _insights.Add(new Insight
            {
                Type = "threshold-suggestion",
                Severity = "info",
                Title = $"Optimized threshold available for {tool}",
                Description = $"Based on {toolStats.TotalDecisions} decisions and {toolStats.OverrideCount} overrides, " +
                              $"an optimal threshold of {toolStats.SuggestedThreshold} has been calculated " +
                              $"(confidence: {toolStats.ConfidenceLevel:P0}).",
                Recommendation = $"Apply the suggested threshold of {toolStats.SuggestedThreshold} for {tool} " +
                                 "to reduce manual overrides.",
                ToolName = tool,
                DataPoints = new Dictionary<string, object>
                {
                    ["suggestedThreshold"] = toolStats.SuggestedThreshold.Value,
                    ["confidence"] = Math.Round(toolStats.ConfidenceLevel, 2),
                    ["currentAvgScore"] = Math.Round(toolStats.AverageSafetyScore, 1)
                }
            });
        }
    }

    private void GenerateUnusualPatternInsights(Dictionary<string, ToolThresholdStats> stats, List<ThresholdOverride> overrides)
    {
        if (overrides.Count < 5) return;

        // Check for sudden spike in overrides for a specific tool
        var recentOverrides = overrides.Where(o => o.Timestamp > DateTime.UtcNow.AddHours(-1)).ToList();
        var toolGroups = recentOverrides.GroupBy(o => o.ToolName)
            .Where(g => g.Count() >= 3)
            .ToList();

        foreach (var group in toolGroups)
        {
            _insights.Add(new Insight
            {
                Type = "unusual-activity",
                Severity = "warning",
                Title = $"Override spike for {group.Key}",
                Description = $"There have been {group.Count()} overrides for {group.Key} in the last hour, " +
                              "which is higher than normal.",
                Recommendation = "Review recent activity to ensure this pattern is expected. " +
                                 "Consider adjusting the threshold if the current setting doesn't match your needs.",
                ToolName = group.Key,
                DataPoints = new Dictionary<string, object>
                {
                    ["recentOverrides"] = group.Count(),
                    ["timeWindow"] = "1 hour"
                }
            });
        }
    }

    private void GenerateSafeListCandidateInsights(Dictionary<string, ToolThresholdStats> stats)
    {
        foreach (var (tool, toolStats) in stats)
        {
            if (toolStats.TotalDecisions < 30) continue;
            if (toolStats.AverageSafetyScore < 90) continue;
            if (toolStats.FalseNegatives > 0) continue;

            _insights.Add(new Insight
            {
                Type = "safe-list-candidate",
                Severity = "suggestion",
                Title = $"{tool} is a safe-list candidate",
                Description = $"{tool} has a perfect track record over {toolStats.TotalDecisions} decisions " +
                              $"with an average safety score of {toolStats.AverageSafetyScore:F1}. " +
                              "No operations were ever overridden to denied.",
                Recommendation = $"Consider adding {tool} to the auto-approve safe list to eliminate prompts entirely.",
                ToolName = tool,
                DataPoints = new Dictionary<string, object>
                {
                    ["totalDecisions"] = toolStats.TotalDecisions,
                    ["avgScore"] = Math.Round(toolStats.AverageSafetyScore, 1),
                    ["falseNegatives"] = 0
                }
            });
        }
    }
}
