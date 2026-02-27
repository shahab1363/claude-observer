# Claude Observer - Project Instructions

## CRITICAL: Read Before Doing Anything

**Always read `HANDOFF.md` first** if it exists -- it contains the latest local project state, in-progress work, and session-specific context. This file is gitignored (local to each developer).

## Project Overview

This is **Claude Observer** -- a C# ASP.NET Core (.NET 10) service that observes and optionally enforces Claude Code permission requests using LLM-based safety analysis. It has a web dashboard, curl-based hooks, and session tracking.

**Core flow:** Claude Code -> `curl` hook command -> `POST /api/hooks/claude` -> C# ASP.NET Core service -> LLM CLI analysis -> approve/deny/passthrough -> Claude-formatted JSON response

**Default mode:** Observe-only. Hooks log events but return no decision (Claude asks user as normal). Enforcement mode can be toggled from the dashboard or via `--enforce` CLI flag.

## Quick Commands

```bash
dotnet build                                                           # Build
dotnet test                                                            # Run tests
dotnet run --project src/ClaudePermissionAnalyzer.Api                  # Run (interactive prompt)
dotnet run --project src/ClaudePermissionAnalyzer.Api -- --install-hooks          # Auto-install hooks
dotnet run --project src/ClaudePermissionAnalyzer.Api -- --install-hooks --enforce # Install + enforce
dotnet run --project src/ClaudePermissionAnalyzer.Api -- --no-hooks               # Skip hooks
```

**On startup:** loads config -> prompts to install hooks -> starts at `http://localhost:5050` -> opens browser -> on Ctrl+C removes hooks

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | C# .NET 10, ASP.NET Core Controllers |
| Hook Commands | `curl` (zero dependencies) |
| Frontend | Vanilla HTML/CSS/JS (zero external dependencies) |
| LLM Integration | Claude CLI subprocess (`claude` command) |
| Storage | JSON files on disk + in-memory MemoryCache |
| Tests | xUnit + Moq |

## Project Structure

```
ClaudeObserver/
├── src/ClaudePermissionAnalyzer.Api/
│   ├── Program.cs                          # Entry point: CLI args, DI, middleware, browser launch, hook install
│   ├── Controllers/                        # 18 API controllers
│   │   ├── ClaudeHookController.cs         # POST /api/hooks/claude?event={type} - main curl hook endpoint
│   │   ├── HooksController.cs              # GET/POST /api/hooks/* - install/uninstall/enforce/status
│   │   ├── ClaudeSettingsController.cs     # GET/PUT /api/claude-settings - view/edit ~/.claude/settings.json
│   │   ├── CopilotHookController.cs        # POST /api/hooks/copilot - Copilot hook endpoint
│   │   ├── DashboardController.cs          # GET /api/dashboard/* - stats, sessions, activity
│   │   ├── ConfigController.cs             # GET/PUT /api/config - config CRUD
│   │   ├── DebugController.cs              # POST /api/debug/llm - LLM replay/debug endpoint
│   │   ├── HealthController.cs             # GET /health, /api/health
│   │   ├── LogsController.cs              # GET/DELETE /api/logs - logs with multi-value filters, clear, export
│   │   ├── SessionsController.cs           # GET /api/sessions/{id} - session details
│   │   ├── PromptsController.cs            # GET/PUT /api/prompts/* - prompt templates
│   │   ├── ClaudeLogsController.cs         # GET /api/claude-logs/* - transcript browsing + SSE stream
│   │   ├── TerminalController.cs           # GET /api/terminal/stream - SSE terminal output
│   │   ├── ProfileController.cs            # GET/POST /api/profile/* - permission profiles
│   │   ├── AdaptiveThresholdController.cs  # GET/POST /api/adaptivethreshold/*
│   │   ├── InsightsController.cs           # GET /api/insights
│   │   ├── QuickActionsController.cs       # POST /api/quickactions/*
│   │   └── AuditReportController.cs        # GET /api/auditreport/*
│   ├── Handlers/                           # 5 hook handler implementations
│   │   ├── IHookHandler.cs                 # Handler interface
│   │   ├── LLMAnalysisHandler.cs           # Queries LLM, returns score + decision
│   │   ├── LogOnlyHandler.cs               # Logs event, returns no decision
│   │   ├── ContextInjectionHandler.cs      # Injects context (git, errors)
│   │   └── CustomLogicHandler.cs           # Handles SessionStart/End events
│   ├── Middleware/                          # 3 security middleware
│   ├── Models/                             # 8 domain models
│   ├── Security/                           # Input sanitization
│   ├── Services/                           # 23 business logic services
│   │   ├── SessionManager.cs               # Session CRUD + ClearAllSessionsAsync
│   │   ├── ConfigurationManager.cs         # Config load/save
│   │   ├── ClaudeCliClient.cs              # ILLMClient - one-shot claude subprocess
│   │   ├── PersistentClaudeClient.cs       # ILLMClient - persistent claude process
│   │   ├── AnthropicApiClient.cs           # ILLMClient - direct API client
│   │   ├── CopilotCliClient.cs             # ILLMClient - Copilot CLI client
│   │   ├── GenericRestClient.cs            # ILLMClient - generic REST LLM client
│   │   ├── LLMClientBase.cs               # Shared base for LLM clients
│   │   ├── LLMClientProvider.cs            # Factory for LLM client selection
│   │   ├── CliProcessRunner.cs             # Shared CLI process execution
│   │   ├── HookHandlerFactory.cs           # Creates handler by mode string
│   │   ├── PromptTemplateService.cs        # Template CRUD with hot-reload
│   │   ├── PromptBuilder.cs                # Builds LLM prompts from templates
│   │   ├── TranscriptWatcher.cs            # Monitors ~/.claude/projects/ + SSE events
│   │   ├── TerminalOutputService.cs        # SSE terminal output stream
│   │   ├── ProfileService.cs               # Permission profile switching
│   │   ├── AdaptiveThresholdService.cs     # Learns from user overrides
│   │   ├── InsightsEngine.cs               # Smart suggestions
│   │   ├── AuditReportGenerator.cs         # HTML/JSON audit reports
│   │   ├── EnforcementService.cs           # Observe/enforce toggle, persisted to config
│   │   ├── HookInstaller.cs               # Install/uninstall curl hooks in ~/.claude/settings.json
│   │   ├── CopilotHookInstaller.cs        # Copilot hook installation
│   │   └── ILLMClient.cs                   # Interface + LLMResponse model
│   ├── Exceptions/
│   └── wwwroot/                            # Web UI (8 pages)
│       ├── index.html                      # Dashboard
│       ├── logs.html                       # Live logs with multi-select filter chips
│       ├── config.html                     # Service config + hook handler management
│       ├── session.html                    # Session list + detail timeline
│       ├── transcripts.html                # Transcript browser with SSE
│       ├── prompts.html                    # Prompt template editor
│       ├── claude-settings.html            # JSON editor for ~/.claude/settings.json
│       ├── css/styles.css                  # Full styling with dark mode, responsive
│       └── js/
│           ├── dashboard.js                # Dashboard logic, charts, profiles, quick actions
│           ├── shared.js                   # Dark mode, toasts, shortcuts, connection status
│           ├── config.js                   # Config page + hook handler management
│           └── logs.js                     # Logs: chip filters, incremental updates, auto-refresh
├── prompts/                                # LLM prompt templates (9 files)
├── tests/ClaudePermissionAnalyzer.Tests/   # xUnit tests
├── .github/workflows/ci.yml               # CI/CD pipeline
├── HANDOFF.md                              # Local project state (gitignored)
└── ClaudePermissionAnalyzer.sln
```

## Hook Architecture

### How Hooks Work (curl-based, zero dependencies)

HookInstaller writes to `~/.claude/settings.json`:
```json
{
  "hooks": {
    "PermissionRequest": [{
      "matcher": "Bash",
      "hooks": [{ "type": "command", "command": "curl -sS -X POST \"http://localhost:5050/api/hooks/claude?event=PermissionRequest\" -H \"Content-Type: application/json\" -d @- # claude-analyzer" }]
    }]
  }
}
```

**Flow:** Claude Code -> stdin JSON -> `curl` -> service analyzes -> Claude-formatted JSON -> stdout -> Claude reads decision.

The `# claude-analyzer` comment is a marker for clean uninstall (only removes our hooks, not user's).

### Passthrough Tools

Non-actionable tools like `AskUserQuestion` are in the `PassthroughTools` set in `ClaudeHookController`. These are logged but always return `{}` (no opinion) regardless of enforcement mode. To add more, edit the `PassthroughTools` HashSet.

### Config -> Hooks Auto-Sync

When hook handler config is saved via the Configuration page, hooks in `~/.claude/settings.json` are automatically reinstalled to stay in sync.

### Enforcement Modes

| Mode | Behavior |
|------|----------|
| **Observe** (default) | Logs events with LLM analysis, returns `{}` -- Claude asks user as normal |
| **Enforce** | Returns approve/deny based on LLM safety scoring |

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/hooks/claude?event={type}` | **Main hook endpoint** -- returns Claude-formatted JSON |
| GET | `/api/hooks/status` | Hook installation & enforcement status |
| POST | `/api/hooks/enforce` | Toggle enforcement on/off |
| POST | `/api/hooks/install` | Install hooks to settings.json |
| POST | `/api/hooks/uninstall` | Remove hooks from settings.json |
| GET | `/api/claude-settings` | Read ~/.claude/settings.json |
| PUT | `/api/claude-settings` | Write ~/.claude/settings.json (with JSON validation) |
| GET | `/api/logs?decision=&category=&hookType=&toolName=&sessionId=&limit=` | Filtered logs (supports comma-separated multi-values) |
| DELETE | `/api/logs` | Clear all session log files |
| GET | `/api/logs/export/{format}` | Export logs (csv/json) |
| GET | `/api/dashboard/stats` | Dashboard statistics |
| GET | `/api/dashboard/sessions` | Active sessions list |
| GET | `/api/dashboard/activity?limit=N` | Recent activity feed |
| GET/PUT | `/api/config` | Configuration CRUD |
| GET | `/health` | Health check |
| GET | `/api/sessions/{id}` | Session event history |
| GET/PUT | `/api/prompts/{name}` | Prompt template CRUD |
| GET | `/api/claude-logs/projects` | List Claude projects |
| GET | `/api/claude-logs/transcript/{id}` | Get transcript entries |
| GET | `/api/claude-logs/transcript/{id}/stream` | SSE live transcript stream |
| GET | `/api/terminal/stream` | SSE terminal output stream |
| POST | `/api/debug/llm` | Replay a log entry through LLM |
| GET | `/api/profile` | List profiles |
| POST | `/api/profile/switch` | Switch permission profile |
| GET | `/api/adaptivethreshold/stats` | Adaptive threshold stats |
| POST | `/api/adaptivethreshold/override` | Record user override |
| GET | `/api/insights` | Smart suggestions |
| POST | `/api/quickactions/{action}` | Execute quick action |
| GET | `/api/auditreport/{sessionId}` | JSON audit report |
| GET | `/api/auditreport/{sessionId}/html` | HTML audit report |

## Security Architecture

### Middleware Pipeline (order matters)
1. **SecurityHeadersMiddleware** -- CSP, X-Frame-Options, no-cache on all responses
2. **RateLimitingMiddleware** -- 600 req/min per IP
3. **ApiKeyAuthMiddleware** -- X-Api-Key header validation
4. **ResponseCompression** -- Gzip
5. **ExceptionHandler** -- Generic errors in production

### Key Measures
- Input sanitization, path traversal protection, command injection prevention
- LLM prompt injection defense, CORS localhost-only, Kestrel body limits
- Hook error safety: any error returns `{}` (no opinion)

## Configuration Reference

Config: `~/.claude-permission-analyzer/config.json` (auto-created)

```json
{
  "llm": { "provider": "claude-cli", "model": "sonnet", "timeout": 30000, "persistentProcess": true },
  "server": { "port": 5050, "host": "localhost" },
  "security": { "apiKey": null, "rateLimitPerMinute": 600 },
  "profiles": { "activeProfile": "moderate" },
  "session": { "maxHistoryPerSession": 50, "storageDir": "~/.claude-permission-analyzer/sessions" },
  "enforcementEnabled": false,
  "hookHandlers": {
    "PermissionRequest": { "enabled": true, "handlers": [
      { "name": "bash-analyzer", "matcher": "Bash", "mode": "llm-analysis", "promptTemplate": "bash-prompt.txt", "threshold": 95, "autoApprove": true }
    ]}
  }
}
```

## Key Conventions

- **Language:** C# with nullable reference types enabled, implicit usings
- **Framework:** ASP.NET Core with Controllers (not Minimal APIs for endpoints)
- **Frontend:** Vanilla HTML/CSS/JS only - NO external CDN dependencies
- **Storage:** JSON files in `~/.claude-permission-analyzer/`
- **Testing:** xUnit + Moq, tests mirror src structure
- **Security:** All new endpoints must go through the middleware pipeline (SecurityHeaders -> RateLimiting -> ApiKeyAuth)
- **Error handling:** Use `StorageException` and `ConfigurationException` custom types, not bare `Exception`
- **Thread safety:** SessionManager uses per-session SemaphoreSlim locks - maintain this pattern
- **Config:** Auto-created at `~/.claude-permission-analyzer/config.json` on first run

## Important Architectural Notes

1. **Program.cs middleware order matters** - SecurityHeaders -> RateLimiting -> ApiKeyAuth -> ResponseCompression -> ExceptionHandler
2. **ConfigurationManager is registered twice** in DI - once for bootstrap (no logger), once for runtime (with logger). This is intentional.
3. **HandlerConfig.SetLogger** uses a static logger pattern because HandlerConfig instances are deserialized from JSON (not created by DI).
4. **All file paths must be validated** - use InputSanitizer for session IDs, check for path traversal.
5. **PassthroughTools** in ClaudeHookController - tools like `AskUserQuestion` skip analysis entirely.

## Known Issues and Technical Debt

### High Priority
1. **No tests for new services** -- ClaudeHookController, HookInstaller, EnforcementService, ClaudeSettingsController
2. **CSP uses unsafe-inline** -- inline scripts in session.html, prompts.html, transcripts.html, claude-settings.html
3. **Duplicate ConfigController endpoints** -- overlapping `UpdateAsync` vs `UpdateConfigurationAsync`

### Medium Priority
4. **Cross-platform paths** -- `~/` expansion may not work perfectly on all OSes
5. **CSS file is ~2300 lines** -- could split into component files
6. **ConfigurationManager registered twice** in Program.cs (bootstrap + DI)

### Low Priority
7. **No API versioning**

## Do NOT

- Add external CDN dependencies to the web UI
- Expose the service on non-localhost addresses
- Remove the middleware pipeline ordering
- Use `Console.WriteLine` - use `ILogger` via DI
- Catch broad `Exception` without rethrowing or using specific types
- Skip `dotnet build` and `dotnet test` after changes
