# Claude Permission Analyzer - Design Document

**Date:** 2026-01-30
**Status:** Design Complete - Ready for Implementation

## Overview

An intelligent permission automation system for Claude Code that uses LLM-based safety analysis to automatically approve safe operations while providing comprehensive logging and session tracking.

## Architecture

### System Components

```
┌─────────────────────────────────────────────────────────┐
│                  Claude Code                            │
└───────────────────┬─────────────────────────────────────┘
                    │ Hook Events (JSON via stdin)
                    ▼
┌─────────────────────────────────────────────────────────┐
│           Python Hook Scripts                           │
│  - bash-hook.py, file-read-hook.py, etc.               │
│  - Lightweight HTTP forwarding                          │
└───────────────────┬─────────────────────────────────────┘
                    │ HTTP POST
                    ▼
┌─────────────────────────────────────────────────────────┐
│      C# Background Service (ASP.NET Core)               │
│  ┌─────────────────────────────────────────────────┐   │
│  │  Permission Analyzer Service                    │   │
│  │  - Load prompt templates                        │   │
│  │  - Build context from session history           │   │
│  │  - Invoke LLM CLI                               │   │
│  │  - Parse safety score                           │   │
│  │  - Apply threshold logic                        │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │  Session Manager                                │   │
│  │  - Track conversation history                   │   │
│  │  - Store session JSON files                     │   │
│  │  - In-memory cache for active sessions          │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │  LLM Client                                     │   │
│  │  - Execute `claude` or `gh copilot` CLI        │   │
│  │  - Handle timeouts and errors                   │   │
│  └─────────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────────┐   │
│  │  Web Server (HTTP :5050)                        │   │
│  │  - API endpoints for hooks                      │   │
│  │  - Serve web UI static files                    │   │
│  │  - SSE for real-time log streaming              │   │
│  └─────────────────────────────────────────────────┘   │
└───────────────────┬─────────────────────────────────────┘
                    │
    ┌───────────────┴────────────────┬──────────────────┐
    ▼                                ▼                  ▼
┌──────────┐                  ┌────────────┐    ┌──────────────┐
│  LLM CLI │                  │  Web UI    │    │  Tray Icon   │
│  claude  │                  │  Browser   │    │  (System)    │
│  gh      │                  └────────────┘    └──────────────┘
└──────────┘
```

### Technology Stack

- **Backend:** C# (.NET 8+), ASP.NET Core Minimal APIs
- **Hook Scripts:** Python 3.8+ (minimal dependencies: requests)
- **Frontend:** Vanilla JavaScript, modern CSS, SignalR for real-time updates
- **Storage:** JSON files (session data), in-memory cache
- **LLM Integration:** CLI subprocess execution (claude/gh CLI tools)
- **System Integration:** Windows Forms/WPF for tray icon

## Configuration

### File Structure

```
~/.claude-permission-analyzer/
├── config.json                 # Main configuration
├── ClaudePermissionAnalyzer.exe
├── hooks/                      # Python hook scripts
│   ├── bash-hook.py
│   ├── file-read-hook.py
│   ├── file-write-hook.py
│   ├── web-hook.py
│   ├── mcp-hook.py
│   ├── user-prompt-hook.py
│   └── stop-hook.py
├── prompts/                    # Customizable LLM prompts
│   ├── bash-prompt.txt
│   ├── file-read-prompt.txt
│   ├── file-write-prompt.txt
│   ├── web-prompt.txt
│   ├── mcp-prompt.txt
│   ├── post-tool-validation-prompt.txt
│   ├── failure-analysis-prompt.txt
│   └── context-injection-prompt.txt
├── sessions/                   # Session history
│   ├── abc123.json
│   ├── def456.json
│   └── ...
└── wwwroot/                    # Web UI static files
    ├── index.html
    ├── dashboard.html
    ├── config.html
    ├── js/
    └── css/
```

### Configuration Schema (config.json)

```json
{
  "llm": {
    "provider": "claude-cli",      // or "github-cli"
    "model": "sonnet",             // for claude-cli: "sonnet", "opus", "haiku"
    "timeout": 30000               // milliseconds
  },
  "server": {
    "port": 5050,
    "host": "localhost"
  },
  "hookHandlers": {
    "PermissionRequest": {
      "enabled": true,
      "handlers": [
        {
          "name": "bash-analyzer",
          "matcher": "Bash",
          "mode": "llm-analysis",
          "promptTemplate": "bash-prompt.txt",
          "threshold": 95,
          "autoApprove": true,
          "config": {
            "sendCode": false,
            "knownSafeCommands": ["git status", "npm install", "npm test", "ls", "pwd"]
          }
        },
        {
          "name": "file-read-analyzer",
          "matcher": "Read",
          "mode": "llm-analysis",
          "promptTemplate": "file-read-prompt.txt",
          "threshold": 93,
          "autoApprove": true,
          "config": {
            "sendCode": false,
            "allowSendCodeIfConfigured": true
          }
        },
        {
          "name": "file-write-analyzer",
          "matcher": "Write|Edit",
          "mode": "llm-analysis",
          "promptTemplate": "file-write-prompt.txt",
          "threshold": 97,
          "autoApprove": true,
          "config": {
            "sendCode": false
          }
        },
        {
          "name": "web-analyzer",
          "matcher": "WebFetch|WebSearch",
          "mode": "llm-analysis",
          "promptTemplate": "web-prompt.txt",
          "threshold": 90,
          "autoApprove": true,
          "config": {
            "knownSafeDomains": [
              "github.com",
              "*.microsoft.com",
              "npmjs.com",
              "pypi.org",
              "stackoverflow.com"
            ]
          }
        },
        {
          "name": "mcp-analyzer",
          "matcher": "mcp__.*",
          "mode": "llm-analysis",
          "promptTemplate": "mcp-prompt.txt",
          "threshold": 92,
          "autoApprove": true,
          "config": {
            "autoApproveRegistered": true
          }
        }
      ]
    },
    "PreToolUse": {
      "enabled": true,
      "handlers": [
        {
          "name": "pre-tool-logger",
          "matcher": "*",
          "mode": "log-only",
          "config": {
            "logLevel": "info"
          }
        }
      ]
    },
    "PostToolUse": {
      "enabled": true,
      "handlers": [
        {
          "name": "post-tool-validator",
          "matcher": "Write|Edit",
          "mode": "llm-validation",
          "promptTemplate": "post-tool-validation-prompt.txt",
          "config": {
            "checkForErrors": true,
            "suggestImprovements": false
          }
        },
        {
          "name": "post-tool-logger",
          "matcher": "*",
          "mode": "log-only"
        }
      ]
    },
    "PostToolUseFailure": {
      "enabled": true,
      "handlers": [
        {
          "name": "failure-analyzer",
          "matcher": "*",
          "mode": "llm-analysis",
          "promptTemplate": "failure-analysis-prompt.txt",
          "config": {
            "suggestFixes": true,
            "addContextToSession": true
          }
        }
      ]
    },
    "UserPromptSubmit": {
      "enabled": true,
      "handlers": [
        {
          "name": "prompt-logger",
          "matcher": null,
          "mode": "log-only"
        },
        {
          "name": "context-injector",
          "matcher": null,
          "mode": "context-injection",
          "promptTemplate": "context-injection-prompt.txt",
          "config": {
            "injectGitBranch": true,
            "injectRecentErrors": true
          }
        }
      ]
    },
    "Stop": {
      "enabled": true,
      "handlers": [
        {
          "name": "stop-logger",
          "matcher": null,
          "mode": "log-only"
        }
      ]
    },
    "SubagentStart": {
      "enabled": true,
      "handlers": [
        {
          "name": "subagent-logger",
          "matcher": null,
          "mode": "log-only"
        }
      ]
    },
    "SubagentStop": {
      "enabled": true,
      "handlers": [
        {
          "name": "subagent-completion-logger",
          "matcher": null,
          "mode": "log-only"
        }
      ]
    },
    "SessionStart": {
      "enabled": true,
      "handlers": [
        {
          "name": "session-initializer",
          "matcher": null,
          "mode": "custom-logic",
          "config": {
            "loadProjectContext": true,
            "checkGitStatus": true
          }
        }
      ]
    },
    "SessionEnd": {
      "enabled": true,
      "handlers": [
        {
          "name": "session-cleanup",
          "matcher": null,
          "mode": "custom-logic",
          "config": {
            "archiveSession": true,
            "generateSummary": false
          }
        }
      ]
    },
    "PreCompact": {
      "enabled": true,
      "handlers": [
        {
          "name": "compact-logger",
          "matcher": null,
          "mode": "log-only"
        }
      ]
    },
    "Setup": {
      "enabled": true,
      "handlers": [
        {
          "name": "setup-logger",
          "matcher": null,
          "mode": "log-only"
        }
      ]
    },
    "Notification": {
      "enabled": true,
      "handlers": [
        {
          "name": "notification-logger",
          "matcher": "*",
          "mode": "log-only"
        }
      ]
    }
  },
  "session": {
    "maxHistoryPerSession": 50,
    "storageDir": "~/.claude-permission-analyzer/sessions"
  }
}
```

### Handler Modes

1. **`llm-analysis`** - Send to LLM for safety scoring, auto-approve if threshold met
2. **`llm-validation`** - Send to LLM for validation/suggestions (PostToolUse)
3. **`context-injection`** - Use LLM to generate additional context
4. **`log-only`** - Just log the event, no LLM call
5. **`custom-logic`** - Run custom C# handler class

## Prompt Templates

### Bash Command Analysis (bash-prompt.txt)

```
You are a security analyzer evaluating whether a Bash command should be auto-approved.

COMMAND: {COMMAND}
DESCRIPTION: {DESCRIPTION}
WORKING DIR: {CWD}
SESSION HISTORY: {SESSION_HISTORY}

SAFETY CRITERIA (use as guidance):
- Known safe commands (git status, npm install, ls, etc.): +40 points
- Operations within workspace directory: +30 points
- Read-only commands (ls, cat, git status): +20 points
- Commands with destructive potential (rm, mv, >): -30 points
- Network operations to unknown hosts: -20 points
- Session context (similar commands recently approved): +10 points

KNOWN SAFE COMMANDS: {KNOWN_SAFE_COMMANDS}
WORKSPACE: {WORKSPACE}

Rate the safety of this command on a scale of 0-100.
Respond ONLY with valid JSON:
{
  "safetyScore": <number 0-100>,
  "reasoning": "<brief explanation>",
  "category": "<safe|cautious|risky|dangerous>"
}
```

### File Read Analysis (file-read-prompt.txt)

```
You are a security analyzer evaluating whether a file read operation should be auto-approved.

FILE PATH: {FILE_PATH}
WORKING DIR: {CWD}
SESSION HISTORY: {SESSION_HISTORY}

SAFETY CRITERIA (use as guidance):
- Reading from workspace directory: +40 points
- Common config/doc files (.md, .json, .txt): +30 points
- Source code files: +20 points
- Reading sensitive files (.env, credentials, keys): -50 points
- Path traversal attempts (../, absolute paths outside workspace): -40 points
- Recently read similar files: +10 points

WORKSPACE: {WORKSPACE}

Rate the safety of this file read on a scale of 0-100.
Respond ONLY with valid JSON:
{
  "safetyScore": <number 0-100>,
  "reasoning": "<brief explanation>",
  "category": "<safe|cautious|risky|dangerous>"
}
```

### File Write/Edit Analysis (file-write-prompt.txt)

```
You are a security analyzer evaluating whether a file write/edit operation should be auto-approved.

OPERATION: {OPERATION}
FILE PATH: {FILE_PATH}
WORKING DIR: {CWD}
SESSION HISTORY: {SESSION_HISTORY}

SAFETY CRITERIA (use as guidance):
- Writing to workspace directory: +30 points
- Creating/updating source code: +20 points
- Editing config files: +15 points
- Writing to system directories: -50 points
- Overwriting critical files (.git/config, package.json): -30 points
- Path traversal attempts: -40 points
- Writing to sensitive files (.env, credentials): -50 points
- Recently performed similar writes: +10 points

WORKSPACE: {WORKSPACE}

Rate the safety of this file operation on a scale of 0-100.
Respond ONLY with valid JSON:
{
  "safetyScore": <number 0-100>,
  "reasoning": "<brief explanation>",
  "category": "<safe|cautious|risky|dangerous>"
}
```

### Web Operation Analysis (web-prompt.txt)

```
You are a security analyzer evaluating whether a web operation should be auto-approved.

OPERATION: {OPERATION}
URL: {URL}
WORKING DIR: {CWD}
SESSION HISTORY: {SESSION_HISTORY}

SAFETY CRITERIA (use as guidance):
- Known safe domains (github.com, microsoft.com, npmjs.com, docs sites): +40 points
- HTTPS URLs: +20 points
- HTTP URLs: -10 points
- Documentation/API endpoints: +20 points
- Unknown or suspicious domains: -30 points
- Recently accessed similar URLs: +10 points
- Data exfiltration patterns: -50 points

KNOWN SAFE DOMAINS: {KNOWN_SAFE_DOMAINS}

Rate the safety of this web operation on a scale of 0-100.
Respond ONLY with valid JSON:
{
  "safetyScore": <number 0-100>,
  "reasoning": "<brief explanation>",
  "category": "<safe|cautious|risky|dangerous>"
}
```

### MCP Tool Analysis (mcp-prompt.txt)

```
You are a security analyzer evaluating whether an MCP tool operation should be auto-approved.

TOOL NAME: {TOOL_NAME}
MCP SERVER: {MCP_SERVER}
OPERATION: {OPERATION}
WORKING DIR: {CWD}
SESSION HISTORY: {SESSION_HISTORY}

SAFETY CRITERIA (use as guidance):
- Registered/configured MCP server: +40 points
- Read-only MCP operations: +30 points
- MCP tools from trusted sources (official plugins): +20 points
- Write/modification operations: +10 points
- Unregistered MCP servers: -30 points
- Recently used similar MCP operations: +10 points

REGISTERED MCP SERVERS: {REGISTERED_MCP_SERVERS}

Rate the safety of this MCP operation on a scale of 0-100.
Respond ONLY with valid JSON:
{
  "safetyScore": <number 0-100>,
  "reasoning": "<brief explanation>",
  "category": "<safe|cautious|risky|dangerous>"
}
```

## Session Management

### Session Storage Format

**File:** `~/.claude-permission-analyzer/sessions/{session_id}.json`

```json
{
  "sessionId": "abc123",
  "startTime": "2026-01-30T10:00:00Z",
  "lastActivity": "2026-01-30T10:45:23Z",
  "workingDirectory": "/Users/user/project",
  "conversationHistory": [
    {
      "timestamp": "2026-01-30T10:00:00Z",
      "type": "user-prompt",
      "content": "Add logging to the authentication module"
    },
    {
      "timestamp": "2026-01-30T10:01:15Z",
      "type": "permission-request",
      "toolName": "Read",
      "toolInput": {"file_path": "src/auth/login.js"},
      "decision": "auto-approved",
      "safetyScore": 96,
      "reasoning": "Reading auth file aligns with user's request to add logging",
      "threshold": 93,
      "category": "safe"
    },
    {
      "timestamp": "2026-01-30T10:01:45Z",
      "type": "permission-request",
      "toolName": "Edit",
      "toolInput": {"file_path": "src/auth/login.js"},
      "decision": "auto-approved",
      "safetyScore": 94,
      "reasoning": "Editing auth file to add logging, consistent with conversation context",
      "threshold": 97,
      "category": "safe",
      "thresholdOverride": "user-lowered-temporarily"
    },
    {
      "timestamp": "2026-01-30T10:02:00Z",
      "type": "claude-stop",
      "content": "Added winston logger to authentication module with debug, info, and error levels"
    }
  ]
}
```

### Context Building

When analyzing a new permission request, the service builds context from recent session activity:

```
CONVERSATION CONTEXT:
User: "Add logging to the authentication module"
- [Auto-approved] Read: src/auth/login.js (score: 96)
- [Auto-approved] Edit: src/auth/login.js (score: 94)
Claude: "Added winston logger to authentication module"

CURRENT REQUEST:
Tool: Bash
Command: npm install winston
```

This helps the LLM understand the workflow and provide consistent, context-aware scoring.

## Hook Script Implementation

### Generic Hook Script Template

```python
#!/usr/bin/env python3
import json
import sys
import requests

SERVICE_URL = "http://localhost:5050/api/analyze"
TIMEOUT = 30

def main():
    try:
        # Read hook input from stdin (provided by Claude Code)
        hook_input = json.load(sys.stdin)

        # Add hook type identifier
        hook_input["hook_type"] = "bash"  # varies per hook

        # Send to background service
        response = requests.post(
            SERVICE_URL,
            json=hook_input,
            timeout=TIMEOUT
        )

        if response.status_code != 200:
            # Service error - fall back to user decision
            sys.exit(0)

        result = response.json()

        # Format output for Claude Code (varies by hook type)
        output = format_output(result)

        # Output decision JSON to stdout
        print(json.dumps(output))
        sys.exit(0)

    except requests.exceptions.ConnectionError:
        # Service not running - fall back to normal permission prompt
        sys.exit(0)
    except Exception as e:
        # Log error and fall back
        print(f"Hook error: {e}", file=sys.stderr)
        sys.exit(0)

def format_output(result):
    # Format varies by hook type
    # See specific implementations below
    pass

if __name__ == "__main__":
    main()
```

### PermissionRequest Hook Output

```python
def format_output(result):
    output = {
        "hookSpecificOutput": {
            "hookEventName": "PermissionRequest",
            "decision": {
                "behavior": "allow" if result["autoApprove"] else "deny",
            }
        }
    }

    # If denied, add reason for Claude
    if not result["autoApprove"]:
        output["hookSpecificOutput"]["decision"]["message"] = (
            f"Safety score {result['safetyScore']} below threshold "
            f"{result['threshold']}. Reason: {result['reasoning']}"
        )

        # High-risk denials can interrupt Claude
        if result.get("interrupt", False):
            output["hookSpecificOutput"]["decision"]["interrupt"] = True

    # Support tool input modification
    if result.get("updatedInput"):
        output["hookSpecificOutput"]["decision"]["updatedInput"] = result["updatedInput"]

    # Optional system message
    if result.get("systemMessage"):
        output["systemMessage"] = result["systemMessage"]

    return output
```

### UserPromptSubmit Hook Output

```python
def format_output(result):
    # Can use additionalContext to inject information
    if result.get("additionalContext"):
        output = {
            "hookSpecificOutput": {
                "hookEventName": "UserPromptSubmit",
                "additionalContext": result["additionalContext"]
            }
        }
        return output

    # Or just print to stdout (simpler)
    return None  # Will print result["additionalContext"] directly
```

## C# Component Structure

### Project Organization

```
ClaudePermissionAnalyzer/
├── ClaudePermissionAnalyzer.Service/
│   ├── Program.cs
│   ├── Services/
│   │   ├── PermissionAnalyzerService.cs
│   │   ├── SessionManager.cs
│   │   ├── LLMClient.cs
│   │   ├── ConfigurationManager.cs
│   │   ├── HookHandlerFactory.cs
│   │   └── TranscriptWatcher.cs
│   ├── Handlers/
│   │   ├── IHookHandler.cs
│   │   ├── LLMAnalysisHandler.cs
│   │   ├── LogOnlyHandler.cs
│   │   ├── ContextInjectionHandler.cs
│   │   └── CustomLogicHandler.cs
│   ├── Models/
│   │   ├── PermissionRequest.cs
│   │   ├── PermissionDecision.cs
│   │   ├── SessionData.cs
│   │   ├── Configuration.cs
│   │   ├── HookInput.cs
│   │   └── HookOutput.cs
│   ├── Controllers/
│   │   ├── AnalyzeController.cs
│   │   └── ClaudeLogsController.cs
│   └── wwwroot/
│       ├── index.html
│       ├── dashboard.html
│       ├── config.html
│       ├── js/
│       │   ├── dashboard.js
│       │   ├── logs.js
│       │   ├── config.js
│       │   └── sidebar-logs.js
│       └── css/
│           └── styles.css
├── ClaudePermissionAnalyzer.TrayApp/
│   ├── TrayIcon.cs
│   └── Program.cs
└── hooks/
    ├── bash-hook.py
    ├── file-read-hook.py
    ├── file-write-hook.py
    ├── web-hook.py
    ├── mcp-hook.py
    ├── user-prompt-hook.py
    └── stop-hook.py
```

### Key Interfaces

**IHookHandler.cs:**
```csharp
public interface IHookHandler
{
    Task<HookOutput> HandleAsync(HookInput input, HandlerConfig config);
}
```

**Handler Implementations:**
- `LLMAnalysisHandler` - For permission analysis with safety scoring
- `LLMValidationHandler` - For post-tool validation
- `ContextInjectionHandler` - For adding context to user prompts
- `LogOnlyHandler` - For simple logging without LLM
- `CustomLogicHandler` - For custom business logic

### Core Service Flow

```csharp
public class PermissionAnalyzerService
{
    public async Task<PermissionDecision> AnalyzeAsync(HookInput input)
    {
        // 1. Load handler configuration
        var handlers = _config.GetHandlersForHook(input.HookEventName);

        // 2. Find matching handler
        var handler = handlers.FirstOrDefault(h =>
            MatchesMatcher(h.Matcher, input.ToolName));

        if (handler == null || handler.Mode == "log-only")
        {
            // Just log, no decision needed
            await _sessionManager.LogEventAsync(input);
            return null;
        }

        // 3. Build context from session history
        var context = await _sessionManager.BuildContextAsync(
            input.SessionId);

        // 4. Execute handler
        var handlerInstance = _handlerFactory.Create(handler.Mode);
        var output = await handlerInstance.HandleAsync(input, handler);

        // 5. Store decision in session
        await _sessionManager.RecordDecisionAsync(
            input.SessionId,
            output);

        return output;
    }
}
```

## Web UI

### Page Structure

1. **Dashboard (`/`)** - Statistics, active sessions, recent activity
2. **Live Logs (`/logs`)** - Real-time stream with filtering
3. **Session Details (`/session/{id}`)** - Deep dive into specific session
4. **Configuration (`/config`)** - Edit settings and thresholds
5. **Prompt Editor (`/prompts`)** - Customize LLM prompts

### Sidebar Integration

The sidebar displays Claude Code session logs from `~/.claude/projects/`:

**Features:**
- Project browser (lists all projects)
- Session selector per project
- Real-time transcript viewer
- Correlation with permission decisions
- Auto-scroll with toggle
- Filtering by message type

**API Endpoints:**
- `GET /api/claude-logs/projects` - List all projects and sessions
- `GET /api/claude-logs/transcript/{sessionId}` - Get full transcript
- `GET /api/claude-logs/transcript/{sessionId}/stream` - SSE for live updates

**Real-time Updates:**
- Uses Server-Sent Events (SSE) to stream new transcript entries
- FileSystemWatcher monitors `.jsonl` files for changes
- Automatically correlates tool uses with permission decisions

## Installation

### Prerequisites

- .NET 8+ Runtime
- Python 3.8+
- Claude Code CLI or GitHub CLI configured
- Windows OS (for tray icon; Linux/Mac support can be added)

### Installation Steps

1. **Extract release package:**
   ```
   ClaudePermissionAnalyzer-v1.0.0.zip
   ```

2. **Run installer:**
   ```powershell
   .\install.ps1
   ```

   This will:
   - Create `~/.claude-permission-analyzer/` directory
   - Copy executable and hook scripts
   - Install Python dependencies
   - Create default configuration
   - Configure Claude Code hooks in `~/.claude/settings.json`
   - Create startup shortcut (optional)

3. **Start the service:**
   ```
   ClaudePermissionAnalyzer.exe
   ```

   Or it will auto-start from the startup shortcut.

4. **Access the web UI:**
   ```
   http://localhost:5050
   ```

5. **Check system tray:**
   Look for the analyzer icon in the system tray.

## Security Considerations

### Privacy Controls

- **Code/Content Sending:** Configurable per hook type
  - Default: OFF for file contents
  - Users can enable if they trust their LLM provider
  - Only metadata sent by default (paths, commands, URLs)

- **Session Data:** Stored locally only
  - No telemetry or external logging
  - Session files can be deleted anytime

### Fallback Behavior

- **Service Down:** Hooks gracefully degrade to normal Claude permission prompts
- **LLM Timeout:** Falls back to user decision
- **Parse Errors:** Logs error and asks user

### Audit Trail

- All decisions logged with:
  - Timestamp
  - Tool name and input
  - Safety score and reasoning
  - Auto/manual approval
  - Session context

## Extensibility

### Adding New Hook Types

1. Add hook configuration to `config.json`
2. Create Python hook script (e.g., `new-hook.py`)
3. Register in `~/.claude/settings.json`
4. Optionally create custom handler in C#

### Adding New LLM Providers

Implement `ILLMClient` interface:

```csharp
public interface ILLMClient
{
    Task<LLMResponse> QueryAsync(string prompt, int timeout);
}
```

### Custom Prompt Templates

1. Create new `.txt` file in `prompts/`
2. Use placeholders: `{COMMAND}`, `{FILE_PATH}`, `{SESSION_HISTORY}`, etc.
3. Reference in handler configuration

## Performance

- **Hook Latency:** ~100-500ms (depends on LLM response time)
- **Session Storage:** O(1) lookup via in-memory cache
- **Web UI:** Real-time updates via SignalR (< 50ms latency)
- **Transcript Watching:** FileSystemWatcher with debouncing

## Future Enhancements

- [ ] Machine learning from user overrides (adjust thresholds automatically)
- [ ] Bulk approval/denial for similar operations
- [ ] Export session logs for compliance/audit
- [ ] Integration with security scanning tools
- [ ] Support for custom LLM APIs (OpenAI, Anthropic direct)
- [ ] Mobile app for remote monitoring
- [ ] Slack/Teams notifications for denied permissions
- [ ] Collaborative mode (team-shared allowlists)

## Conclusion

This design provides a comprehensive, extensible, and privacy-conscious permission automation system for Claude Code. By leveraging LLM-based safety analysis with configurable thresholds and contextual awareness, it streamlines the development workflow while maintaining security and auditability.
