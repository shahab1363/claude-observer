#if WINDOWS
using System.Runtime.Versioning;
using ClaudePermissionAnalyzer.Api.Models;

namespace ClaudePermissionAnalyzer.Api.Services.Tray.Windows;

/// <summary>
/// Small borderless WinForms popup positioned near the system tray area.
/// Shows tool name, safety score, reasoning snippet, and Approve/Deny buttons.
/// Auto-closes on timeout.
/// </summary>
[SupportedOSPlatform("windows")]
public class TrayDecisionForm : System.Windows.Forms.Form
{
    public event EventHandler<TrayDecision?>? DecisionMade;

    private readonly System.Windows.Forms.Timer _timer;
    private int _remainingSeconds;

    public TrayDecisionForm(NotificationInfo info, int timeoutSeconds)
    {
        _remainingSeconds = timeoutSeconds;

        // Form setup — borderless, positioned near system tray
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
        StartPosition = System.Windows.Forms.FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new System.Drawing.Size(340, 220);
        BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
        ForeColor = System.Drawing.Color.White;

        // Position near system tray (bottom-right of screen)
        var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        Location = new System.Drawing.Point(
            screen.Right - Size.Width - 12,
            screen.Bottom - Size.Height - 12);

        // Title label
        var titleLabel = new System.Windows.Forms.Label
        {
            Text = info.Title,
            Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold),
            ForeColor = info.Level == NotificationLevel.Danger
                ? System.Drawing.Color.FromArgb(239, 68, 68)
                : System.Drawing.Color.FromArgb(251, 191, 36),
            Location = new System.Drawing.Point(12, 10),
            Size = new System.Drawing.Size(316, 22),
            AutoEllipsis = true
        };
        Controls.Add(titleLabel);

        // Tool + score
        var toolText = $"Tool: {info.ToolName ?? "unknown"}   Score: {info.SafetyScore ?? 0}";
        var toolLabel = new System.Windows.Forms.Label
        {
            Text = toolText,
            Font = new System.Drawing.Font("Segoe UI", 9),
            ForeColor = System.Drawing.Color.FromArgb(200, 200, 200),
            Location = new System.Drawing.Point(12, 36),
            Size = new System.Drawing.Size(316, 18)
        };
        Controls.Add(toolLabel);

        // Reasoning snippet
        var reasoning = info.Reasoning ?? "";
        if (reasoning.Length > 150)
            reasoning = reasoning[..147] + "...";
        var reasonLabel = new System.Windows.Forms.Label
        {
            Text = reasoning,
            Font = new System.Drawing.Font("Segoe UI", 8.5f),
            ForeColor = System.Drawing.Color.FromArgb(170, 170, 170),
            Location = new System.Drawing.Point(12, 58),
            Size = new System.Drawing.Size(316, 56),
            AutoEllipsis = true
        };
        Controls.Add(reasonLabel);

        // Countdown label
        var countdownLabel = new System.Windows.Forms.Label
        {
            Text = $"Auto-dismiss in {_remainingSeconds}s",
            Font = new System.Drawing.Font("Segoe UI", 8),
            ForeColor = System.Drawing.Color.FromArgb(130, 130, 130),
            Location = new System.Drawing.Point(12, 120),
            Size = new System.Drawing.Size(316, 16)
        };
        Controls.Add(countdownLabel);

        // Approve button
        var approveBtn = new System.Windows.Forms.Button
        {
            Text = "Approve",
            Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold),
            BackColor = System.Drawing.Color.FromArgb(34, 197, 94),
            ForeColor = System.Drawing.Color.White,
            FlatStyle = System.Windows.Forms.FlatStyle.Flat,
            Location = new System.Drawing.Point(12, 145),
            Size = new System.Drawing.Size(150, 36),
            Cursor = System.Windows.Forms.Cursors.Hand
        };
        approveBtn.FlatAppearance.BorderSize = 0;
        approveBtn.Click += (_, _) =>
        {
            DecisionMade?.Invoke(this, TrayDecision.Approve);
            Close();
        };
        Controls.Add(approveBtn);

        // Deny button
        var denyBtn = new System.Windows.Forms.Button
        {
            Text = "Deny",
            Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold),
            BackColor = System.Drawing.Color.FromArgb(239, 68, 68),
            ForeColor = System.Drawing.Color.White,
            FlatStyle = System.Windows.Forms.FlatStyle.Flat,
            Location = new System.Drawing.Point(174, 145),
            Size = new System.Drawing.Size(150, 36),
            Cursor = System.Windows.Forms.Cursors.Hand
        };
        denyBtn.FlatAppearance.BorderSize = 0;
        denyBtn.Click += (_, _) =>
        {
            DecisionMade?.Invoke(this, TrayDecision.Deny);
            Close();
        };
        Controls.Add(denyBtn);

        // Timeout timer
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            _remainingSeconds--;
            countdownLabel.Text = $"Auto-dismiss in {_remainingSeconds}s";
            if (_remainingSeconds <= 0)
            {
                _timer.Stop();
                DecisionMade?.Invoke(this, null);
                Close();
            }
        };
        _timer.Start();
    }

    protected override void OnFormClosed(System.Windows.Forms.FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
#endif
