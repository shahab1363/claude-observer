# Claude Permission Analyzer - Project Instructions

## CRITICAL: Read Before Doing Anything

**Always read `HANDOFF.md` first** before starting any work. It contains the complete project state, architecture, known issues, security decisions, and next steps. This file is gitignored (local to each developer).

## Project Overview

This is the **Claude Permission Analyzer** - a C# ASP.NET Core (.NET 10) service that auto-approves Claude Code permission requests using LLM-based safety analysis. It has a web dashboard, Python hook scripts, and session tracking.

## Quick Commands

```bash
dotnet build                                           # Build
dotnet test                                            # Run 107 tests
dotnet run --project src/ClaudePermissionAnalyzer.Api  # Run (http://localhost:5050)
```

## Key Conventions

- **Language:** C# with nullable reference types enabled, implicit usings
- **Framework:** ASP.NET Core with Controllers (not Minimal APIs for endpoints)
- **Frontend:** Vanilla HTML/CSS/JS only - NO external CDN dependencies
- **Storage:** JSON files in `~/.claude-permission-analyzer/`
- **Testing:** xUnit + Moq, tests mirror src structure
- **Python hooks:** Minimal dependencies (only `requests` package)
- **Security:** All new endpoints must go through the middleware pipeline (SecurityHeaders -> RateLimiting -> ApiKeyAuth)
- **Error handling:** Use `StorageException` and `ConfigurationException` custom types, not bare `Exception`
- **Thread safety:** SessionManager uses per-session SemaphoreSlim locks - maintain this pattern
- **Config:** Auto-created at `~/.claude-permission-analyzer/config.json` on first run

## File Layout

- `src/ClaudePermissionAnalyzer.Api/` - Main application
- `tests/ClaudePermissionAnalyzer.Tests/` - Test project
- `hooks/` - Python hook scripts for Claude Code integration
- `docs/plans/` - Design documents
- `prompts/` - LLM prompt templates
- `HANDOFF.md` - **Local project state and handoff details** (gitignored)

## Important Architectural Notes

1. **Program.cs middleware order matters** - SecurityHeaders -> RateLimiting -> ApiKeyAuth -> ResponseCompression -> ExceptionHandler
2. **ConfigurationManager is registered twice** in DI - once for bootstrap (no logger), once for runtime (with logger). This is intentional.
3. **HandlerConfig.SetLogger** uses a static logger pattern because HandlerConfig instances are deserialized from JSON (not created by DI).
4. **Python hooks exit with code 1 on errors** - this is critical for security. Exit code 0 means "no opinion" to Claude Code.
5. **All file paths must be validated** - use InputSanitizer for session IDs, check for path traversal.

## Do NOT

- Add external CDN dependencies to the web UI
- Expose the service on non-localhost addresses
- Remove the middleware pipeline ordering
- Use `Console.WriteLine` - use `ILogger` via DI
- Catch broad `Exception` without rethrowing or using specific types
- Skip `dotnet build` and `dotnet test` after changes
