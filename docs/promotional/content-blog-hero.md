# NeMo + MAF + A2A v1: A Practical Multi-Agent Demo You Can Run Today

Hi!  
This repo is a hands-on demo showing how to connect two specialized agents in one real workflow:

- **NeMo Agent Toolkit (Python)** for analysis
- **Microsoft Agent Framework (MAF, .NET)** for actions
- **A2A v1-style discovery/communication** between agents
- **Aspire** to run everything together with health, logs, and traces

If you want a practical baseline for multi-agent systems, this is exactly that.

---

## TL;DR

This project solves a common problem: one agent is great at analysis, another is great at execution, and we need both without creating a fragile monolith.

In this demo:

1. Web UI receives the prompt.
2. NeMo analyzes data and produces findings.
3. A follow-up prompt triggers MAF to execute an action (for example, alerts/reports).
4. Aspire provides runtime visibility.

---

## The idea

Most “agent demos” are single-agent chat wrappers.  
Real production systems usually need specialized roles:

- one service to reason over data,
- another service to trigger actions in systems,
- and a thin orchestration layer that keeps things clear and observable.

That’s what this repository implements.

---

## Architecture in one view

- **NeMo Data Analysis Agent** (`src/NemoDataAnalysisAgent`)
  - A2A endpoint + agent card discovery
  - tools for trend/anomaly/metrics analysis
- **MAF Action Agent** (`src/MafActionAgent`)
  - action endpoints (`/api/actions/*`)
  - A2A bridge endpoint
- **Web Chat Interface** (`src/WebChatInterface`)
  - prompt routing (analysis vs action)
  - 2-prompt chain support (NeMo -> MAF)
- **Aspire AppHost** (`apphost.cs`)
  - startup ordering + endpoint wiring + OTEL exporter wiring

---

## Why this approach works

### 1) Separation of responsibilities

NeMo focuses on analysis quality.  
MAF focuses on deterministic action execution.

### 2) Explicit orchestration

The Web UI does intent-based routing.  
It can preserve analysis context and pass it to MAF on the next step.

### 3) Operability

Aspire makes local distributed development manageable:

- one command to start all resources
- resource-level logs
- health checks
- traces

---

## Code samples

## 1) AppHost wiring (Aspire)

```csharp
var nemo = builder.AddExecutable("nemo-agent", "powershell", ".",
    args: new[] { "-NoProfile", "-Command", "...nat a2a serve..." })
    .WithHttpEndpoint(name: "http", env: "NEMO_PORT")
    .WithOtlpExporter();

var mafAgent = builder.AddExecutable("maf-agent", "dotnet", ".",
    args: new[] { "run", "--project", ".\\src\\MafActionAgent\\MafActionAgent.csproj" })
    .WithHttpEndpoint(name: "http", env: "MAF_PORT")
    .WithOtlpExporter()
    .WaitFor(nemo);
```

## 2) MAF GenAI-labeled tracing span

```csharp
using var activity = MafGenAiTelemetry.Source.StartActivity(
    "maf.gen_ai.plan_action", ActivityKind.Internal);

activity?.SetTag("gen_ai.system", "microsoft.agent.framework");
activity?.SetTag("gen_ai.operation.name", "plan_action");
activity?.SetTag("gen_ai.request.type", request.ActionType);
```

## 3) A2A-style discovery contract

```json
{
  "name": "MAF Action Agent",
  "endpoint": "/a2a/maf-action-agent",
  "a2a_version": "1.0"
}
```

---

## Example user flow

1. Prompt 1: **“Analyze quarterly revenue trends”**  
   -> routed to NeMo

2. Prompt 2: **“Trigger alert for high CPU usage based on the analysis findings”**  
   -> routed to MAF with prior analysis context

This gives a clean demo of “analyze first, act second”.

---

## A2A v1 context

One goal of this repo is to align with the cross-platform agent communication direction from the MAF team:

- [A2A v1 is here: cross-platform agent communication in Microsoft Agent Framework for .NET](https://devblogs.microsoft.com/agent-framework/a2a-v1-is-here-cross-platform-agent-communication-in-microsoft-agent-framework-for-net/)

This matters because it keeps the integration model future-friendly and interoperable.

---

## Current status and candid notes

- MAF GenAI-labeled spans are available in Aspire traces.
- NeMo telemetry support exists in NAT, but span richness depends on workflow/tool execution path.
- Fast profile is tuned for responsiveness and may reduce trace detail for direct-answer paths.

---

## How to run

See README Quick Start for the shortest path:

1. Clone repo
2. Create/activate venv
3. Start with `aspire start`
4. Use sample prompts in the Web UI

---

## Final take

This is not a toy screenshot repo.  
It is a practical, runnable multi-agent baseline that shows:

- analysis + action separation,
- A2A-style interoperability,
- and real operational visibility during execution.
