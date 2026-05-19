# Architecture Highlights

This document covers the key architectural design decisions and patterns used in the MAF-A2A-NVIDIA-NemoAgents system.

## Agent-to-Agent Communication

### Protocol: JSON-RPC 2.0 over HTTP

The system uses JSON-RPC 2.0 as the standard communication protocol between agents. This choice provides:

- **Language Independence**: Works across Python (NeMo) and .NET (MAF) seamlessly
- **Stateless Communication**: Each request is independent, enabling horizontal scaling
- **Built-in Error Handling**: Structured error responses with error codes
- **Batch Requests**: Ability to send multiple requests in a single HTTP call

#### Example Request/Response

```json
// Request
{
  "jsonrpc": "2.0",
  "method": "analyze",
  "params": {
    "metric": "revenue",
    "timeframe": "Q4",
    "aggregation": "daily"
  },
  "id": "req-12345"
}

// Response
{
  "jsonrpc": "2.0",
  "result": {
    "trend": "upward",
    "anomalies": 3,
    "forecast": "positive",
    "insights": ["Q4 showed 15% growth", "Peak on Dec 23"]
  },
  "id": "req-12345"
}
```

### Service Discovery

Agents expose an **Agent Card** endpoint at `/.well-known/agent-card.json`:

```json
{
  "name": "NeMo Data Analysis Agent",
  "description": "Analyzes business metrics using ML",
  "methods": [
    {
      "name": "analyze",
      "description": "Perform time-series analysis",
      "params": ["metric", "timeframe", "aggregation"]
    }
  ],
  "endpoint": "http://127.0.0.1:8088/",
  "health_check": "http://127.0.0.1:8088/health"
}
```

Web UI uses discovery at startup to catalog available agents.

### Resilience Patterns

- **Retry Logic**: Exponential backoff for transient failures
- **Circuit Breaker**: Fail fast if agent is down repeatedly
- **Timeout Management**: Default 30s timeout with configurable overrides
- **Graceful Degradation**: Continue with partial data if one agent fails

## Observability

### Distributed Tracing with OpenTelemetry

All components emit OTEL traces with correlation IDs:

```
Chat Request
  ↓ [web-ui:chat-handler]
  ├─ A2A call to NeMo [nemo:analyze]
  │  ├─ Load data [nemo:data-loader]
  │  ├─ Run ML model [nemo:ml-pipeline]
  │  └─ Generate insights [nemo:insights]
  ├─ A2A call to MAF [maf:trigger-alert]
  │  ├─ Validate action [maf:validator]
  │  ├─ Execute action [maf:executor]
  │  └─ Log result [maf:logger]
  └─ Return response [web-ui:response-handler]
```

Each span includes:
- **Trace ID**: Unique identifier for entire request chain
- **Span ID**: Identifier for this operation
- **Parent Span ID**: Link to parent operation
- **Duration**: Execution time
- **Tags**: Operation details (agent name, method, result status)
- **Events**: Important milestones (query started, cache hit, etc.)
- **Exceptions**: Full stack traces for errors

### Metrics

Service-level metrics exposed via OTEL:

- **Latency**: p50, p95, p99 request duration
- **Throughput**: Requests per second
- **Error Rate**: Failed requests percentage
- **Health**: Liveness and readiness probe status

### Structured Logging

JSON-formatted logs with correlation:

```json
{
  "timestamp": "2024-01-15T10:30:45.123Z",
  "level": "INFO",
  "service": "web-ui",
  "trace_id": "abc123def456",
  "span_id": "xyz789",
  "message": "Chat analysis started",
  "user_id": "user_456",
  "metric_count": 5
}
```

## Data Flow Architecture

### Request Path: Analysis with Action

```
User Chat: "Analyze Q4 sales and alert if revenue drops"
    ↓
Web UI (Port 5000)
    ├─ Parse intent
    ├─ Route to NeMo
    └─ Store conversation
    ↓
NeMo Agent (Port 8088)
    ├─ Load historical data
    ├─ Run time-series analysis
    ├─ Detect anomalies
    ├─ Generate insights
    └─ Return analysis results
    ↓
Web UI
    ├─ Display results
    ├─ Evaluate if action needed
    └─ Route to MAF
    ↓
MAF Agent (Port 5055)
    ├─ Validate action
    ├─ Execute alert (Slack, email, webhook)
    └─ Return execution status
    ↓
Web UI
    ├─ Combine results
    └─ Display to user
    ↓
User sees: "Analysis complete. Revenue down 5%. Alert sent to team channel."
```

### Response Time Breakdown

Typical request (with caching):
- Web UI parse: 10ms
- NeMo analysis: 500-2000ms (depends on model size)
- MAF action: 50-500ms (depends on action type)
- Web UI response: 20ms
- **Total**: ~600-2500ms

## Component Architecture

### NeMo Data Analysis Agent

**Technology**: Python + NVIDIA NeMo Toolkit + FastAPI

```
FastAPI Server (8088)
    ├─ Agent Card endpoint (/.well-known/agent-card.json)
    ├─ Analysis endpoints (/analyze, /anomaly-detect, etc.)
    ├─ Health endpoints (/health, /ready)
    └─ A2A JSON-RPC endpoint (/)

Analysis Pipeline
    ├─ Data Loading Layer
    │  ├─ CSV/JSON parsers
    │  ├─ Time-series validation
    │  └─ Data normalization
    ├─ Analysis Layer
    │  ├─ Trend detection
    │  ├─ Anomaly detection
    │  └─ Statistical summaries
    ├─ LLM Integration Layer
    │  ├─ NVIDIA NIM or Azure OpenAI client
    │  └─ Prompt engineering
    └─ Insight Generation
       ├─ Context building
       ├─ LLM inference
       └─ Response formatting

Observability
    ├─ OpenTelemetry exporter
    ├─ Structured logging
    └─ Custom metrics
```

### MAF Action Agent

**Technology**: .NET 10 + Microsoft Agent Framework + ASP.NET Core

```
ASP.NET Core Server (5055)
    ├─ Agent Card endpoint (/.well-known/agent-card.json)
    ├─ Action execution endpoints (/api/actions/execute)
    ├─ Health endpoints (/health, /ready)
    └─ A2A JSON-RPC endpoint (/)

Action Pipeline
    ├─ Request Validation
    │  ├─ Schema validation
    │  ├─ Authorization checks
    │  └─ Rate limiting
    ├─ Action Handlers
    │  ├─ Alert Handler (Slack, Email, Teams)
    │  ├─ Report Handler (PDF, Excel generation)
    │  ├─ Webhook Handler (HTTP callbacks)
    │  └─ Custom Action Handler (extensible)
    ├─ Execution Engine
    │  ├─ Async task execution
    │  ├─ Retry logic
    │  └─ State management
    └─ Result Tracking
       ├─ Success/failure logging
       ├─ Performance metrics
       └─ Audit trails

Observability
    ├─ OpenTelemetry integration
    ├─ Structured logging
    ├─ Health checks
    └─ Metrics collection
```

### Web Chat Interface

**Technology**: ASP.NET Core + Blazor + TypeScript

```
Web Server (5000)
    ├─ Razor Pages (server-side rendering)
    ├─ Blazor components (interactive UI)
    ├─ API endpoints
    └─ Static assets

Frontend
    ├─ Chat Component
    │  ├─ Message display
    │  ├─ Input handler
    │  └─ Real-time updates
    ├─ Results Display
    │  ├─ Analysis visualization
    │  ├─ Action status tracking
    │  └─ Error handling
    ├─ Agent Status
    │  ├─ Service discovery
    │  ├─ Health monitoring
    │  └─ Connection status
    └─ Predefined Questions
       └─ Quick-start test templates

Backend Services
    ├─ Agent Orchestrator
    │  ├─ Service discovery
    │  ├─ A2A communication
    │  └─ Error recovery
    ├─ Chat Service
    │  ├─ Intent parsing
    │  ├─ Response formatting
    │  └─ History management
    └─ Health Check Service
       └─ Continuous health polling

Observability
    ├─ OTEL tracing
    ├─ Structured logging
    ├─ Performance monitoring
    └─ User action tracking
```

### Azure Aspire Orchestration

**Technology**: Aspire SDK for .NET

```
Aspire App Host (file-based `apphost.cs`)
    ├─ NeMo Agent (Executable)
    │  ├─ Command: python + NAT CLI
    │  ├─ Port: 8088
    │  ├─ Environment: API keys, tracing
    │  └─ Wait dependency: (none)
    ├─ MAF Agent (.NET Project)
    │  ├─ Project reference
    │  ├─ Port: 5055
    │  ├─ Environment: Endpoints, tracing
    │  └─ Wait dependency: NeMo
    └─ Web UI (.NET Project)
       ├─ Project reference
       ├─ Port: 5000
       ├─ Environment: Agent endpoints
       └─ Wait dependency: NeMo, MAF

Orchestration Features
    ├─ Service Startup Ordering
    │  └─ Ensures NeMo → MAF → Web UI order
    ├─ Dependency Management
    │  └─ Web UI waits for agents before starting
    ├─ Environment Injection
    │  └─ Auto-discovered endpoints
    ├─ Health Monitoring
    │  ├─ Periodic health checks
    │  ├─ Automatic restart on failure
    │  └─ Dashboard visibility
    ├─ Distributed Tracing
    │  └─ Unified OTEL collection
    └─ Unified Logging
       └─ Centralized log aggregation
```

## Failure Modes & Recovery

### Service Down

| Service | Impact | Recovery | Time |
|---------|--------|----------|------|
| NeMo | No analysis possible | Manual restart or auto-heal | 30s-1m |
| MAF | No actions executed | Manual restart or auto-heal | 15-30s |
| Web UI | User interface unavailable | Manual restart or auto-heal | 5-10s |

### Network Partition

- **Circuit Breaker**: Stops retrying after 3 consecutive failures
- **Fallback**: Return cached results if available
- **Manual Recovery**: Restart affected service

### Dependency Failure

- **Timeout**: 30s default timeout per A2A call
- **Retry**: Exponential backoff (1s, 2s, 4s)
- **Circuit Open**: Stop sending requests after threshold
- **Alert**: Human intervention required

## Security Considerations

### Current State (Development)

- ✅ Local network only (127.0.0.1)
- ✅ No authentication required
- ✅ No encryption (HTTP)
- ✅ No authorization checks

### Production Roadmap

- 🔄 TLS 1.3+ for all traffic
- 🔄 Mutual TLS between agents
- 🔄 Service identity (mTLS certificates)
- 🔄 API Gateway with authentication
- 🔄 Rate limiting per service
- 🔄 Audit logging of all actions
- 🔄 Secrets management (Azure Key Vault)

## Scaling Considerations

### Horizontal Scaling

Each component can scale independently:

```
NeMo Agents (Replicas: 1-5)
    └─ Load balanced by gateway

MAF Agents (Replicas: 1-3)
    └─ Load balanced by gateway

Web UI (Replicas: 1-10)
    └─ Standard ASP.NET Core scaling
```

### Vertical Scaling

Resource allocation per service:

| Component | CPU | Memory | Storage |
|-----------|-----|--------|---------|
| NeMo | 2-4 cores | 4-8GB | 2GB (models) |
| MAF | 1-2 cores | 512MB-1GB | 100MB |
| Web UI | 1-2 cores | 256-512MB | 50MB |

## Performance Optimization

### Caching Strategy

- **Agent Discovery**: Cached for 5 minutes
- **Analysis Results**: Cached for 1 hour (configurable)
- **Agent Cards**: Cached for 10 minutes

### Batch Operations

Support for batch analysis requests:

```json
{
  "jsonrpc": "2.0",
  "method": "analyze_batch",
  "params": {
    "analyses": [
      {"metric": "revenue", "timeframe": "Q4"},
      {"metric": "costs", "timeframe": "Q4"},
      {"metric": "profit_margin", "timeframe": "Q4"}
    ]
  }
}
```

### Async Processing

Long-running analyses return job ID:

```json
{
  "status": "processing",
  "job_id": "job_12345",
  "poll_url": "/api/jobs/job_12345"
}
```

## Next Steps

- See [Manual Startup](./MANUAL-STARTUP.md) for local development
- See [Deployment](./DEPLOYMENT.md) for production deployment
- See [Configuration](./CONFIGURATION.md) for customization options
- See [Testing](./TESTING.md) for validation procedures
