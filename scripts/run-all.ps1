# Run all components (for manual startup without Aspire)
# Opens three PowerShell windows for Nemo, MAF, and Web UI

param(
    [ValidateSet("nim", "azure-openai")]
    [string]$Provider = "nim",
    
    [string]$NemoHost = "127.0.0.1",
    [int]$NemoPort = 8088,
    
    [string]$MafHost = "127.0.0.1",
    [int]$MafPort = 5055,
    
    [string]$WebUiHost = "127.0.0.1",
    [int]$WebUiPort = 5000
)

# Load environment variables
$envPath = ".\.env"
if (Test-Path $envPath) {
    Get-Content $envPath | ForEach-Object {
        if ($_ -match "^([^=]+)=(.*)$") {
            [System.Environment]::SetEnvironmentVariable($matches[1], $matches[2], "Process")
        }
    }
    Write-Host "✓ Loaded environment variables from .env" -ForegroundColor Green
}

# Validate provider credentials
function Test-ProviderConfig {
    if ($Provider -eq "nim") {
        if (-not $env:NVIDIA_API_KEY) {
            throw "NVIDIA_API_KEY not set. Please set it in .env or environment."
        }
        Write-Host "✓ NVIDIA API key configured" -ForegroundColor Green
    }
    elseif ($Provider -eq "azure-openai") {
        $required = @("AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_API_KEY", "AZURE_OPENAI_DEPLOYMENT_NAME")
        foreach ($var in $required) {
            if (-not (Get-Item env:$var -ErrorAction SilentlyContinue)) {
                throw "$var not set for Azure OpenAI"
            }
        }
        Write-Host "✓ Azure OpenAI configured" -ForegroundColor Green
    }
}

try {
    Test-ProviderConfig
    
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "MAF-A2A-NVIDIA-NemoAgents Demo Startup" -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
    
    Write-Host "Configuration:" -ForegroundColor Yellow
    Write-Host "  Provider: $Provider"
    Write-Host "  NeMo Agent: http://$NemoHost:$NemoPort"
    Write-Host "  MAF Agent: http://$MafHost:$MafPort"
    Write-Host "  Web UI: http://$WebUiHost:$WebUiPort`n"
    
    # Start NeMo Agent
    Write-Host "Starting NeMo Data Analysis Agent..." -ForegroundColor Yellow
    $nemoScript = @"
Set-Location "src\NemoDataAnalysisAgent"
`$env:NEMO_HOST = "$NemoHost"
`$env:NEMO_PORT = $NemoPort
`$env:NEMO_PROVIDER = "$Provider"

Write-Host "NeMo Agent is starting..." -ForegroundColor Cyan
Write-Host "Endpoint: http://`$env:NEMO_HOST:`$env:NEMO_PORT" -ForegroundColor Green

# Run NeMo with NAT CLI
`$venvNat = "..\..\.venv\Scripts\nat.exe"
if (Test-Path `$venvNat) {
    & `$venvNat a2a serve --config_file .\nemo\workflow.yml --host `$env:NEMO_HOST --port `$env:NEMO_PORT --name "nemo-data-analysis-agent"
}
else {
    nat a2a serve --config_file .\nemo\workflow.yml --host `$env:NEMO_HOST --port `$env:NEMO_PORT --name "nemo-data-analysis-agent"
}
"@
    
    Start-Process pwsh -ArgumentList "-NoProfile", "-Command", $nemoScript -WindowStyle Normal
    Start-Sleep -Seconds 2
    
    # Start MAF Agent
    Write-Host "Starting MAF Action Agent..." -ForegroundColor Yellow
    $mafScript = @"
Set-Location "src\MafActionAgent"
`$env:MAF_HOST = "$MafHost"
`$env:MAF_PORT = $MafPort
`$env:NEMO_A2A_ENDPOINT = "http://$NemoHost:$NemoPort"

Write-Host "MAF Agent is starting..." -ForegroundColor Cyan
Write-Host "Endpoint: http://`$env:MAF_HOST:`$env:MAF_PORT" -ForegroundColor Green

dotnet run
"@
    
    Start-Process pwsh -ArgumentList "-NoProfile", "-Command", $mafScript -WindowStyle Normal
    Start-Sleep -Seconds 2
    
    # Start Web UI
    Write-Host "Starting Web Chat Interface..." -ForegroundColor Yellow
    $webScript = @"
Set-Location "src\WebChatInterface"
`$env:WEB_UI_HOST = "$WebUiHost"
`$env:WEB_UI_PORT = $WebUiPort
`$env:NEMO_A2A_ENDPOINT = "http://$NemoHost:$NemoPort"
`$env:MAF_AGENT_ENDPOINT = "http://$MafHost:$MafPort"

Write-Host "Web UI is starting..." -ForegroundColor Cyan
Write-Host "Endpoint: http://`$env:WEB_UI_HOST:`$env:WEB_UI_PORT" -ForegroundColor Green

dotnet run
"@
    
    Start-Process pwsh -ArgumentList "-NoProfile", "-Command", $webScript -WindowStyle Normal
    
    Write-Host "`n✓ All services started!" -ForegroundColor Green
    Write-Host "`nService URLs:" -ForegroundColor Yellow
    Write-Host "  Web UI: http://$WebUiHost:$WebUiPort" -ForegroundColor Cyan
    Write-Host "  NeMo Agent Card: http://$NemoHost:$NemoPort/.well-known/agent-card.json" -ForegroundColor Cyan
    Write-Host "  MAF Agent Health: http://$MafHost:$MafPort/health" -ForegroundColor Cyan
    Write-Host "`nNote: Three PowerShell windows will open. Close any to stop that service." -ForegroundColor Gray
    Write-Host "Press Ctrl+C in the main window to close all services.`n" -ForegroundColor Gray
    
    # Keep main process alive
    while ($true) {
        Start-Sleep -Seconds 10
    }
}
catch {
    Write-Host "`n✗ Error: $_" -ForegroundColor Red
    exit 1
}
