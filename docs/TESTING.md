# Testing

This guide covers testing the MAF-A2A-NVIDIA-NemoAgents system.

## Unit & Integration Tests

### Running All Tests

```bash
# Run entire test suite
dotnet test

# Run with verbose output
dotnet test --verbosity detailed

# Run specific test project
dotnet test src/Tests/Integration.Tests

# Run specific test class
dotnet test --filter "ClassName=AgentOrchestratorTests"
```

### Test Projects

- **`src/Tests/Unit.Tests`** - Component-level tests (agents, services, models)
- **`src/Tests/Integration.Tests`** - End-to-end agent communication tests
- **`src/Tests/Api.Tests`** - REST API endpoint tests

## Code Coverage

### Generate Coverage Report

```bash
# Generate OpenCover report
dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover

# Generate detailed HTML report
dotnet test /p:CollectCoverage=true /p:CoverageFormat=cobertura
```

### Coverage Targets

- **Overall**: ≥80% coverage required
- **Critical Paths**: ≥95% coverage
  - Agent orchestration logic
  - A2A communication protocol
  - Error handling & resilience

## Manual Testing

### Test Checklist

Before deployment, verify:

- [ ] **NeMo Agent Discovery** - Agent card loads successfully
  ```bash
  curl http://127.0.0.1:8088/.well-known/agent-card.json
  ```

- [ ] **MAF Agent Discovery** - Health endpoint responds
  ```bash
  curl http://127.0.0.1:5055/health
  ```

- [ ] **Web UI Loads** - No console errors
  - Open <http://localhost:5000>
  - Check browser console (F12)

- [ ] **Agent Communication** - A2A protocol works
  ```bash
  # Request analysis from NeMo
  curl -X POST http://127.0.0.1:8088/ \
    -H "Content-Type: application/json" \
    -d '{"method":"analyze","params":{"metric":"revenue"}}'
  ```

- [ ] **Chat API** - Endpoint responds
  ```bash
  curl -X POST http://localhost:5000/api/chat \
    -H "Content-Type: application/json" \
    -d '{"message":"Analyze Q4 sales"}'
  ```

### Testing Predefined Questions

The chat interface includes predefined test questions. Click any question to send it through the system:

- "Analyze quarterly revenue trends"
- "Detect anomalies in system performance"
- "Generate monthly metrics report"
- "Trigger alert for high CPU usage"
- "Compare year-over-year growth"

### Verify NeMo vs MAF Routing

Use these quick checks against `/api/chat`:

```bash
# Expected respondedBy: "NeMo Data Analysis Agent"
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"Analyze quarterly revenue trends"}'

# Expected respondedBy: "MAF Action Agent"
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"Trigger alert for high CPU usage"}'
```

### Playwright Latency Test (Web UI flow)

This repository includes a browser-based latency test for the exact chat flow:

```bash
npx playwright test tests/playwright/chat-latency.spec.ts
```

### Playwright 2-Prompt Chain Test (NeMo -> MAF)

Validates the session workflow where analysis is executed first and action execution uses the prior analysis context:

```bash
npx playwright test tests/playwright/chat-chain.spec.ts
```

## Performance Testing

### Load Testing with Apache Bench

```bash
# Install Apache Bench (if not present)
# macOS: brew install httpd
# Linux: apt-get install apache2-utils
# Windows: Use WSL or alternative

# Test chat endpoint with 100 requests, 10 concurrent
ab -n 100 -c 10 http://localhost:5000/api/chat
```

### Load Testing with k6 (Advanced)

```bash
# Install k6
# macOS: brew install k6
# Linux: sudo apt-get install k6
# Windows: choco install k6

# Create test script (save as test.js)
import http from 'k6/http';
import { check } from 'k6';

export let options = {
  vus: 10,
  duration: '30s',
};

export default function () {
  let response = http.post('http://localhost:5000/api/chat', {
    message: 'Analyze sales data',
  });
  check(response, {
    'status is 200': (r) => r.status === 200,
  });
}

# Run test
k6 run test.js
```

## Agent-Specific Tests

### NeMo Agent Tests

```bash
# Test NeMo analysis capabilities
cd src/NemoDataAnalysisAgent

# Run Python tests
python -m pytest tests/ -v

# Test workflow file validity
..\..\.venv\Scripts\nat.exe validate --config_file nemo/workflow.yml
```

### MAF Agent Tests

```bash
# Test action execution
dotnet test src/Tests/MafAgent.Tests

# Specific action handler tests
dotnet test --filter "ActionType=Alert" src/Tests/MafAgent.Tests
```

## Debugging Tests

### Run with Debug Output

```bash
# Show all logging output
dotnet test --logger "console;verbosity=detailed"

# Capture logs to file
dotnet test --logger "trx;LogFileName=test-results.trx"
```

### Attach Debugger

In Visual Studio:

1. Go to **Test** → **Debug All Tests** (or specific test)
2. Set breakpoints as needed
3. Debug output will appear in Debug window

## Continuous Integration

### GitHub Actions

Tests run automatically on:
- Every push to `main` branch
- Every pull request
- Nightly scheduled run

See `.github/workflows/test.yml` for CI configuration.

## Known Issues & Workarounds

### "Connection Refused" in Tests

- Ensure all services are running before integration tests
- Tests require: NeMo on 8088, MAF on 5055, Web UI on 5000

### "Timeout" in Agent Communication Tests

- Increase timeout if running on slow hardware
- Check network connectivity between test agent instances

### Flaky Tests

Tests may be flaky if:
- Running with limited system resources
- High system load
- Network latency

Retry failed tests:
```bash
dotnet test --logger "trx" --collection-level=class
```

## Next Steps

- See [Configuration](./CONFIGURATION.md) to validate settings
- See [Manual Startup](./MANUAL-STARTUP.md) to set up test environment
- See [Deployment](./DEPLOYMENT.md) for production validation
