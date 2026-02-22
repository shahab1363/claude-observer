# Claude Permission Analyzer Installation Script

Write-Host "Installing Claude Permission Analyzer..." -ForegroundColor Green

# Create installation directory
$installDir = Join-Path $env:USERPROFILE ".claude-permission-analyzer"
if (-not (Test-Path $installDir)) {
    New-Item -ItemType Directory -Path $installDir | Out-Null
}

# Copy files
Write-Host "Copying files..."
Copy-Item "ClaudePermissionAnalyzer.exe" $installDir -Force
Copy-Item "hooks/*" (Join-Path $installDir "hooks") -Recurse -Force
Copy-Item "prompts/*" (Join-Path $installDir "prompts") -Recurse -Force
Copy-Item "wwwroot/*" (Join-Path $installDir "wwwroot") -Recurse -Force

# Install Python dependencies
Write-Host "Installing Python dependencies..."
pip install requests

# Create default config if not exists
$configPath = Join-Path $installDir "config.json"
if (-not (Test-Path $configPath)) {
    @"
{
  "llm": {
    "provider": "claude-cli",
    "model": "sonnet",
    "timeout": 30000
  },
  "server": {
    "port": 5050,
    "host": "localhost"
  }
}
"@ | Out-File $configPath -Encoding UTF8
}

Write-Host "`nInstallation complete!" -ForegroundColor Green
Write-Host "Run '$installDir\ClaudePermissionAnalyzer.exe' to start the service."
Write-Host "Access the web UI at http://localhost:5050"
