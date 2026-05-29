# MAF-A2A-NVIDIA-NemoAgents: Multi-Agent Data Analysis & Action System

> A production-ready sample demonstrating **NVIDIA NeMo Agent Toolkit** + **Microsoft Agent Framework (MAF)** with **Agent-to-Agent (A2A)** communication, orchestrated with **Aspire**.

## 🎯 The Scenario: Data Analysis Meets Action Execution

Modern enterprises need systems that can **analyze complex data in real-time and immediately take action** without manual handoffs.

This repository demonstrates a practical workflow:

1. **Data Analysis Agent (NeMo)**: Receives raw business data (sales metrics, system performance, anomalies)
2. **Analysis Results**: Detects trends, identifies anomalies, calculates KPIs using ML/statistics
3. **Action Orchestration**: Web UI requests NeMo analysis first, then optionally invokes the Action Agent for explicit action intents
4. **Action Agent (MAF)**: Executes remediation—sends alerts, generates reports, triggers escalations
5. **User Interface**: Chat-based interface for humans to request analysis and observe actions in real-time

### Why Two Agents?

- **Separation of Concerns**: Data analysis expertise ≠ action execution expertise
- **Scalability**: Agents can be deployed independently, scaled horizontally
- **Reliability**: Failure in one agent doesn't cascade; degraded mode still possible
- **Vendor Flexibility**: Mix and match toolkits (NeMo for analysis, MAF for orchestration)

---

<img src="./images/MAF-A2A-Nemo-Demo02.gif" alt="MAF A2A NeMo demo in Aspire" />

## ⚡ Quick Start

### 1) Clone repo

```bash
git clone https://github.com/yourusername/MAF-A2A-NVIDIA-NemoAgents.git
cd MAF-A2A-NVIDIA-NemoAgents
```

### 2) Create and activate virtual environment

**⚠️ Important**: The virtual environment must be created before running Aspire. Aspire uses the venv's Python executable to run the NeMo agent.

```bash
python -m venv .venv
```

Windows (PowerShell):

```powershell
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

Linux/macOS (bash):

```bash
source .venv/bin/activate
pip install -r requirements.txt
```

Prereqs: Python 3.10+, .NET 10 SDK, NVIDIA API key, Azure OpenAI credentials.

### 3) Run app

```bash
aspire start
```

Then open the Web UI at `http://localhost:5000`.

### 4) Try sample prompts

- `Analyze quarterly revenue trends` (**NeMo**)
- `Trigger alert for high CPU usage` (**MAF**)
- `Analyze quarterly revenue trends and then trigger alert for high CPU usage based on the analysis findings` (**NeMo + MAF**, 2-step demo intent)

### Want more details?

- **Quick Start details**: [docs/QUICK-START-DETAILS.md](docs/QUICK-START-DETAILS.md)
- **Manual startup**: [docs/MANUAL-STARTUP.md](docs/MANUAL-STARTUP.md)
- **Configuration**: [docs/CONFIGURATION.md](docs/CONFIGURATION.md)
- **Testing**: [docs/TESTING.md](docs/TESTING.md)

---

## 🏗️ System Architecture

```mermaid
graph TB
    subgraph "Web Layer"
        UI["Chat UI<br/>(Blazor + ASP.NET)"]
    end
    
    subgraph "Agent Layer"
        NEMO["NeMo Data Analysis Agent<br/>(Python + Toolkit)<br/>Trends • Anomalies • Metrics"]
        MAF["MAF Action Agent<br/>(.NET + Azure OpenAI)<br/>Alerts • Reports • Actions"]
    end
    
    subgraph "Communication"
        A2A["A2A Protocol<br/>(JSON-RPC)<br/>Service Discovery"]
    end
    
    subgraph "Orchestration"
        ASPIRE["Aspire<br/>Health • Tracing • Discovery"]
    end
    
    UI -->|Chat Request| A2A
    A2A -->|Analysis Request| NEMO
    NEMO -->|Analysis Results| A2A
    A2A -->|Action Trigger| MAF
    MAF -->|Action Status| A2A
    A2A -->|Display Results| UI
    
    ASPIRE -.->|Health Checks<br/>Service Discovery<br/>OTEL Tracing| NEMO
    ASPIRE -.->|Health Checks<br/>Service Discovery<br/>OTEL Tracing| MAF
    ASPIRE -.->|Health Checks<br/>Service Discovery<br/>OTEL Tracing| UI
```

### Key Integration Points

| Component | Role | Port | Technology |
|-----------|------|------|-----------|
| **NeMo Agent** | Data Analysis | Dynamic (Aspire) / 8088 (manual startup) | Python + NVIDIA NeMo Toolkit |
| **MAF Agent** | Action Execution | Dynamic (Aspire) / 5055 (manual startup) | .NET 10 + Microsoft Agent Framework |
| **Web UI** | User Interface | 5000 | Blazor + ASP.NET Core |
| **Aspire** | Orchestration | Dashboard | Service discovery, health, OTEL tracing |

---

## 📋 Features

### NeMo Data Analysis Agent

✅ **Time-Series Analysis** - Trend detection using statistical methods  
✅ **Anomaly Detection** - Z-score-based outlier identification  
✅ **Metric Calculation** - Comprehensive statistical summaries (mean, percentiles, variance)  
✅ **Insight Generation** - AI-driven business recommendations  
✅ **A2A Exposure** - JSON-RPC endpoint for cross-agent communication  
✅ **Dual Provider Support** - NVIDIA API (NIM) + Azure OpenAI  

### MAF Action Agent

✅ **Action Execution** - Pluggable action handlers  
✅ **Alert Triggering** - Multi-level alerts (Critical/High/Medium/Low)  
✅ **Report Generation** - Async report creation  
✅ **A2A Integration** - Agent discovery + JSON-RPC communication  
✅ **Health Checks** - Liveness, readiness, startup probes  
✅ **OpenTelemetry** - Distributed tracing with GenAI-labeled MAF spans (`maf.gen_ai.*`)  

### Web Chat Interface

✅ **Real-Time Chat** - Interactive multi-turn conversations  
✅ **Agent Discovery** - Auto-discovery of NeMo + MAF agents  
✅ **Analysis Display** - Structured presentation of insights  
✅ **Action Monitoring** - Track real-time action execution  
✅ **Service Health** - Dashboard showing agent status  
✅ **NeMo-First Routing** - Chat responses come from NeMo analysis before optional MAF action execution  
✅ **Bounded Agent Calls** - Web UI cancels NeMo/MAF HTTP calls when configured timeout is reached to avoid runaway waits  

### Aspire Orchestration

✅ **Service Discovery** - Automatic agent endpoint registration  
✅ **Dependency Management** - Ensure startup ordering (NeMo → MAF → Web UI)  
✅ **Health Monitoring** - Continuous liveness & readiness checks  
✅ **Distributed Tracing** - OTEL correlation across all services  
✅ **Unified Logs** - Single pane of glass for all service logs  
✅ **NeMo Pre-Warm** - Fire a synthetic startup request so the first user prompt is faster  

## 📚 Documentation

- **[Quick Start Details](docs/QUICK-START-DETAILS.md)** - Extended startup, routing, and workflow notes.
- **[Architecture Guide](docs/README-ARCHITECTURE.md)** - Deep dive into system design
- **[Configuration Guide](docs/CONFIGURATION.md)** - Environment variables, provider credentials, and service wiring details.
- **[Testing Guide](docs/TESTING.md)** - Commands and workflows for unit, integration, and manual validation.
- **[Deployment Guide](docs/DEPLOYMENT.md)** - Azure Container Apps and Docker Compose deployment flows.
- **[Contributing](docs/development/CONTRIBUTING.md)** - Development guidelines
- **[ADRs](docs/development/architecture-decisions.md)** - Architecture decision records

---

## 🛠️ Tech Stack

### NeMo Agent

- **NVIDIA NeMo Agent Toolkit** `1.0.0+`
- **Python** `3.10+`
- **FastAPI** - HTTP server
- **pandas/numpy/scikit-learn** - Data analysis
- **OpenTelemetry** - Distributed tracing

### MAF Agent & Web UI

- **.NET 10** - Runtime
- **ASP.NET Core** - Web framework
- **Microsoft Agent Framework** `1.0.0+`
- **Blazor** - Interactive UI
- **OpenTelemetry** - Distributed tracing

### Orchestration

- **Aspire** `13.0.0+`
- **Docker Compose**
- **Azure Container Apps**

---

## 📊 Architecture Highlights

For deep dive into communication protocols, observability patterns, component architecture, and scaling strategies, see **[Architecture Highlights](docs/ARCHITECTURE-HIGHLIGHTS.md)**.

Key highlights:

- **Agent-to-Agent Communication**: JSON-RPC 2.0 protocol with service discovery
- **Observability**: OpenTelemetry distributed tracing, structured logging, metrics
- **Resilience**: Circuit breakers, retry logic, graceful degradation
- **Scalability**: Horizontal and vertical scaling strategies
- **Security**: Roadmap for TLS, mutual authentication, authorization

See [Architecture Highlights](docs/ARCHITECTURE-HIGHLIGHTS.md) for complete details including:

- Communication protocols and patterns
- Component architecture diagrams
- Data flow examples
- Performance optimization strategies
- Failure modes and recovery procedures

---

## 🤝 Contributing

We welcome contributions! See [CONTRIBUTING.md](docs/development/CONTRIBUTING.md) for guidelines.

---

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

---

## 🔗 Resources

- [NVIDIA NeMo Agent Toolkit Docs](https://docs.nvidia.com/nemo/agent-toolkit/latest/)
- [Microsoft Agent Framework Docs](https://learn.microsoft.com/en-us/agent-framework/)
- [A2A v1 in Microsoft Agent Framework (.NET)](https://devblogs.microsoft.com/agent-framework/a2a-v1-is-here-cross-platform-agent-communication-in-microsoft-agent-framework-for-net/)
- [A2A Integration Guide](https://learn.microsoft.com/en-us/agent-framework/integrations/a2a)
- [Azure Aspire Docs](https://aspire.dev/)

---

## ❓ FAQ

**Q: Can I use a different LLM provider?**  
A: Yes! The workflow files support multiple providers. See [docs/SETUP-GUIDE.md](docs/SETUP-GUIDE.md#providers).

**Q: How do I add custom analysis tools to NeMo?**  
A: See [docs/development/CONTRIBUTING.md](docs/development/CONTRIBUTING.md#extending-nemo).

**Q: How do I deploy this to production?**  
A: See [docs/SETUP-GUIDE.md#production](docs/SETUP-GUIDE.md#production-deployment).

**Q: Is the A2A communication authenticated?**  
A: Currently no (local development). TLS + mutual auth is on the roadmap.

---

## 🎓 Learning Resources

This repository is designed to teach:

- ✅ How to build multi-agent systems with modern frameworks
- ✅ Agent-to-Agent communication patterns
- ✅ Microservices orchestration with Aspire
- ✅ Distributed tracing and observability
- ✅ Production-ready Python + .NET integration

---

**Happy coding! 🚀**
