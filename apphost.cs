#:sdk Aspire.AppHost.Sdk@13.3.4

using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Get configuration - LLM Provider Secrets
var nvidiaApiKey = builder.AddParameter("nvidia-api-key", secret: true);
var azureOpenAiEndpoint = builder.AddParameter("azure-openai-endpoint", secret: true);
var azureOpenAiDeploymentName = builder.AddParameter("azure-openai-deployment-name", secret: true);
var azureOpenAiApiKey = builder.AddParameter("azure-openai-api-key", secret: true);

// NeMo Data Analysis Agent (Executable - Python)
var nemo = builder.AddExecutable(
        name: "nemo-agent",
        command: "powershell",
        workingDirectory: ".",
        args: new[]
        {
            "-NoProfile",
            "-Command",
            "$env:OTEL_EXPORTER_OTLP_ENDPOINT = if (-not [string]::IsNullOrWhiteSpace($env:ASPIRE_RESOURCE_SERVICE_BINDING_OTEL_EXPORTER_OTLP_ENDPOINT)) { $env:ASPIRE_RESOURCE_SERVICE_BINDING_OTEL_EXPORTER_OTLP_ENDPOINT } else { $env:OTEL_EXPORTER_OTLP_ENDPOINT }; $env:OTEL_EXPORTER_OTLP_HEADERS = if (-not [string]::IsNullOrWhiteSpace($env:ASPIRE_RESOURCE_SERVICE_BINDING_OTEL_EXPORTER_OTLP_HEADERS)) { $env:ASPIRE_RESOURCE_SERVICE_BINDING_OTEL_EXPORTER_OTLP_HEADERS } else { $env:OTEL_EXPORTER_OTLP_HEADERS }; $certCandidate = Get-ChildItem -Path $env:SSL_CERT_DIR -Filter '*.pem' -ErrorAction SilentlyContinue | Select-Object -First 1; if ($null -eq $certCandidate) { $certCandidate = Get-ChildItem -Path $env:SSL_CERT_DIR -Filter '*.0' -ErrorAction SilentlyContinue | Select-Object -First 1 }; if ($null -ne $certCandidate) { $env:GRPC_DEFAULT_SSL_ROOTS_FILE_PATH = $certCandidate.FullName }; $workflowProfile = if ([string]::IsNullOrWhiteSpace($env:NEMO_WORKFLOW_PROFILE)) { 'standard' } else { $env:NEMO_WORKFLOW_PROFILE.ToLowerInvariant() }; $workflowFile = if ($workflowProfile -eq 'fast') { '.\\src\\NemoDataAnalysisAgent\\nemo\\workflow-fast.yml' } else { '.\\src\\NemoDataAnalysisAgent\\nemo\\workflow.yml' }; if (Test-Path '.\\.venv\\Scripts\\nat.exe') { & '.\\.venv\\Scripts\\nat.exe' a2a serve --config_file $workflowFile --host $env:NEMO_HOST --port $env:NEMO_PORT --public_base_url $env:NEMO_PUBLIC_BASE_URL --name \"nemo-data-analysis-agent\" } else { nat a2a serve --config_file $workflowFile --host $env:NEMO_HOST --port $env:NEMO_PORT --public_base_url $env:NEMO_PUBLIC_BASE_URL --name \"nemo-data-analysis-agent\" }"
        })
    .WithHttpEndpoint(name: "http", env: "NEMO_PORT")
    .WithEnvironment("NEMO_HOST", "127.0.0.1")
    .WithEnvironment("NEMO_WORKFLOW_PROFILE", "fast")
    .WithEnvironment("NEMO_FAST_MODEL_NAME", "meta/llama-3.2-3b-instruct")
    .WithEnvironment("NVIDIA_API_KEY", nvidiaApiKey)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAiEndpoint)
    .WithEnvironment("AZURE_OPENAI_DEPLOYMENT_NAME", azureOpenAiDeploymentName)
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithOtlpExporter()
    .WithEnvironment("NEMO_OTEL_PROJECT", "nemo-data-analysis-agent")
    .WithEnvironment("NEMO_LOG_LEVEL", "INFO")
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "/.well-known/agent-card.json";
        url.DisplayText = "Agent Card";
    });

nemo.WithEnvironment("NEMO_PUBLIC_BASE_URL", nemo.GetEndpoint("http"));

// MAF Action Agent (.NET)
var mafAgent = builder.AddExecutable(
        name: "maf-agent",
        command: "dotnet",
        workingDirectory: ".",
        args: new[]
        {
            "run",
            "--project",
            ".\\src\\MafActionAgent\\MafActionAgent.csproj"
        })
    .WithHttpEndpoint(name: "http", env: "MAF_PORT")
    .WithEnvironment("MAF_HOST", "127.0.0.1")
    .WithEnvironment("NEMO_A2A_ENDPOINT", nemo.GetEndpoint("http"))
    .WithEnvironment("NVIDIA_API_KEY", nvidiaApiKey)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAiEndpoint)
    .WithEnvironment("AZURE_OPENAI_DEPLOYMENT_NAME", azureOpenAiDeploymentName)
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithEnvironment("ENABLE_OTEL_TRACING", "true")
    .WithOtlpExporter()
    .WaitFor(nemo)
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "/health";
        url.DisplayText = "Health";
    });

// Web Chat Interface (.NET)
var webUi = builder.AddExecutable(
        name: "web-ui",
        command: "dotnet",
        workingDirectory: ".",
        args: new[]
        {
            "run",
            "--project",
            ".\\src\\WebChatInterface\\WebChatInterface.csproj"
        })
    .WithHttpEndpoint(name: "http", env: "WEB_UI_PORT", port: 5000)
    .WithEnvironment("NEMO_A2A_ENDPOINT", nemo.GetEndpoint("http"))
    .WithEnvironment("MAF_AGENT_ENDPOINT", mafAgent.GetEndpoint("http"))
    .WithEnvironment("NVIDIA_API_KEY", nvidiaApiKey)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAiEndpoint)
    .WithEnvironment("AZURE_OPENAI_DEPLOYMENT_NAME", azureOpenAiDeploymentName)
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithEnvironment("ENABLE_OTEL_TRACING", "true")
    .WithEnvironment("NEMO_WARMUP_ENABLED", "false")
    .WithOtlpExporter()
    .WaitFor(nemo)
    .WaitFor(mafAgent)
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "/";
        url.DisplayText = "Web UI";
    });

// Build and run
builder.Build().Run();
