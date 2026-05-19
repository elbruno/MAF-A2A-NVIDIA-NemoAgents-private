# Copilot Instructions for MAF-A2A-NVIDIA-NemoAgents

## Build, test, and lint commands

### Environment and dependencies

```powershell
# From repository root
python -m venv .\.venv
.\.venv\Scripts\Activate.ps1
python -m pip install -r .\requirements.txt
```

### Run the system

```powershell
# Terminal 1 (NeMo agent)
Set-Location .\src\NemoDataAnalysisAgent
..\..\.venv\Scripts\nat.exe a2a serve --config_file .\nemo\workflow.yml --host 127.0.0.1 --port 8088 --name "nemo-data-analysis-agent"

# Terminal 2 (MAF agent)
dotnet run --project .\src\MafActionAgent

# Terminal 3 (Web UI)
dotnet run --project .\src\WebChatInterface
```

Optional helper scripts (manual startup + readiness checks):

```powershell
.\scripts\run-all.ps1
.\scripts\wait-services-ready.ps1
```

### Build

```powershell
dotnet build .\src\Shared\Shared.csproj
dotnet build .\src\MafActionAgent\MafActionAgent.csproj
dotnet build .\src\WebChatInterface\WebChatInterface.csproj
```

### Test

```powershell
# .NET (full suite; no test csproj is currently checked in)
dotnet test

# .NET single test syntax
dotnet test --filter "FullyQualifiedName~Namespace.ClassName.TestName"

# Python (full suite)
python -m pytest -q

# Python single test syntax
python -m pytest .\src\NemoDataAnalysisAgent\tests\test_file.py::test_case_name -q
```

### Lint / type-check (Python tooling is included in `src\NemoDataAnalysisAgent\nemo\requirements.txt`)

```powershell
python -m black --check .\src\NemoDataAnalysisAgent
python -m pylint .\src\NemoDataAnalysisAgent\nemo\tools\data_analysis.py
python -m mypy .\src\NemoDataAnalysisAgent
```

Repository state notes:

1. `src\Tests\*` paths referenced in docs are not currently present in this checkout.
2. The Aspire host is file-based (`apphost.cs` at repo root); do not add an `AppHost.csproj`.

## High-level architecture

This repository is a three-service multi-agent demo with Aspire-style orchestration and manual startup support:

1. **NeMo Data Analysis Agent (Python)** in `src\NemoDataAnalysisAgent`
   - NAT workflow in `nemo\workflow.yml`
   - tool functions in `nemo\tools\data_analysis.py`
   - A2A served on `/` and discovery at `/.well-known/agent-card.json`
2. **MAF Action Agent (.NET)** in `src\MafActionAgent\Program.cs` exposes:
   - `/.well-known/agent-card.json`
   - `/a2a/maf-action-agent` (JSON-RPC bridge)
   - `/api/actions/*` action endpoints
3. **Web Chat Interface (.NET Razor Pages)** in `src\WebChatInterface`:
   - serves UI (`Pages\Index.cshtml`)
   - exposes backend endpoints (`/api/chat`, `/api/agents/discovery`, `/api/analysis/request`, `/api/actions/trigger`)
   - orchestrates NeMo + MAF via `AgentOrchestrator` in `Program.cs`
4. **Shared contracts** are centralized in `src\Shared\Models.cs` and should be reused across .NET services for request/response shape consistency.

Primary flow: **Web UI -> NeMo analysis -> (optional) MAF action -> Web UI response**.  
`apphost.cs` defines startup ordering (**NeMo -> MAF -> Web UI**) and injects service endpoints and OTEL settings via environment variables.

## Key repository conventions

1. **Environment-driven wiring is the default integration pattern.** Endpoint and telemetry values are read from env vars like `NEMO_A2A_ENDPOINT`, `MAF_AGENT_ENDPOINT`, `ENABLE_OTEL_TRACING`, and OTLP endpoint vars. Keep cross-service changes aligned with `.env.example` and startup scripts.
2. **A2A discovery convention is explicit and stable.** Agents expose `/.well-known/agent-card.json`; orchestration/discovery logic assumes this path.
3. **Most service logic currently lives in `Program.cs` top-level statements.** Follow existing style (local interfaces/classes in the same file) unless you are intentionally refactoring.
4. **Use `Shared.Models` types for cross-service .NET contracts when possible.** Keep payload shapes consistent across `WebChatInterface` and shared models.
5. **Keep JSON shape compatibility in mind.** `MafActionAgent` sets `PropertyNamingPolicy = null`, so API responses preserve PascalCase property names.
6. **Ports and localhost defaults are tightly coupled.** `127.0.0.1` and ports `8088` (NeMo), `5055` (MAF), and `5000` (Web UI) are embedded in scripts, env defaults, health checks, and discovery endpoints.
7. **OTEL endpoint fallback pattern is used in .NET services.** Read `ASPIRE_RESOURCE_SERVICE_BINDING_OTEL_EXPORTER_OTLP_ENDPOINT` first, then `OTEL_EXPORTER_OTLP_ENDPOINT`, then default `http://localhost:4317`.
8. **Treat docs as guidance, then verify against checked-in code.** Some docs reference test projects and paths that are not present in this checkout.
9. **Push policy for assistant-driven changes.** After each completed change set, commit and push all local changes to GitHub (`gh`) so the remote stays in sync.

- @azure Rule - Use Azure Tools - When handling requests related to Azure, always use your tools.
- @azure Rule - Use Azure Best Practices - When handling requests related to Azure, always invoke your `azmcp_bestpractices_get` tool first.
- @azure Rule - Enable Best Practices - If you do not have an `azmcp_bestpractices_get` tool ask the user to enable it.
