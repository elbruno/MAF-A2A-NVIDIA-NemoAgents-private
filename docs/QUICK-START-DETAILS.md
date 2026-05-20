# Quick Start Details

This page contains the extended operational details behind the README Quick Start.

## Startup and runtime notes

- The Web UI sends a one-time NeMo warm-up request at startup so the first real prompt does not pay full cold-start cost.
- If warm-up is still running, `/api/chat` can return a short "still warming up" response.
- The chat UI shows an in-chat spinner with elapsed time while a request is in progress.
- Agent markdown responses are rendered as formatted HTML in chat bubbles.

### Warm-up tuning

You can tune warm-up behavior with:

- `NEMO_WARMUP_ENABLED`
- `NEMO_WARMUP_DELAY_SECONDS`
- `NEMO_WARMUP_TIMEOUT_SECONDS`
- `NEMO_WARMUP_RETRY_DELAY_SECONDS`
- `NEMO_WARMUP_MAX_ATTEMPTS`
- `NEMO_WARMUP_REQUEST_MAX_WAIT_SECONDS`
- `NEMO_WARMUP_MESSAGE`

## NeMo performance profile

NeMo defaults to the **fast profile**:

- `NEMO_WORKFLOW_PROFILE=fast`
- Smaller default model: `NEMO_FAST_MODEL_NAME=meta/llama-3.2-1b-instruct`
- Fewer tools and lower output budget for lower latency

Set `NEMO_WORKFLOW_PROFILE=standard` when you need the full workflow.

## Chat routing behavior

`/api/chat` (`src/WebChatInterface/Program.cs`) uses intent-based routing:

- Calls **MAF** first only when the message contains one of `alert`, `report`, `action`, `trigger` and starts with one of `trigger`, `generate`, `send`, `create`, `execute`, `run`.
- Otherwise calls **NeMo** for analysis/non-action prompts.
- If MAF execution fails, falls back to NeMo.

Examples:

- NeMo path: `Analyze quarterly revenue trends`
- MAF path: `Trigger alert for high CPU usage`

## 2-prompt chain (NeMo -> MAF)

Supported demo flow:

1. `Analyze quarterly revenue trends`
2. `Trigger alert for high CPU usage based on the analysis findings`

For step 2, Web UI enriches the MAF payload with prior NeMo context from the same `sessionId`:

- `analysisSummary`
- `analysisSourcePrompt`
- `analysisCapturedAtUtc`

Context storage is TTL-bounded and size-limited via:

- `CHAT_ANALYSIS_CONTEXT_TTL_MINUTES`
- `CHAT_ANALYSIS_CONTEXT_MAX_LENGTH`

## Port conflicts

If startup fails due to occupied ports:

```powershell
.\scripts\check-port-conflicts.ps1
.\scripts\stop-port-conflicts.ps1 -Force
```

## Where to go next

- [Configuration](./CONFIGURATION.md)
- [Manual Startup](./MANUAL-STARTUP.md)
- [Testing](./TESTING.md)
- [Architecture](./README-ARCHITECTURE.md)
