# Claude Observer

A local service that monitors every tool call [Claude Code](https://docs.anthropic.com/en/docs/claude-code) makes, scores it for safety using an LLM, and gives you a real-time dashboard to see exactly what's happening. Optionally, it can auto-approve or deny requests based on the safety score.

## Why

Claude Code asks for permission before running commands, reading files, or making edits. When you're deep in a task, clicking "allow" dozens of times breaks your flow. But blindly auto-approving everything is risky.

Claude Observer sits in the middle: it intercepts every permission request, runs it through a safety analysis, and either logs it silently (observe mode) or makes the approve/deny decision for you (enforce mode). Either way, you get full visibility into what Claude is doing.

## How It Works

```
Claude Code  -->  curl hook  -->  Claude Observer  -->  LLM safety analysis  -->  decision
```

Hooks are lightweight `curl` commands injected into `~/.claude/settings.json`. When Claude Code triggers a tool call, the hook sends the request to the local service. The service scores it 0-100, categorizes it (safe/cautious/risky/dangerous), and either:

- **Observe mode** (default): Logs everything, returns no opinion. Claude asks you as normal.
- **Enforce mode**: Returns approve/deny based on the safety score vs threshold.

Zero external dependencies beyond .NET and `curl`. No Python, no npm, no Docker.

## Quick Start

```bash
git clone https://github.com/shahab1363/claude-observer.git
cd claude-observer
dotnet build
dotnet run --project src/ClaudePermissionAnalyzer.Api
```

That's it. On startup the service:

1. Installs hooks into `~/.claude/settings.json` automatically
2. Starts at **http://localhost:5050** and opens the dashboard
3. Prints an in-place status line showing live event counts and latency
4. On `Ctrl+C`, removes all installed hooks cleanly

Use `--no-hooks` to skip hook installation, or `--enforce` to start in enforcement mode.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Claude CLI](https://docs.anthropic.com/en/docs/claude-code) installed and authenticated
- `curl` (ships with Windows 10+, macOS, and Linux)

## Dashboard

The web UI at `http://localhost:5050` has 7 pages:

| Page | What it shows |
|------|---------------|
| **Dashboard** | Live stats, safety score charts, permission profiles, quick actions, hook controls |
| **Live Logs** | Every hook event with multi-select filter chips, LLM reasoning, response JSON, export to CSV/JSON |
| **Sessions** | Per-session timelines with event breakdowns and filter chips |
| **Transcripts** | Browse Claude's conversation transcripts with SSE live streaming, markdown rendering |
| **Prompt Editor** | Edit the LLM prompt templates that drive safety analysis |
| **Configuration** | Hook handler management: matchers, modes, thresholds, prompt templates |
| **Claude Settings** | Direct JSON editor for `~/.claude/settings.json` |

## Configuration

Config lives at `~/.claude-permission-analyzer/config.json` (auto-created on first run).

```json
{
  "llm": {
    "provider": "claude-cli",
    "model": "sonnet",
    "timeout": 30000,
    "persistentProcess": true
  },
  "server": { "port": 5050, "host": "localhost" },
  "enforcementEnabled": false,
  "hookHandlers": {
    "PermissionRequest": {
      "enabled": true,
      "handlers": [{
        "name": "bash-analyzer",
        "matcher": "Bash",
        "mode": "llm-analysis",
        "promptTemplate": "bash-prompt.txt",
        "threshold": 95,
        "autoApprove": true
      }]
    }
  }
}
```

**Key settings:**

| Setting | What it does |
|---------|-------------|
| `enforcementEnabled` | `false` = observe only, `true` = auto-approve/deny |
| `hookHandlers` | Which tools get analyzed, with what prompt, at what threshold |
| `profiles.activeProfile` | `permissive` / `moderate` / `strict` / `lockdown` |
| `llm.persistentProcess` | Keep a single Claude subprocess alive for faster responses |

## CLI Flags

```bash
dotnet run --project src/ClaudePermissionAnalyzer.Api             # Start with hooks, observe mode
dotnet run --project src/ClaudePermissionAnalyzer.Api -- --enforce # Start with enforcement enabled
dotnet run --project src/ClaudePermissionAnalyzer.Api -- --no-hooks # Start without installing hooks
```

## How Hooks Work

The service writes `curl` commands into `~/.claude/settings.json` tagged with a `# claude-analyzer` marker:

```json
{
  "hooks": {
    "PreToolUse": [{
      "matcher": "Bash",
      "hooks": [{
        "type": "command",
        "command": "curl -sS -X POST \"http://localhost:5050/api/hooks/claude?event=PreToolUse\" -H \"Content-Type: application/json\" -d @- # claude-analyzer"
      }]
    }]
  }
}
```

On shutdown, only hooks with the `# claude-analyzer` marker are removed. Your own hooks are never touched.

## Security

- Localhost-only binding with CORS restrictions
- Security headers (CSP, X-Frame-Options, no-cache)
- Rate limiting (600 req/min per IP)
- Optional API key authentication
- Input sanitization and path traversal protection
- Any hook error returns `{}` (no opinion) so Claude falls through to normal behavior

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | C# .NET 10, ASP.NET Core |
| Hook Transport | `curl` |
| Frontend | Vanilla HTML/CSS/JS (no CDN dependencies) |
| LLM | Claude CLI subprocess |
| Storage | JSON files + MemoryCache |
| Tests | xUnit + Moq (143 tests) |

## Development

```bash
dotnet build    # Build
dotnet test     # Run 143 tests
```

## Releases

CI builds self-contained binaries for Windows x64, Linux x64, macOS x64, and macOS ARM64. See the [Releases](https://github.com/shahab1363/claude-observer/releases) page.
