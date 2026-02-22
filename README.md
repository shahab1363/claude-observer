# Claude Permission Analyzer

An intelligent permission automation system for Claude Code that uses LLM-based safety analysis to automatically approve safe operations.

## Features

- **LLM-Based Safety Analysis**: Evaluates operations using Claude or GitHub Copilot CLI
- **Configurable Thresholds**: Per-hook safety thresholds with smart defaults
- **Session Tracking**: Contextual awareness from conversation history
- **Web Dashboard**: Real-time monitoring and configuration
- **Privacy Controls**: Configure what data is sent to LLM

## Installation

### Prerequisites

- .NET 8+ Runtime
- Python 3.8+
- Claude CLI (`claude`) or GitHub CLI (`gh`) configured
- Windows OS (Linux/Mac support coming soon)

### Quick Install

1. Extract the release package
2. Run installation script:
   ```powershell
   .\install.ps1
   ```
3. Start the service:
   ```powershell
   ClaudePermissionAnalyzer.exe
   ```
4. Open web UI: http://localhost:5050

## Configuration

Edit `~/.claude-permission-analyzer/config.json` to customize:

- LLM provider and model
- Safety thresholds per hook type
- Auto-approval settings
- Privacy controls

## Usage

Once installed and running, the service automatically:

1. Receives hook events from Claude Code
2. Analyzes safety using LLM
3. Auto-approves or denies based on threshold
4. Logs all decisions to session history

View activity in the web dashboard at http://localhost:5050.

## Development

### Build

```bash
dotnet build
```

### Test

```bash
dotnet test
```

### Run Locally

```bash
dotnet run --project src/ClaudePermissionAnalyzer.Api
```

## License

MIT
