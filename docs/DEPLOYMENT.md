# Deployment

This guide covers deploying the MAF-A2A-NVIDIA-NemoAgents system to production and staging environments.

## Local Development with Aspire

### Prerequisites

- **Aspire CLI** installed: <https://aspire.dev/get-started/install-cli/>
- All services configured (see [Configuration](./CONFIGURATION.md))

### Starting with Aspire

```bash
# Start all services with orchestration
aspire start

# Aspire will:
# 1. Start all components in correct order
# 2. Expose Aspire Dashboard at http://localhost:18888
# 3. Manage health checks and dependencies
# 4. Provide unified logging and tracing
```

### Aspire Dashboard

Access dashboard at: **<http://localhost:18888>**

Features:
- **Service Status**: Real-time health of all components
- **Logs**: Aggregated logs from all services
- **Traces**: Distributed tracing with OTEL
- **Metrics**: Performance metrics per service
- **Resource Usage**: CPU, memory, network monitoring

## Docker Deployment

### Building Images

```bash
# Build all containers
docker-compose build

# Build specific service
docker build -t maf-web-ui:latest -f src/WebChatInterface/Dockerfile .
docker build -t maf-agent:latest -f src/MafActionAgent/Dockerfile .
docker build -t nemo-agent:latest -f src/NemoDataAnalysisAgent/Dockerfile .
```

### Running with Docker Compose

```bash
# Start all services
docker-compose up -d

# View logs
docker-compose logs -f

# Stop services
docker-compose down
```

### Docker Compose Configuration

See `docker-compose.yml` for:
- Service definitions
- Port mappings
- Volume mounts
- Environment variable configuration
- Health checks
- Startup order

## Azure Container Instances (ACI)

### Prerequisites

- Azure CLI installed: <https://learn.microsoft.com/cli/azure>
- Azure subscription with ACR (Azure Container Registry)
- Resource group created

### Deploy Script

```bash
./scripts/deploy-aci.ps1 -ResourceGroup my-resource-group -Environment staging
```

### Manual Deployment

```bash
# Log in to Azure
az login

# Create resource group
az group create --name maf-agents-rg --location eastus

# Create ACR
az acr create --resource-group maf-agents-rg \
  --name mafagentsacr --sku Basic

# Build and push images
az acr build --registry mafagentsacr \
  --image maf-web-ui:latest -f src/WebChatInterface/Dockerfile .

# Deploy container group
az container create \
  --resource-group maf-agents-rg \
  --name maf-agents-deployment \
  --image mafagentsacr.azurecr.io/maf-web-ui:latest \
  --ports 5000 80 \
  --environment-variables ENABLE_OTEL_TRACING=true
```

## Kubernetes Deployment

### Prerequisites

- `kubectl` configured
- Kubernetes cluster available
- Container images in registry

### Helm Chart (Coming Soon)

Helm values for deploying to Kubernetes:

```yaml
nemoAgent:
  replicas: 2
  resources:
    requests:
      cpu: 500m
      memory: 512Mi

mafAgent:
  replicas: 2
  resources:
    requests:
      cpu: 250m
      memory: 256Mi

webUI:
  replicas: 3
  resources:
    requests:
      cpu: 100m
      memory: 128Mi
```

## Production Configuration

### Security

- [ ] Use HTTPS with valid certificates
- [ ] Enable TLS 1.3+ for all endpoints
- [ ] Set up API gateway with authentication
- [ ] Rotate credentials regularly
- [ ] Use secrets management (Azure Key Vault, HashiCorp Vault)

### Performance

- [ ] Enable HTTP/2 and HTTP/3 support
- [ ] Configure CDN for static assets
- [ ] Set up load balancing across service replicas
- [ ] Enable caching where appropriate
- [ ] Tune connection pool sizes

### Monitoring & Observability

- [ ] Configure OTEL exporter to production collector
- [ ] Set up log aggregation (ELK, Datadog, etc.)
- [ ] Configure alerts for critical metrics
- [ ] Set up distributed tracing
- [ ] Monitor service dependencies

### High Availability

- [ ] Run multiple replicas of each service
- [ ] Use managed databases with replication
- [ ] Configure auto-scaling policies
- [ ] Set up health checks
- [ ] Implement circuit breakers

## Production Startup Sequence

When deploying to production:

```bash
# 1. Start orchestration layer
aspire start

# 2. Verify all services healthy (via dashboard)
# 3. Run smoke tests
./scripts/smoke-tests.ps1

# 4. Monitor logs for errors
# 5. Gradually increase traffic (blue-green or canary)
```

## Backup & Recovery

### Database Backups

```bash
# Back up configuration
cp .env .env.backup
cp src/NemoDataAnalysisAgent/nemo/workflow.yml workflow.yml.backup

# For stateful components, implement persistence:
# - Query results cache
# - Analysis history
# - User preferences
```

### Recovery Procedure

```bash
# 1. Restore configuration
cp .env.backup .env

# 2. Restart services
docker-compose restart

# 3. Verify health
curl http://localhost:5000/health
curl http://localhost:5055/health
```

## Scaling Strategies

### Horizontal Scaling

Deploy multiple instances:

```yaml
services:
  web-ui:
    replicas: 3  # Scale to 3 instances
  nemo-agent:
    replicas: 2  # Scale to 2 instances
  maf-agent:
    replicas: 2  # Scale to 2 instances
```

### Vertical Scaling

Increase resources for single instance:

```yaml
resources:
  requests:
    cpu: "2"
    memory: "4Gi"
  limits:
    cpu: "4"
    memory: "8Gi"
```

## Cost Optimization

### Development Environment

- Use managed services (Azure App Service, Cosmos DB)
- Set auto-shutdown for non-production environments
- Use spot instances where applicable

### Production Environment

- Use reserved instances for predictable workloads
- Implement auto-scaling based on metrics
- Regular cost analysis and optimization
- Use CDN for static asset delivery

## Troubleshooting Deployment

### Service Won't Start

```bash
# Check logs
docker-compose logs service-name

# Verify configuration
env | grep -i config_var

# Check port availability
netstat -ano | findstr :5000
```

### Connection Failures Between Services

```bash
# Verify service discovery
curl http://nemo-agent:8088/.well-known/agent-card.json

# Check network configuration
docker network ls
docker network inspect maf-agents_default
```

### Performance Issues

- Check resource utilization in monitoring dashboard
- Review OTEL traces for bottlenecks
- Verify database query performance
- Check network latency between services

## Next Steps

- See [Configuration](./CONFIGURATION.md) for environment setup
- See [Manual Startup](./MANUAL-STARTUP.md) for development
- See [Testing](./TESTING.md) for validation procedures
- See [Architecture](../README.md#-system-architecture) for design details
