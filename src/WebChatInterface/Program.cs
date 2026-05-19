using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Shared.Models;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var configuration = builder.Configuration;
configuration
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

var webUiHost = configuration["WEB_UI_HOST"] ?? "127.0.0.1";
var webUiPort = configuration["WEB_UI_PORT"] ?? "5000";
builder.WebHost.UseUrls($"http://{webUiHost}:{webUiPort}");

// Logging
builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddConsole();
    loggingBuilder.AddDebug();
    var logLevel = Enum.TryParse<LogLevel>(configuration["WEB_UI_LOG_LEVEL"] ?? "Information", out var level)
        ? level
        : LogLevel.Information;
    loggingBuilder.SetMinimumLevel(logLevel);
});

var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting Web Chat Interface...");

// OpenTelemetry Setup
var otelEnabled = bool.TryParse(configuration["ENABLE_OTEL_TRACING"] ?? "true", out var enabled) && enabled;
if (otelEnabled)
{
    logger.LogInformation("Configuring OpenTelemetry tracing...");
    
    var resource = ResourceBuilder.CreateDefault()
        .AddService(serviceName: "web-chat-interface", serviceVersion: "1.0.0");
    var otlpEndpointConfigured = !string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

    builder.Services
        .AddOpenTelemetry()
        .WithTracing(tracing =>
        {
            tracing
                .SetResourceBuilder(resource)
                .AddAspNetCoreInstrumentation(opt => opt.RecordException = true)
                .AddHttpClientInstrumentation(opt => opt.RecordException = true);

            if (otlpEndpointConfigured)
            {
                tracing.AddOtlpExporter();
            }
        }
        );

    builder.Logging.AddOpenTelemetry(logging =>
    {
        logging.IncludeFormattedMessage = true;
        logging.IncludeScopes = true;
        logging.ParseStateValues = true;
        logging.SetResourceBuilder(resource);

        if (otlpEndpointConfigured)
        {
            logging.AddOtlpExporter();
        }
    });
}

// Add services
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddRazorPages();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// Add HTTP Client
builder.Services.AddHttpClient<IAgentClient, AgentClient>(client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// Add Health Checks
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" });

// Add Application Services
builder.Services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

// Health check endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});

// API Endpoints
app.MapPost("/api/chat", async (ChatRequest request, IChatService service) =>
{
    logger.LogInformation($"Chat request: {request.Message}");
    var response = await service.ProcessChatAsync(request);
    return Results.Ok(response);
})
.Produces<ChatResponse>()
.WithName("ChatWithAgents")
.WithOpenApi();

app.MapGet("/api/agents/discovery", async (IAgentOrchestrator orchestrator) =>
{
    logger.LogInformation("Discovering agents...");
    var services = await orchestrator.DiscoverAgentsAsync();
    return Results.Ok(services);
})
.Produces<List<ServiceDiscoveryResult>>()
.WithName("DiscoverAgents")
.WithOpenApi();

app.MapPost("/api/analysis/request", async (AnalysisRequest request, IAgentOrchestrator orchestrator) =>
{
    logger.LogInformation($"Analysis request: {request.MetricName}");
    var result = await orchestrator.RequestAnalysisAsync(request);
    return Results.Ok(result);
})
.Produces<Dictionary<string, object>>()
.WithName("RequestAnalysis")
.WithOpenApi();

app.MapPost("/api/actions/trigger", async (ActionRequest request, IAgentOrchestrator orchestrator) =>
{
    logger.LogInformation($"Action trigger: {request.ActionType}");
    var result = await orchestrator.ExecuteActionAsync(request);
    return Results.Ok(result);
})
.Produces<ActionResult>()
.WithName("TriggerAction")
.WithOpenApi();

app.MapRazorPages();
app.MapControllers();

await app.RunAsync();

// Service interfaces
interface IAgentOrchestrator
{
    Task<List<ServiceDiscoveryResult>> DiscoverAgentsAsync();
    Task<Dictionary<string, object>> RequestAnalysisAsync(AnalysisRequest request);
    Task<ActionResult> ExecuteActionAsync(ActionRequest request);
}

interface IChatService
{
    Task<ChatResponse> ProcessChatAsync(ChatRequest request);
}

interface IAgentClient
{
    Task<T> GetAsync<T>(string url);
    Task<T> PostAsync<T>(string url, object payload);
}

// Service implementations
class AgentClient : IAgentClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AgentClient> _logger;

    public AgentClient(HttpClient httpClient, ILogger<AgentClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<T> GetAsync<T>(string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new Exception("Deserialization failed");
            }
            throw new Exception($"Request failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetAsync");
            throw;
        }
    }

    public async Task<T> PostAsync<T>(string url, object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(responseContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new Exception("Deserialization failed");
            }
            throw new Exception($"Request failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PostAsync");
            throw;
        }
    }
}

class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IAgentClient _agentClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentOrchestrator> _logger;

    public AgentOrchestrator(IAgentClient agentClient, IConfiguration configuration, ILogger<AgentOrchestrator> logger)
    {
        _agentClient = agentClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<ServiceDiscoveryResult>> DiscoverAgentsAsync()
    {
        var results = new List<ServiceDiscoveryResult>();
        
        // Discover NeMo Agent
        var nemoEndpoint = _configuration["NEMO_A2A_ENDPOINT"] ?? "http://127.0.0.1:8088";
        try
        {
            var cardPayload = await _agentClient.GetAsync<JsonElement>($"{nemoEndpoint}/.well-known/agent-card.json");
            var card = ParseAgentCard(cardPayload);
            results.Add(new ServiceDiscoveryResult
            {
                ServiceName = "NeMo Data Analysis Agent",
                Status = "Running",
                Endpoint = nemoEndpoint,
                AgentCard = card,
                LastChecked = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover NeMo agent");
            results.Add(new ServiceDiscoveryResult { ServiceName = "NeMo", Status = "Error", LastError = ex.Message, LastChecked = DateTime.UtcNow });
        }

        // Discover MAF Agent
        var mafEndpoint = _configuration["MAF_AGENT_ENDPOINT"] ?? "http://127.0.0.1:5055";
        try
        {
            var cardPayload = await _agentClient.GetAsync<JsonElement>($"{mafEndpoint}/.well-known/agent-card.json");
            var card = ParseAgentCard(cardPayload);
            results.Add(new ServiceDiscoveryResult
            {
                ServiceName = "MAF Action Agent",
                Status = "Running",
                Endpoint = mafEndpoint,
                AgentCard = card,
                LastChecked = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to discover MAF agent");
            results.Add(new ServiceDiscoveryResult { ServiceName = "MAF", Status = "Error", LastError = ex.Message, LastChecked = DateTime.UtcNow });
        }

        return results;
    }

    private static AgentCard ParseAgentCard(JsonElement payload)
    {
        var card = new AgentCard
        {
            Name = ReadString(payload, "name"),
            Description = ReadString(payload, "description"),
            Version = ReadString(payload, "version"),
            Endpoint = ReadString(payload, "endpoint"),
            A2AVersion = ReadString(payload, "a2a_version", "a2AVersion")
        };

        if (TryGetProperty(payload, out var capabilitiesElement, "capabilities"))
        {
            card.Capabilities = ParseCapabilities(capabilitiesElement);
        }

        return card;
    }

    private static List<string> ParseCapabilities(JsonElement capabilitiesElement)
    {
        var capabilities = new List<string>();

        switch (capabilitiesElement.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in capabilitiesElement.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        capabilities.Add(item.GetString() ?? string.Empty);
                    }
                }
                break;
            case JsonValueKind.Object:
                foreach (var property in capabilitiesElement.EnumerateObject())
                {
                    capabilities.Add(property.Name);
                }
                break;
            case JsonValueKind.String:
                capabilities.Add(capabilitiesElement.GetString() ?? string.Empty);
                break;
        }

        return capabilities.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
    }

    private static string ReadString(JsonElement payload, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (TryGetProperty(payload, out var element, key) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool TryGetProperty(JsonElement payload, out JsonElement value, params string[] keys)
    {
        foreach (var property in payload.EnumerateObject())
        {
            foreach (var key in keys)
            {
                if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    public async Task<Dictionary<string, object>> RequestAnalysisAsync(AnalysisRequest request)
    {
        var nemoEndpoint = _configuration["NEMO_A2A_ENDPOINT"] ?? "http://127.0.0.1:8088";
        // Send A2A JSON-RPC request to NeMo agent
        return await _agentClient.PostAsync<Dictionary<string, object>>($"{nemoEndpoint}/", request);
    }

    public async Task<ActionResult> ExecuteActionAsync(ActionRequest request)
    {
        var mafEndpoint = _configuration["MAF_AGENT_ENDPOINT"] ?? "http://127.0.0.1:5055";
        return await _agentClient.PostAsync<ActionResult>($"{mafEndpoint}/api/actions/execute", request);
    }
}

class ChatService : IChatService
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ILogger<ChatService> _logger;

    public ChatService(IAgentOrchestrator orchestrator, ILogger<ChatService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task<ChatResponse> ProcessChatAsync(ChatRequest request)
    {
        _logger.LogInformation($"Processing chat: {request.Message}");

        var normalized = request.Message.ToLowerInvariant();
        var response = new ChatResponse
        {
            MessageId = Guid.NewGuid().ToString(),
            Content = string.Empty,
            RespondedBy = "Web Chat Orchestrator",
            ResponseTime = DateTime.UtcNow
        };

        if (normalized.Contains("alert") || normalized.Contains("report") || normalized.Contains("action") || normalized.Contains("trigger"))
        {
            var actionRequest = new ActionRequest
            {
                ActionType = InferActionType(normalized),
                Parameters = new Dictionary<string, object>
                {
                    ["message"] = request.Message
                }
            };

            try
            {
                var actionResult = await _orchestrator.ExecuteActionAsync(actionRequest);
                response.RespondedBy = "MAF Action Agent";
                response.Content = string.IsNullOrWhiteSpace(actionResult.Details)
                    ? "Action workflow processed by MAF Action Agent."
                    : actionResult.Details;
                response.ActionsExecuted = new List<ActionResult> { actionResult };
                response.AnalysisInsights = new List<string>
                {
                    "Action workflow executed through MAF Action Agent."
                };
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MAF execution failed for chat request");
            }
        }

        response.RespondedBy = "NeMo Data Analysis Agent";
        response.Content = $"Analysis request accepted for: {request.Message}";
        response.AnalysisInsights = BuildAnalysisInsights(request.Message);
        return response;
    }

    private static string InferActionType(string normalizedMessage)
    {
        if (normalizedMessage.Contains("alert"))
        {
            return "trigger-alert";
        }

        if (normalizedMessage.Contains("report"))
        {
            return "generate-report";
        }

        return "execute-action";
    }

    private static List<string> BuildAnalysisInsights(string message)
    {
        return new List<string>
        {
            "Request routed to NeMo Data Analysis Agent.",
            $"Query context: {message}"
        };
    }
}
