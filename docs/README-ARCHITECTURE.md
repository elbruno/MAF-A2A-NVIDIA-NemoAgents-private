# System Architecture & Design

## High-Level Overview

The MAF-A2A-NVIDIA-NemoAgents system demonstrates a modern multi-agent architecture where specialized agents collaborate through standardized protocols to solve complex business problems.

### Components

#### 1. NeMo Data Analysis Agent (Python)
- **Role**: Analyzes structured data to extract insights
- **Technology**: NVIDIA NeMo Agent Toolkit
- **Capabilities**:
  - Time-series trend analysis
  - Statistical anomaly detection
  - KPI calculation
  - AI-driven insights generation
- **Communication**: A2A (JSON-RPC) endpoint on port 8088

#### 2. MAF Action Agent (.NET)
- **Role**: Executes actions based on analysis recommendations
- **Technology**: Microsoft Agent Framework
- **Capabilities**:
  - Action routing and execution
  - Alert/notification triggering
  - Report generation
  - Service discovery
- **Communication**: A2A (JSON-RPC) endpoint on port 5055

#### 3. Web Chat Interface (.NET)
- **Role**: Provides human-friendly access to both agents
- **Technology**: Blazor + ASP.NET Core
- **Capabilities**:
  - Multi-turn conversations
  - Agent discovery & health monitoring
  - Real-time analysis visualization
  - Action tracking
- **Communication**: REST + WebSocket on port 5000

#### 4. Azure Aspire (Orchestration)
- **Role**: Manages service discovery, health, and tracing
- **Capabilities**:
  - Automatic service registration
  - Dependency management
  - Health check aggregation
  - OTEL distributed tracing

## Data Flow Architecture

### Current Chat Routing Logic (implemented)

The chat endpoint (`/api/chat`) does **not** always call both agents in sequence.

Routing behavior in `src/WebChatInterface/Program.cs`:

1. If message intent looks like an explicit action request, Web UI calls **MAF** first:
   - must include one of: `alert`, `report`, `action`, `trigger`
   - and start with one of: `trigger`, `generate`, `send`, `create`, `execute`, `run`
2. Otherwise Web UI calls **NeMo** for analysis.
3. If the MAF action call fails, Web UI falls back to NeMo.

### Example Workflow: "Analyze Q4 Sales Data"

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. User Chat Request                                             │
│    "Analyze Q4 sales and alert if anomalies found"              │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           v
┌─────────────────────────────────────────────────────────────────┐
│ 2. Web UI - Parse Intent & Route                                │
│    • Determine this requires NeMo analysis + MAF action         │
│    • Prepare analysis request payload                            │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           v
┌─────────────────────────────────────────────────────────────────┐
│ 3. Web UI → NeMo Agent (A2A Request #1)                         │
│    POST http://127.0.0.1:8088/a2a/nemo-agent                   │
│    {                                                            │
│      "jsonrpc": "2.0",                                          │
│      "method": "analyze_sales_data",                            │
│      "params": {                                                │
│        "data": [...Q4 sales data...],                           │
│        "analysis_types": ["trend", "anomalies", "insights"]    │
│      }                                                          │
│    }                                                            │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           v
┌─────────────────────────────────────────────────────────────────┐
│ 4. NeMo Agent - Analysis Processing                             │
│    • Load Q4 sales data                                         │
│    • Detect trends: Revenue ↑12% (strong upward)               │
│    • Detect anomalies: 3 days with 5σ deviations found         │
│    • Generate insights: "Anomalies during promotional period"  │
│    • Recommend action: "Send high-priority alert"              │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           v
┌─────────────────────────────────────────────────────────────────┐
│ 5. NeMo → Web UI (A2A Response #1)                              │
│    Analysis Result:                                             │
│    {                                                            │
│      "trend": "increasing",                                     │
│      "anomaly_count": 3,                                        │
│      "insights": ["High variability during promos"],            │
│      "recommendation": "send_alert"                             │
│    }                                                            │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           v
┌─────────────────────────────────────────────────────────────────┐
│ 6. Web UI → MAF Agent (A2A Request #2)                          │
│    POST http://127.0.0.1:5055/a2a/maf-agent                    │
│    {                                                            │
│      "jsonrpc": "2.0",                                          │
│      "method": "trigger_alert",                                │
│      "params": {                                                │
│        "level": "HIGH",                                         │
│        "anomalies": 3,                                          │
│        "insights": [...]                                        │
│      }                                                          │
│    }                                                            │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           v
┌─────────────────────────────────────────────────────────────────┐
│ 7. MAF Agent - Action Execution                                 │
│    • Route alert to Slack/email/webhooks                       │
│    • Generate incident report                                  │
│    • Log action execution                                      │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           v
┌─────────────────────────────────────────────────────────────────┐
│ 8. MAF → Web UI (A2A Response #2)                               │
│    Action Status:                                               │
│    {                                                            │
│      "success": true,                                           │
│      "action_id": "alert-q4-sales-001",                        │
│      "status": "executed",                                      │
│      "message": "Alert sent to operations team"                │
│    }                                                            │
└──────────────────────────┬──────────────────────────────────────┘
                           │
                           v
┌─────────────────────────────────────────────────────────────────┐
│ 9. Web UI → User                                                │
│    "Analysis complete:"                                         │
│    "• Trend: Strong upward (+12%)"                             │
│    "• Anomalies: 3 found during promotional period"             │
│    "• Action: High-priority alert sent to operations team"     │
└─────────────────────────────────────────────────────────────────┘
```

## Communication Protocol: Agent-to-Agent (A2A)

### Discovery
Each agent exposes a standard agent card:
```bash
GET /.well-known/agent-card.json
```

Response:
```json
{
  "name": "NeMo Data Analysis Agent",
  "description": "Analyzes data and generates insights",
  "version": "1.0.0",
  "capabilities": ["analyze", "detect_anomalies", "generate_insights"],
  "endpoint": "http://127.0.0.1:8088/a2a/nemo-agent",
  "a2a_version": "1.0"
}
```

### JSON-RPC 2.0 Communication
```json
{
  "jsonrpc": "2.0",
  "method": "method_name",
  "params": {...},
  "id": "request-uuid"
}
```

Response:
```json
{
  "jsonrpc": "2.0",
  "result": {...},
  "id": "request-uuid"
}
```

## Service Discovery & Health

### Health Check Endpoints
- **NeMo**: `GET /health/live` - Liveness probe
- **MAF**: `GET /health` - Overall health
- **Web UI**: `GET /health` - Overall health

### Aspire Integration
- Automatic service registration in Aspire dashboard
- Health check aggregation
- Dependency tracking (Web UI waits for NeMo + MAF)
- Distributed tracing correlation

## Deployment Patterns

### Pattern 1: Local Development (Aspire)
```
aspire start → Aspire Dashboard (http://localhost:18888)
  ├── NeMo Agent (Python executable)
  ├── MAF Agent (.NET web app)
  └── Web UI (.NET web app)
```

### Pattern 2: Container Deployment (Docker)
```
docker-compose up
  ├── nemo-agent (Python container)
  ├── maf-agent (.NET container)
  ├── web-ui (.NET container)
  └── otel-collector (observability)
```

### Pattern 3: Kubernetes (Future)
```
kubectl apply -f manifests/
  ├── nemo-agent-deployment.yaml
  ├── maf-agent-deployment.yaml
  ├── web-ui-deployment.yaml
  └── services.yaml
```

## Error Handling & Resilience

### Retry Logic
- **Transient Errors**: Exponential backoff (1s, 2s, 4s, 8s, 16s)
- **Max Retries**: 3 attempts
- **Circuit Breaker**: After 5 consecutive failures, circuit opens for 30s

### Fallback Behavior
- Analysis timeout → Use last known good result + warning
- Action failure → Log incident, notify operations
- Service unavailable → Graceful degradation

## Observability

### Tracing
- **OTEL Instrumentation**: All HTTP clients and servers
- **Correlation IDs**: Propagated across A2A calls
- **Span Context**: Parent-child relationships tracked

### Metrics
- Request latency (p50, p95, p99)
- Throughput (requests/sec)
- Error rates (by error type)
- Agent availability (uptime %)

### Logging
- Structured JSON logging
- Log levels: DEBUG, INFO, WARNING, ERROR
- Correlation ID in all logs
- Sensitive data redaction

## Security Considerations

### Current (Development)
- Local-only communication (127.0.0.1)
- No authentication
- No encryption

### Roadmap (Production)
- TLS 1.3 for all A2A communication
- Mutual TLS (mTLS) with certificates
- OAuth 2.0 for user authentication
- RBAC for action authorization
- Audit logging for all actions

## Scalability

### Horizontal Scaling
- NeMo: Run multiple instances, load balance on port 8088
- MAF: Stateless, scale replicas behind load balancer
- Web UI: Stateless, scale behind reverse proxy

### Performance Optimization
- Data caching (Redis for analysis results)
- Batch analysis requests
- Async action execution
- Connection pooling

## Deployment Checklist

- [ ] Environment variables configured
- [ ] LLM provider credentials set
- [ ] Health checks passing for all services
- [ ] OTEL endpoint configured
- [ ] Logs flowing to central aggregator
- [ ] Metrics dashboard configured
- [ ] Backup/recovery procedures tested
- [ ] Failover scenarios documented
