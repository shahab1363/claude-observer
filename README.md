# Claude Observer

An intelligent permission observer and automation system for [Claude Code](https://docs.anthropic.com/en/docs/claude-code) that uses LLM-based safety analysis to monitor, log, and optionally auto-approve or deny tool permission requests.

## How It Works

```
Claude Code  -->  curl hook  -->  POST /api/hooks/claude  -->  LLM safety analysis  -->  approve/deny/passthrough
```

1. Claude Code triggers a hook event (e.g., `PermissionRequest`, `PreToolUse`)
2. A `curl` command (installed in `~/.claude/settings.json`) sends the event to the local service
3. The service evaluates the request using a Claude CLI subprocess
4. In **observe mode** (default): logs the event and returns `{}` (Claude asks user as normal)
5. In **enforce mode**: returns an approve/deny decision based on the safety score

Zero external dependencies. No Python, no npm. Just `curl` and `.NET`.

## Features

- **Observe or Enforce** -- default observe-only mode logs everything without interfering; toggle enforcement from the dashboard
- **LLM Safety Scoring** -- each tool request is scored 0-100 with category (safe/cautious/risky/dangerous) and detailed reasoning
- **Web Dashboard** -- real-time stats, session timelines, live logs with multi-select filter chips, transcript browser, prompt editor, config management
- **Session Tracking** -- contextual awareness from conversation history per session
- **Adaptive Thresholds** -- learns from user overrides to adjust scoring
- **Permission Profiles** -- switch between permissive/moderate/strict/lockdown presets
- **Audit Reports** -- exportable HTML/JSON audit reports per session
- **Passthrough Tools** -- non-actionable tools like `AskUserQuestion` are automatically skipped

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later)
- [Claude CLI](https://docs.anthropic.com/en/docs/claude-code) (`claude` command in PATH)
- `curl` (included on Windows 10+, macOS, Linux)

## Quick Start

```bash
# Clone and build
git clone https://github.com/shahab1363/claude-observer.git
cd claude-observer
dotnet build

# Run (prompts to install hooks on startup)
dotnet run --project src/ClaudePermissionAnalyzer.Api

# Or auto-install hooks
dotnet run --project src/ClaudePermissionAnalyzer.Api -- --install-hooks

# Or install hooks + enable enforcement
dotnet run --project src/ClaudePermissionAnalyzer.Api -- --install-hooks --enforce

# Skip hook installation
dotnet run --project src/ClaudePermissionAnalyzer.Api -- --no-hooks
```

On startup the service:
1. Loads config from `~/.claude-permission-analyzer/config.json` (auto-created)
2. Optionally installs curl hooks into `~/.claude/settings.json`
3. Starts at **http://localhost:5050** and opens the browser
4. On `Ctrl+C`, removes installed hooks cleanly

## Web Dashboard

| Page | URL | Description |
|------|-----|-------------|
| Dashboard | `/` | Stats, charts, profiles, insights, hooks install/enforce toggles |
| Live Logs | `/logs.html` | Multi-select filter chips, incremental updates, auto-scroll, export CSV/JSON |
| Sessions | `/session.html` | Session list, detail timeline, filter chips, live refresh |
| Transcripts | `/transcripts.html` | Project/session browser, SSE live stream, markdown rendering, export |
| Prompt Editor | `/prompts.html` | Edit LLM prompt templates used for safety analysis |
| Configuration | `/config.html` | Service config + hook handler management |
| Claude Settings | `/claude-settings.html` | JSON editor for `~/.claude/settings.json` |

## Configuration

Config file: `~/.claude-permission-analyzer/config.json` (auto-created on first run)

```json
{
  "llm": { "provider": "claude-cli", "model": "sonnet", "timeout": 30000, "persistentProcess": true },
  "server": { "port": 5050, "host": "localhost" },
  "security": { "apiKey": null, "rateLimitPerMinute": 600 },
  "profiles": { "activeProfile": "moderate" },
  "enforcementEnabled": false,
  "hookHandlers": {
    "PermissionRequest": { "enabled": true, "handlers": [
      { "name": "bash-analyzer", "matcher": "Bash", "mode": "llm-analysis", "promptTemplate": "bash-prompt.txt", "threshold": 95, "autoApprove": true }
    ]}
  }
}
```

Key settings:
- **`enforcementEnabled`** -- `false` = observe only (log but don't decide), `true` = approve/deny
- **`hookHandlers`** -- configure which tools get analyzed, with what prompt template, and at what threshold
- **`profiles.activeProfile`** -- `permissive`, `moderate`, `strict`, or `lockdown`

## Development

```bash
dotnet build    # Build
dotnet test     # Run tests (xUnit)
dotnet run --project src/ClaudePermissionAnalyzer.Api    # Run locally
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | C# .NET 10, ASP.NET Core Controllers |
| Hook Transport | `curl` (zero dependencies) |
| Frontend | Vanilla HTML/CSS/JS (no CDN dependencies) |
| LLM Integration | Claude CLI subprocess |
| Storage | JSON files on disk + in-memory MemoryCache |
| Tests | xUnit + Moq |

## Security

- Localhost-only binding with CORS restrictions
- Security headers middleware (CSP, X-Frame-Options)
- Rate limiting (600 req/min per IP)
- Optional API key authentication
- Input sanitization and path traversal protection
- Hook error safety: any error returns `{}` (no opinion, Claude asks user)

