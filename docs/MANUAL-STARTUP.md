# Manual Startup (3 Terminals)

This guide shows how to start all components manually without using Azure Aspire, useful for development and debugging.

## Prerequisites

- **Python 3.10+** with pip
- **.NET 10 SDK** (download from <https://dotnet.microsoft.com/download>)
- **One LLM Provider** (NVIDIA API or Azure OpenAI)
- Environment file configured (see [Configuration](./CONFIGURATION.md))

## Starting Each Component

Open three separate terminal windows in the project root directory.

### Terminal 1 - Start NeMo Agent

```powershell
Set-Location .\src\NemoDataAnalysisAgent
python -m nat a2a serve --config_file .\nemo\workflow.yml --host 127.0.0.1 --port 8088
```

**Expected Output:**
```
INFO: NeMo Agent started on 127.0.0.1:8088
INFO: A2A endpoint ready at http://127.0.0.1:8088/.well-known/agent-card.json
```

### Terminal 2 - Start MAF Agent

```bash
dotnet run --project src/MafActionAgent
```

**Expected Output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://0.0.0.0:5055
```

### Terminal 3 - Start Web UI

```bash
dotnet run --project src/WebChatInterface
```

**Expected Output:**
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://0.0.0.0:5000
```

## Verification

Once all three are running:

1. **Web UI**: <http://localhost:5000>
2. **NeMo Agent Card**: <http://127.0.0.1:8088/.well-known/agent-card.json>
3. **MAF Agent Health**: <http://127.0.0.1:5055/health>

All three endpoints should respond successfully. If any return errors, check the terminal output for error messages.

## Troubleshooting

### Port Already in Use

If you see "port already in use" errors:

```powershell
# Find process using port 8088
netstat -ano | findstr :8088

# Kill the process (replace PID with the actual process ID)
taskkill /PID <PID> /F
```

### Python Module Not Found

Ensure dependencies are installed:

```bash
pip install -r requirements.txt
pip install -r src/NemoDataAnalysisAgent/nemo/requirements.txt
```

### Connection Errors

If agents can't reach each other:

1. Verify endpoints match in each terminal's logs
2. Check firewall settings allow local connections
3. Ensure no VPN is interfering with 127.0.0.1 connections

## Stopping the System

Press `Ctrl+C` in each terminal window to gracefully shutdown each component.

## Next Steps

- See [Architecture](../README.md#-system-architecture) to understand system design
- See [Configuration](./CONFIGURATION.md) to customize settings
- Check [Deployment](./DEPLOYMENT.md) for production-ready startup with Aspire
