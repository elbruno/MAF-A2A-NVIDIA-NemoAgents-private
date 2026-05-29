# Configuration

This guide covers all configuration options for the MAF-A2A-NVIDIA-NemoAgents system.

## Environment Setup

### Creating .env File

```bash
# Copy the template
cp .env.example .env

# Edit and add your credentials
# Use your preferred editor (code, vim, notepad, etc.)
```

## LLM Provider Configuration

Choose **ONE** of the following:

### Option A: NVIDIA API

```bash
# Get API key from https://build.nvidia.com/
NVIDIA_API_KEY=sk-your-key-here

# Optional: Customize NeMo agent host/port
NEMO_HOST=127.0.0.1
NEMO_PORT=8088
```

**Supported Models:**
- `meta/llama-2-7b-chat`
- `meta/llama-2-70b-chat`
- `mistralai/mistral-7b-instruct-v0.1`
- And more via NVIDIA NIM

### Option B: Azure OpenAI

```bash
# Get these from Azure OpenAI resource
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4-turbo
AZURE_OPENAI_API_KEY=your-api-key

# Optional: Customize services
NEMO_HOST=127.0.0.1
NEMO_PORT=8088
```

**Note:** Use the full endpoint URL with trailing slash.

## Service Endpoints

```bash
# NeMo Data Analysis Agent
NEMO_HOST=127.0.0.1
NEMO_PORT=8088

# MAF Action Agent
MAF_HOST=127.0.0.1
MAF_PORT=5055

# Web UI
WEB_UI_HOST=127.0.0.1
WEB_UI_PORT=5000
```

## Observability & Tracing

```bash
# Enable/disable OpenTelemetry tracing
ENABLE_OTEL_TRACING=true

# OTEL collector endpoint (used for manual/non-Aspire runs)
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317

# Log level (Trace, Debug, Information, Warning, Error, Critical)
NEMO_LOG_LEVEL=Information
WEB_UI_LOG_LEVEL=Information
```

### GenAI tracing notes

- **MAF agent** now emits GenAI-tagged spans such as `maf.gen_ai.plan_action` to the Aspire OTEL collector.
- **NeMo agent** depends on NeMo Agent Toolkit telemetry behavior and tool execution path; in fast profile it may answer directly without tool spans for some prompts.
- In Aspire runs, OTLP endpoint/headers are injected dynamically; for manual runs you must provide `OTEL_EXPORTER_OTLP_ENDPOINT` (and headers when required by your collector).

## Aspire Configuration

When running with `aspire start`:

```bash
# OTLP endpoint and headers are injected by Aspire when resources use .WithOtlpExporter()
# OTEL_EXPORTER_OTLP_ENDPOINT=<dynamic>
# OTEL_EXPORTER_OTLP_HEADERS=<dynamic>
# OTEL_EXPORTER_OTLP_PROTOCOL=grpc

# Runtime service ports are injected by Aspire to avoid port collisions:
# NEMO_PORT=<dynamic>
# MAF_PORT=<dynamic>
```

## Advanced Configuration

### NeMo Workflow Config

The NeMo agent supports two workflow profiles:

- `src/NemoDataAnalysisAgent/nemo/workflow.yml` (standard)
- `src/NemoDataAnalysisAgent/nemo/workflow-fast.yml` (lower-latency profile)

Select a profile with:

```bash
NEMO_WORKFLOW_PROFILE=fast  # or standard
```

The fast profile is tuned for faster responses by reducing tools and prompt complexity. You can also tune its model independently:

```bash
NEMO_FAST_MODEL_NAME=meta/llama-3.2-3b-instruct
```

Custom analysis functions are registered by the local package in `src/NemoDataAnalysisAgent/src/nemo_data_analysis_agent`.

Key settings:
- **Tools**: Data analysis tools (time-series, anomaly detection, metrics)
- **Provider**: NVIDIA API or Azure OpenAI
- **Model**: LLM model to use for analysis
- **Temperature**: LLM creativity (0.0-1.0)

### Customizing Models

To use a specific LLM model, update the `llms` section in `workflow.yml`:

```yaml
llms:
  nvidia_llm:
    _type: nim
    model_name: meta/llama-3.1-70b-instruct
    temperature: 0.3
```
Then point `workflow.llm_name` at the LLM entry you want to use.

### Health Check Endpoints

The system exposes health check endpoints:

```bash
# Web UI health
curl http://localhost:5000/health

# NeMo agent card
curl http://127.0.0.1:8088/.well-known/agent-card.json

# MAF agent health
curl http://127.0.0.1:5055/health
```

## Environment Variables Reference

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `NVIDIA_API_KEY` | Conditional | - | NVIDIA API key (if using NVIDIA provider) |
| `AZURE_OPENAI_ENDPOINT` | Conditional | - | Azure OpenAI endpoint (if using Azure) |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Conditional | - | Azure deployment name (if using Azure) |
| `AZURE_OPENAI_API_KEY` | Conditional | - | Azure OpenAI API key (if using Azure) |
| `NEMO_HOST` | No | 127.0.0.1 | NeMo agent hostname |
| `NEMO_PORT` | No | 8088 | NeMo agent port |
| `NEMO_PUBLIC_BASE_URL` | No | http://127.0.0.1:8088 | Public NeMo base URL used by A2A discovery |
| `NEMO_WORKFLOW_PROFILE` | No | fast | Select NeMo workflow profile (`fast` or `standard`) |
| `NEMO_FAST_MODEL_NAME` | No | meta/llama-3.2-3b-instruct | Model used by fast NeMo profile |
| `MAF_HOST` | No | 127.0.0.1 | MAF agent hostname |
| `MAF_PORT` | No | 5055 | MAF agent port |
| `WEB_UI_HOST` | No | 127.0.0.1 | Web UI hostname |
| `WEB_UI_PORT` | No | 5000 | Web UI port |
| `CHAT_ANALYSIS_CONTEXT_TTL_MINUTES` | No | 30 | TTL for storing prior NeMo analysis per chat session |
| `CHAT_ANALYSIS_CONTEXT_MAX_LENGTH` | No | 1600 | Max stored characters for NeMo summary forwarded to MAF |
| `ENABLE_OTEL_TRACING` | No | true | Enable OpenTelemetry tracing |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | No | http://localhost:4317 | OTEL collector endpoint (manual runs); Aspire injects dynamic value at runtime |
| `NEMO_LOG_LEVEL` | No | Information | NeMo logging level |
| `WEB_UI_LOG_LEVEL` | No | Information | Web UI logging level |

## Troubleshooting Configuration

### "API Key Invalid" Error

- Verify key is copied correctly (no extra spaces)
- Check key hasn't expired
- For NVIDIA: visit <https://build.nvidia.com/> to verify account status
- For Azure: verify resource exists and deployment is active

### "Provider Not Found"

- Ensure exactly one provider is configured
- Check for typos in provider names
- Restart the agent after changing `.env`

### Connection Timeouts

- Check firewall allows connections on configured ports
- Verify services are running on configured hosts/ports
- Try using `localhost` instead of `127.0.0.1` if DNS issues

### "Address already in use" / Port collisions

Use the helper scripts from the repository root:

```powershell
.\scripts\check-port-conflicts.ps1
.\scripts\stop-port-conflicts.ps1 -Force
```

Notes:
- `check-port-conflicts.ps1` checks NeMo/MAF/Web default ports (`8088`, `5055`, `5000`).
- `stop-port-conflicts.ps1` stops only the detected blocking listeners by PID.

## Next Steps

- See [Manual Startup](./MANUAL-STARTUP.md) to run components manually
- See [Deployment](./DEPLOYMENT.md) for Azure Container Apps and Docker Compose setup
- See [Testing](./TESTING.md) to validate configuration
