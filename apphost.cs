#:sdk Aspire.AppHost.Sdk@10.0.0

using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Get configuration - LLM Provider Secrets
var nvidiaApiKey = builder.AddParameter("nvidia-api-key", secret: true);
var azureOpenAiEndpoint = builder.AddParameter("azure-openai-endpoint", secret: true);
var azureOpenAiDeploymentName = builder.AddParameter("azure-openai-deployment-name", secret: true);
var azureOpenAiApiKey = builder.AddParameter("azure-openai-api-key", secret: true);

var dashboardOtlpHttpEndpoint = builder.Configuration["ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"];
var dashboardOtlpGrpcEndpoint = builder.Configuration["ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"];
var nemoOtelEndpoint = !string.IsNullOrWhiteSpace(dashboardOtlpHttpEndpoint)
    ? $"{dashboardOtlpHttpEndpoint.TrimEnd('/')}/v1/traces"
    : dashboardOtlpGrpcEndpoint ?? "http://localhost:4317";

// NeMo Data Analysis Agent (Executable - Python)
var nemo = builder.AddExecutable(
        name: "nemo-agent",
        command: "powershell",
        workingDirectory: ".",
        args: new[]
        {
            "-NoProfile",
            "-Command",
            "if (Test-Path '.\\.venv\\Scripts\\nat.exe') { & '.\\.venv\\Scripts\\nat.exe' a2a serve --config_file .\\src\\NemoDataAnalysisAgent\\nemo\\workflow.yml --host 127.0.0.1 --port 8088 --name \"nemo-data-analysis-agent\" } else { nat a2a serve --config_file .\\src\\NemoDataAnalysisAgent\\nemo\\workflow.yml --host 127.0.0.1 --port 8088 --name \"nemo-data-analysis-agent\" }"
        })
    .WithHttpEndpoint(name: "http", env: "NEMO_PORT", hostPort: 8088)
    .WithEnvironment("NEMO_HOST", "127.0.0.1")
    .WithEnvironment("NEMO_PORT", "8088")
    .WithEnvironment("NVIDIA_API_KEY", nvidiaApiKey)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAiEndpoint)
    .WithEnvironment("AZURE_OPENAI_DEPLOYMENT_NAME", azureOpenAiDeploymentName)
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithEnvironment("NEMO_OTEL_TRACES_ENDPOINT", nemoOtelEndpoint)
    .WithEnvironment("NEMO_OTEL_PROJECT", "nemo-data-analysis-agent")
    .WithEnvironment("NEMO_LOG_LEVEL", "INFO")
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "http://127.0.0.1:8088/.well-known/agent-card.json";
        url.DisplayText = "Agent Card";
    });

// MAF Action Agent (.NET)
var mafAgent = builder.AddProject<Projects.MafActionAgent>(name: "maf-agent")
    .WithHttpEndpoint(name: "http", env: "MAF_PORT", hostPort: 5055)
    .WithEnvironment("MAF_HOST", "127.0.0.1")
    .WithEnvironment("MAF_PORT", "5055")
    .WithEnvironment("NEMO_A2A_ENDPOINT", nemo.GetEndpoint("http"))
    .WithEnvironment("NVIDIA_API_KEY", nvidiaApiKey)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAiEndpoint)
    .WithEnvironment("AZURE_OPENAI_DEPLOYMENT_NAME", azureOpenAiDeploymentName)
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithEnvironment("ENABLE_OTEL_TRACING", "true")
    .WithEnvironment("ASPIRE_RESOURCE_SERVICE_BINDING_OTEL_EXPORTER_OTLP_ENDPOINT", nemoOtelEndpoint)
    .WaitFor(nemo)
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "/health";
        url.DisplayText = "Health";
    });

// Web Chat Interface (.NET)
var webUi = builder.AddProject<Projects.WebChatInterface>(name: "web-ui")
    .WithHttpEndpoint(name: "http", env: "WEB_UI_PORT", hostPort: 5000)
    .WithEnvironment("NEMO_A2A_ENDPOINT", nemo.GetEndpoint("http"))
    .WithEnvironment("MAF_AGENT_ENDPOINT", mafAgent.GetEndpoint("http"))
    .WithEnvironment("NVIDIA_API_KEY", nvidiaApiKey)
    .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAiEndpoint)
    .WithEnvironment("AZURE_OPENAI_DEPLOYMENT_NAME", azureOpenAiDeploymentName)
    .WithEnvironment("AZURE_OPENAI_API_KEY", azureOpenAiApiKey)
    .WithEnvironment("ENABLE_OTEL_TRACING", "true")
    .WithEnvironment("ASPIRE_RESOURCE_SERVICE_BINDING_OTEL_EXPORTER_OTLP_ENDPOINT", nemoOtelEndpoint)
    .WaitFor(nemo)
    .WaitFor(mafAgent)
    .WithUrlForEndpoint("http", url =>
    {
        url.Url = "/";
        url.DisplayText = "Web UI";
    });

// Build and run
builder.Build().Run();

// Project references for Aspire
namespace Projects
{
    public class MafActionAgent { }
    public class WebChatInterface { }
}
