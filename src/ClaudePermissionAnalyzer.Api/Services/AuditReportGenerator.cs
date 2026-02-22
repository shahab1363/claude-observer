using ClaudePermissionAnalyzer.Api.Models;
using System.Text;
using System.Web;

namespace ClaudePermissionAnalyzer.Api.Services;

public class AuditReportGenerator
{
    private readonly SessionManager _sessionManager;
    private readonly AdaptiveThresholdService _adaptiveService;
    private readonly ProfileService _profileService;

    public AuditReportGenerator(
        SessionManager sessionManager,
        AdaptiveThresholdService adaptiveService,
        ProfileService profileService)
    {
        _sessionManager = sessionManager;
        _adaptiveService = adaptiveService;
        _profileService = profileService;
    }

    public async Task<AuditReport> GenerateReportAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _sessionManager.GetOrCreateSessionAsync(sessionId, cancellationToken);
        var toolStats = _adaptiveService.GetToolStats();
        var profile = _profileService.GetActiveProfile();

        var events = session.ConversationHistory;
        var totalDecisions = events.Count;
        var approved = events.Count(e => e.Decision == "auto-approved");
        var denied = events.Count(e => e.Decision == "denied");
        var noHandler = events.Count(e => e.Decision == "no-handler");

        var riskDistribution = events
            .Where(e => !string.IsNullOrEmpty(e.Category))
            .GroupBy(e => e.Category!)
            .ToDictionary(g => g.Key, g => g.Count());

        var topFlaggedOps = events
            .Where(e => e.SafetyScore.HasValue && e.SafetyScore < 80)
            .OrderBy(e => e.SafetyScore)
            .Take(10)
            .Select(e => new FlaggedOperation
            {
                ToolName = e.ToolName ?? "unknown",
                SafetyScore = e.SafetyScore ?? 0,
                Category = e.Category ?? "unknown",
                Timestamp = e.Timestamp,
                Decision = e.Decision ?? "unknown",
                Reasoning = e.Reasoning ?? string.Empty
            })
            .ToList();

        var toolBreakdown = events
            .Where(e => !string.IsNullOrEmpty(e.ToolName))
            .GroupBy(e => e.ToolName!)
            .Select(g => new ToolBreakdown
            {
                ToolName = g.Key,
                TotalRequests = g.Count(),
                Approved = g.Count(e => e.Decision == "auto-approved"),
                Denied = g.Count(e => e.Decision == "denied"),
                AverageSafetyScore = g.Where(e => e.SafetyScore.HasValue).Select(e => e.SafetyScore!.Value).DefaultIfEmpty(0).Average()
            })
            .OrderByDescending(t => t.TotalRequests)
            .ToList();

        var avgScore = events.Where(e => e.SafetyScore.HasValue).Select(e => e.SafetyScore!.Value).DefaultIfEmpty(0).Average();

        return new AuditReport
        {
            SessionId = sessionId,
            GeneratedAt = DateTime.UtcNow,
            SessionStart = session.StartTime,
            SessionLastActivity = session.LastActivity,
            ActiveProfile = profile.Name,
            TotalDecisions = totalDecisions,
            Approved = approved,
            Denied = denied,
            NoHandler = noHandler,
            AverageSafetyScore = Math.Round(avgScore, 1),
            RiskDistribution = riskDistribution,
            TopFlaggedOperations = topFlaggedOps,
            ToolBreakdown = toolBreakdown
        };
    }

    public string RenderHtml(AuditReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("<title>Audit Report - " + Encode(report.SessionId) + "</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(GetReportStyles());
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<div class=\"report\">");

        // Header
        sb.AppendLine("<header class=\"report-header\">");
        sb.AppendLine("<h1>Permission Audit Report</h1>");
        sb.AppendLine($"<p class=\"subtitle\">Session: {Encode(report.SessionId)}</p>");
        sb.AppendLine($"<p class=\"meta\">Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC | Profile: {Encode(report.ActiveProfile)}</p>");
        sb.AppendLine("</header>");

        // Summary cards
        sb.AppendLine("<section class=\"summary-grid\">");
        RenderSummaryCard(sb, "Total Decisions", report.TotalDecisions.ToString(), "#1976D2");
        RenderSummaryCard(sb, "Auto-Approved", report.Approved.ToString(), "#4CAF50");
        RenderSummaryCard(sb, "Denied", report.Denied.ToString(), "#f44336");
        RenderSummaryCard(sb, "Avg Safety Score", report.AverageSafetyScore.ToString("F1"), GetScoreColor(report.AverageSafetyScore));
        sb.AppendLine("</section>");

        // Risk distribution
        if (report.RiskDistribution.Count > 0)
        {
            sb.AppendLine("<section class=\"section\">");
            sb.AppendLine("<h2>Risk Distribution</h2>");
            sb.AppendLine("<div class=\"risk-bars\">");
            var total = report.RiskDistribution.Values.Sum();
            foreach (var (category, count) in report.RiskDistribution.OrderByDescending(r => r.Value))
            {
                var pct = total > 0 ? (count * 100.0 / total) : 0;
                var color = GetCategoryColor(category);
                sb.AppendLine($"<div class=\"risk-bar-row\">");
                sb.AppendLine($"<span class=\"risk-label\">{Encode(category)}</span>");
                sb.AppendLine($"<div class=\"risk-bar-track\"><div class=\"risk-bar-fill\" style=\"width:{pct:F1}%;background:{color}\"></div></div>");
                sb.AppendLine($"<span class=\"risk-count\">{count} ({pct:F0}%)</span>");
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div></section>");
        }

        // Tool breakdown table
        if (report.ToolBreakdown.Count > 0)
        {
            sb.AppendLine("<section class=\"section\">");
            sb.AppendLine("<h2>Tool Breakdown</h2>");
            sb.AppendLine("<table><thead><tr>");
            sb.AppendLine("<th>Tool</th><th>Requests</th><th>Approved</th><th>Denied</th><th>Avg Score</th>");
            sb.AppendLine("</tr></thead><tbody>");
            foreach (var tool in report.ToolBreakdown)
            {
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td>{Encode(tool.ToolName)}</td>");
                sb.AppendLine($"<td>{tool.TotalRequests}</td>");
                sb.AppendLine($"<td class=\"approved\">{tool.Approved}</td>");
                sb.AppendLine($"<td class=\"denied\">{tool.Denied}</td>");
                sb.AppendLine($"<td style=\"color:{GetScoreColor(tool.AverageSafetyScore)}\">{tool.AverageSafetyScore:F1}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table></section>");
        }

        // Top flagged operations
        if (report.TopFlaggedOperations.Count > 0)
        {
            sb.AppendLine("<section class=\"section\">");
            sb.AppendLine("<h2>Top Flagged Operations</h2>");
            sb.AppendLine("<div class=\"flagged-list\">");
            foreach (var op in report.TopFlaggedOperations)
            {
                sb.AppendLine("<div class=\"flagged-item\">");
                sb.AppendLine($"<div class=\"flagged-header\">");
                sb.AppendLine($"<span class=\"tool-name\">{Encode(op.ToolName)}</span>");
                sb.AppendLine($"<span class=\"score\" style=\"color:{GetScoreColor(op.SafetyScore)}\">{op.SafetyScore}</span>");
                sb.AppendLine($"<span class=\"category category-{Encode(op.Category)}\">{Encode(op.Category)}</span>");
                sb.AppendLine($"<span class=\"timestamp\">{op.Timestamp:HH:mm:ss}</span>");
                sb.AppendLine("</div>");
                if (!string.IsNullOrEmpty(op.Reasoning))
                {
                    sb.AppendLine($"<p class=\"reasoning\">{Encode(op.Reasoning)}</p>");
                }
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div></section>");
        }

        // Recommendations
        sb.AppendLine("<section class=\"section\">");
        sb.AppendLine("<h2>Recommendations</h2>");
        sb.AppendLine("<ul class=\"recommendations\">");
        GenerateRecommendations(sb, report);
        sb.AppendLine("</ul></section>");

        sb.AppendLine("<footer><p>Claude Permission Analyzer - Audit Report</p></footer>");
        sb.AppendLine("</div></body></html>");

        return sb.ToString();
    }

    private static void RenderSummaryCard(StringBuilder sb, string label, string value, string color)
    {
        sb.AppendLine($"<div class=\"summary-card\">");
        sb.AppendLine($"<div class=\"card-label\">{Encode(label)}</div>");
        sb.AppendLine($"<div class=\"card-value\" style=\"color:{color}\">{Encode(value)}</div>");
        sb.AppendLine("</div>");
    }

    private static void GenerateRecommendations(StringBuilder sb, AuditReport report)
    {
        if (report.TotalDecisions == 0)
        {
            sb.AppendLine("<li>No permission decisions recorded. Ensure the hook integration is working.</li>");
            return;
        }

        double approvalRate = report.TotalDecisions > 0 ? (double)report.Approved / report.TotalDecisions : 0;
        if (approvalRate > 0.95)
        {
            sb.AppendLine("<li>Very high approval rate (" + $"{approvalRate:P0}" + "). Consider switching to 'Permissive' profile to reduce friction.</li>");
        }
        else if (approvalRate < 0.5)
        {
            sb.AppendLine("<li>Low approval rate (" + $"{approvalRate:P0}" + "). Consider reviewing threshold settings or switching to 'Moderate' profile.</li>");
        }

        if (report.Denied > 10)
        {
            sb.AppendLine($"<li>{report.Denied} operations were denied. Review denied operations for false positives.</li>");
        }

        foreach (var tool in report.ToolBreakdown.Where(t => t.Denied > 0 && t.AverageSafetyScore > 80))
        {
            sb.AppendLine($"<li>{Encode(tool.ToolName)} has denials despite high average score ({tool.AverageSafetyScore:F0}). Consider lowering its threshold.</li>");
        }

        if (report.TopFlaggedOperations.Any(o => o.Category == "dangerous"))
        {
            sb.AppendLine("<li>Dangerous operations were detected. Review these carefully and consider stricter controls.</li>");
        }
    }

    private static string GetScoreColor(double score) => score switch
    {
        >= 90 => "#4CAF50",
        >= 70 => "#FF9800",
        >= 50 => "#f44336",
        _ => "#d32f2f"
    };

    private static string GetCategoryColor(string category) => category switch
    {
        "safe" => "#4CAF50",
        "cautious" => "#FF9800",
        "risky" => "#f44336",
        "dangerous" => "#d32f2f",
        _ => "#9E9E9E"
    };

    private static string Encode(string value) => HttpUtility.HtmlEncode(value);

    private static string GetReportStyles() => """
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #f0f2f5; color: #333; }
        .report { max-width: 900px; margin: 20px auto; background: white; border-radius: 12px; box-shadow: 0 4px 12px rgba(0,0,0,0.1); overflow: hidden; }
        .report-header { background: linear-gradient(135deg, #1976D2, #1565C0); color: white; padding: 32px; }
        .report-header h1 { font-size: 28px; margin-bottom: 8px; }
        .subtitle { font-size: 16px; opacity: 0.9; }
        .meta { font-size: 12px; opacity: 0.7; margin-top: 8px; }
        .summary-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1px; background: #e0e0e0; }
        .summary-card { background: white; padding: 24px; text-align: center; }
        .card-label { font-size: 12px; color: #666; margin-bottom: 8px; text-transform: uppercase; letter-spacing: 0.5px; }
        .card-value { font-size: 32px; font-weight: bold; }
        .section { padding: 24px 32px; border-top: 1px solid #e0e0e0; }
        .section h2 { font-size: 18px; margin-bottom: 16px; color: #1a1a1a; }
        table { width: 100%; border-collapse: collapse; }
        th { text-align: left; padding: 10px 12px; background: #f5f5f5; border-bottom: 2px solid #e0e0e0; font-size: 13px; color: #666; }
        td { padding: 10px 12px; border-bottom: 1px solid #f0f0f0; font-size: 14px; }
        td.approved { color: #4CAF50; font-weight: 600; }
        td.denied { color: #f44336; font-weight: 600; }
        .risk-bars { display: flex; flex-direction: column; gap: 8px; }
        .risk-bar-row { display: flex; align-items: center; gap: 12px; }
        .risk-label { width: 80px; font-size: 13px; font-weight: 600; text-transform: capitalize; }
        .risk-bar-track { flex: 1; height: 24px; background: #f0f0f0; border-radius: 12px; overflow: hidden; }
        .risk-bar-fill { height: 100%; border-radius: 12px; transition: width 0.3s; }
        .risk-count { width: 80px; font-size: 13px; color: #666; text-align: right; }
        .flagged-list { display: flex; flex-direction: column; gap: 8px; }
        .flagged-item { padding: 12px; background: #fafafa; border-radius: 8px; border-left: 3px solid #f44336; }
        .flagged-header { display: flex; align-items: center; gap: 12px; }
        .tool-name { font-weight: 600; }
        .score { font-weight: bold; font-size: 18px; }
        .category { font-size: 11px; padding: 2px 8px; border-radius: 10px; background: #f0f0f0; }
        .category-safe { background: #e8f5e9; color: #2e7d32; }
        .category-cautious { background: #fff3e0; color: #e65100; }
        .category-risky { background: #fce4ec; color: #c62828; }
        .category-dangerous { background: #f44336; color: white; }
        .timestamp { font-size: 12px; color: #999; margin-left: auto; }
        .reasoning { font-size: 13px; color: #666; margin-top: 6px; }
        .recommendations { padding-left: 20px; }
        .recommendations li { padding: 8px 0; line-height: 1.5; color: #555; }
        footer { padding: 16px 32px; text-align: center; color: #999; font-size: 12px; border-top: 1px solid #e0e0e0; }
        @media (max-width: 700px) { .summary-grid { grid-template-columns: repeat(2, 1fr); } }
        """;
}

public class AuditReport
{
    public string SessionId { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public DateTime SessionStart { get; set; }
    public DateTime SessionLastActivity { get; set; }
    public string ActiveProfile { get; set; } = string.Empty;
    public int TotalDecisions { get; set; }
    public int Approved { get; set; }
    public int Denied { get; set; }
    public int NoHandler { get; set; }
    public double AverageSafetyScore { get; set; }
    public Dictionary<string, int> RiskDistribution { get; set; } = new();
    public List<FlaggedOperation> TopFlaggedOperations { get; set; } = new();
    public List<ToolBreakdown> ToolBreakdown { get; set; } = new();
}

public class FlaggedOperation
{
    public string ToolName { get; set; } = string.Empty;
    public int SafetyScore { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}

public class ToolBreakdown
{
    public string ToolName { get; set; } = string.Empty;
    public int TotalRequests { get; set; }
    public int Approved { get; set; }
    public int Denied { get; set; }
    public double AverageSafetyScore { get; set; }
}
