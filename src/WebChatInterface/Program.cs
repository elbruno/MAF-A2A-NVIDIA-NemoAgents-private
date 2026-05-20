using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Markdig;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
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
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Add Health Checks
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" });

// Add Application Services
builder.Services.AddSingleton<AgentWarmupState>();
builder.Services.AddSingleton<IConversationContextStore, ConversationContextStore>();
builder.Services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddHostedService<AgentWarmupService>();

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
    Task<string> SendNemoMessageAsync(string message, string? sessionId);
}

interface IChatService
{
    Task<ChatResponse> ProcessChatAsync(ChatRequest request);
}

interface IConversationContextStore
{
    AnalysisContextEntry? GetLatestAnalysis(string sessionId);
    void SaveAnalysis(string sessionId, string sourcePrompt, string analysisSummary);
}

interface IAgentClient
{
    Task<T> GetAsync<T>(string url);
    Task<T> PostAsync<T>(string url, object payload);
}

record AnalysisContextEntry(string SourcePrompt, string Summary, DateTime CapturedAtUtc);

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
        var nemoEndpoint = ResolveServiceEndpoint(_configuration["NEMO_A2A_ENDPOINT"], "http://127.0.0.1:8088");
        var nemoDiscoveryEndpoint = NormalizeServiceBaseEndpoint(nemoEndpoint);
        var nemoCardUrl = BuildCardUrl(nemoDiscoveryEndpoint);
        try
        {
            var cardPayload = await _agentClient.GetAsync<JsonElement>(nemoCardUrl);
            var card = ParseAgentCard(cardPayload, nemoDiscoveryEndpoint);
            results.Add(new ServiceDiscoveryResult
            {
                ServiceName = "NeMo Data Analysis Agent",
                Status = "Running",
                Endpoint = string.IsNullOrWhiteSpace(card.Endpoint) ? nemoDiscoveryEndpoint : card.Endpoint,
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
        var mafEndpoint = ResolveServiceEndpoint(_configuration["MAF_AGENT_ENDPOINT"], "http://127.0.0.1:5055");
        var mafDiscoveryEndpoint = NormalizeServiceBaseEndpoint(mafEndpoint);
        var mafCardUrl = BuildCardUrl(mafDiscoveryEndpoint);
        try
        {
            var cardPayload = await _agentClient.GetAsync<JsonElement>(mafCardUrl);
            var card = ParseAgentCard(cardPayload, mafDiscoveryEndpoint);
            results.Add(new ServiceDiscoveryResult
            {
                ServiceName = "MAF Action Agent",
                Status = "Running",
                Endpoint = string.IsNullOrWhiteSpace(card.Endpoint) ? mafDiscoveryEndpoint : card.Endpoint,
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

    private static AgentCard ParseAgentCard(JsonElement payload, string serviceBaseEndpoint)
    {
        var card = new AgentCard
        {
            Name = ReadString(payload, "name"),
            Description = ReadString(payload, "description"),
            Version = ReadString(payload, "version"),
            Endpoint = ResolveCardEndpoint(serviceBaseEndpoint, ReadString(payload, "endpoint", "url")),
            A2AVersion = ReadString(payload, "a2a_version", "a2AVersion", "protocolVersion")
        };

        if (TryGetProperty(payload, out var capabilitiesElement, "capabilities"))
        {
            card.Capabilities = ParseCapabilities(capabilitiesElement);
        }

        return card;
    }

    private static string BuildCardUrl(string serviceBaseEndpoint)
    {
        return $"{NormalizeServiceBaseEndpoint(serviceBaseEndpoint).TrimEnd('/')}/.well-known/agent-card.json";
    }

    private static string ResolveServiceEndpoint(string? configuredEndpoint, string fallbackEndpoint)
    {
        var candidate = string.IsNullOrWhiteSpace(configuredEndpoint)
            ? fallbackEndpoint
            : configuredEndpoint.Trim();

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return fallbackEndpoint;
        }

        if (uri.AbsolutePath.Equals("/.well-known/agent-card.json", StringComparison.OrdinalIgnoreCase))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        return candidate.TrimEnd('/');
    }

    private static string NormalizeServiceBaseEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return endpoint.TrimEnd('/');
        }

        return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    private static string ResolveCardEndpoint(string serviceBaseEndpoint, string rawEndpoint)
    {
        if (string.IsNullOrWhiteSpace(rawEndpoint))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(rawEndpoint, UriKind.Absolute, out var absoluteUri))
        {
            if (!Uri.TryCreate(NormalizeServiceBaseEndpoint(serviceBaseEndpoint), UriKind.Absolute, out var serviceBaseUri))
            {
                return absoluteUri.ToString();
            }

            var serviceAuthority = serviceBaseUri.GetLeftPart(UriPartial.Authority);
            var rawAuthority = absoluteUri.GetLeftPart(UriPartial.Authority);

            if (string.Equals(serviceAuthority, rawAuthority, StringComparison.OrdinalIgnoreCase))
            {
                return absoluteUri.ToString();
            }

            var canonicalBaseUri = new Uri($"{serviceAuthority.TrimEnd('/')}/");
            var relativePath = absoluteUri.PathAndQuery.TrimStart('/');
            var canonicalUri = string.IsNullOrWhiteSpace(relativePath)
                ? canonicalBaseUri
                : new Uri(canonicalBaseUri, relativePath);

            return absoluteUri.Fragment.Length > 0
                ? $"{canonicalUri}{absoluteUri.Fragment}"
                : canonicalUri.ToString();
        }

        var baseWithSlash = $"{serviceBaseEndpoint.TrimEnd('/')}/";
        return new Uri(new Uri(baseWithSlash), rawEndpoint.TrimStart('/')).ToString();
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
        var nemoEndpoint = ResolveServiceEndpoint(_configuration["NEMO_A2A_ENDPOINT"], "http://127.0.0.1:8088");
        // Send A2A JSON-RPC request to NeMo agent
        return await _agentClient.PostAsync<Dictionary<string, object>>(nemoEndpoint, request);
    }

    public async Task<ActionResult> ExecuteActionAsync(ActionRequest request)
    {
        var mafEndpoint = NormalizeServiceBaseEndpoint(ResolveServiceEndpoint(_configuration["MAF_AGENT_ENDPOINT"], "http://127.0.0.1:5055"));
        return await _agentClient.PostAsync<ActionResult>($"{mafEndpoint}/api/actions/execute", request);
    }

    public async Task<string> SendNemoMessageAsync(string message, string? sessionId)
    {
        var nemoEndpoint = ResolveServiceEndpoint(_configuration["NEMO_A2A_ENDPOINT"], "http://127.0.0.1:8088");
        var resolvedSessionId = string.IsNullOrWhiteSpace(sessionId)
            ? $"web-chat-{Guid.NewGuid():N}"
            : sessionId;

        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString("D"),
            method = "message/send",
            @params = new
            {
                message = new
                {
                    kind = "message",
                    messageId = Guid.NewGuid().ToString("D"),
                    contextId = resolvedSessionId,
                    role = "user",
                    parts = new[]
                    {
                        new
                        {
                            kind = "text",
                            text = message
                        }
                    }
                }
            }
        };

        var responsePayload = await _agentClient.PostAsync<JsonElement>(nemoEndpoint, payload);
        return ExtractNemoTextResponse(responsePayload);
    }

    private static string ExtractNemoTextResponse(JsonElement payload)
    {
        if (TryGetProperty(payload, out var errorNode, "error"))
        {
            var errorMessage = ReadString(errorNode, "message");
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                throw new InvalidOperationException($"NeMo returned an error: {errorMessage}");
            }

            throw new InvalidOperationException("NeMo returned an error payload.");
        }

        if (!TryGetProperty(payload, out var resultNode, "result"))
        {
            throw new InvalidOperationException("NeMo response did not contain a result payload.");
        }

        if (TryGetProperty(resultNode, out var partsNode, "parts") && partsNode.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in partsNode.EnumerateArray())
            {
                if (TryGetProperty(part, out var textNode, "text") && textNode.ValueKind == JsonValueKind.String)
                {
                    var text = textNode.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }

        throw new InvalidOperationException("NeMo response did not include textual content.");
    }
}

class ConversationContextStore : IConversationContextStore
{
    private readonly ConcurrentDictionary<string, AnalysisContextEntry> _analysisBySession = new(StringComparer.Ordinal);
    private readonly TimeSpan _ttl;
    private readonly int _maxSummaryLength;
    private readonly ILogger<ConversationContextStore> _logger;

    public ConversationContextStore(IConfiguration configuration, ILogger<ConversationContextStore> logger)
    {
        _logger = logger;
        var ttlMinutes = int.TryParse(configuration["CHAT_ANALYSIS_CONTEXT_TTL_MINUTES"], out var parsedTtlMinutes)
            ? parsedTtlMinutes
            : 30;
        _ttl = TimeSpan.FromMinutes(Math.Clamp(ttlMinutes, 1, 240));
        var maxSummaryLength = int.TryParse(configuration["CHAT_ANALYSIS_CONTEXT_MAX_LENGTH"], out var parsedMaxSummaryLength)
            ? parsedMaxSummaryLength
            : 1600;
        _maxSummaryLength = Math.Clamp(maxSummaryLength, 200, 8000);
    }

    public AnalysisContextEntry? GetLatestAnalysis(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        if (!_analysisBySession.TryGetValue(sessionId, out var entry))
        {
            return null;
        }

        if (DateTime.UtcNow - entry.CapturedAtUtc <= _ttl)
        {
            return entry;
        }

        _analysisBySession.TryRemove(sessionId, out _);
        return null;
    }

    public void SaveAnalysis(string sessionId, string sourcePrompt, string analysisSummary)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(analysisSummary))
        {
            return;
        }

        var normalizedSummary = analysisSummary.Trim();
        if (normalizedSummary.Length > _maxSummaryLength)
        {
            normalizedSummary = normalizedSummary[.._maxSummaryLength];
        }

        var normalizedPrompt = string.IsNullOrWhiteSpace(sourcePrompt)
            ? string.Empty
            : sourcePrompt.Trim();
        if (normalizedPrompt.Length > 300)
        {
            normalizedPrompt = normalizedPrompt[..300];
        }

        _analysisBySession[sessionId] = new AnalysisContextEntry(
            SourcePrompt: normalizedPrompt,
            Summary: normalizedSummary,
            CapturedAtUtc: DateTime.UtcNow);
        _logger.LogDebug("Stored NeMo analysis context for session {SessionId}", sessionId);

        CleanupExpiredEntries();
    }

    private void CleanupExpiredEntries()
    {
        var expirationThreshold = DateTime.UtcNow - _ttl;
        foreach (var kvp in _analysisBySession)
        {
            if (kvp.Value.CapturedAtUtc < expirationThreshold)
            {
                _analysisBySession.TryRemove(kvp.Key, out _);
            }
        }
    }
}

class ChatService : IChatService
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private readonly IAgentOrchestrator _orchestrator;
    private readonly IConversationContextStore _contextStore;
    private readonly AgentWarmupState _warmupState;
    private readonly ILogger<ChatService> _logger;
    private readonly TimeSpan _nemoTimeout;
    private readonly TimeSpan _warmupRequestMaxWait;

    public ChatService(
        IAgentOrchestrator orchestrator,
        IConversationContextStore contextStore,
        AgentWarmupState warmupState,
        ILogger<ChatService> logger,
        IConfiguration configuration)
    {
        _orchestrator = orchestrator;
        _contextStore = contextStore;
        _warmupState = warmupState;
        _logger = logger;
        var timeoutSeconds = int.TryParse(configuration["NEMO_CHAT_TIMEOUT_SECONDS"], out var parsedSeconds)
            ? parsedSeconds
            : 300;
        _nemoTimeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 10, 300));
        var warmupWaitSeconds = int.TryParse(configuration["NEMO_WARMUP_REQUEST_MAX_WAIT_SECONDS"], out var parsedWarmupWaitSeconds)
            ? parsedWarmupWaitSeconds
            : 0;
        _warmupRequestMaxWait = TimeSpan.FromSeconds(Math.Clamp(warmupWaitSeconds, 0, 30));
    }

    public async Task<ChatResponse> ProcessChatAsync(ChatRequest request)
    {
        _logger.LogInformation($"Processing chat: {request.Message}");
        var sessionId = ResolveSessionId(request.SessionId);

        var normalized = request.Message.ToLowerInvariant();
        var response = new ChatResponse
        {
            MessageId = Guid.NewGuid().ToString(),
            Content = string.Empty,
            RespondedBy = "Web Chat Orchestrator",
            ResponseTime = DateTime.UtcNow
        };

        var shouldTriggerAction = ShouldTriggerAction(normalized);

        if (shouldTriggerAction)
        {
            var actionRequest = new ActionRequest
            {
                ActionType = InferActionType(normalized),
                Parameters = new Dictionary<string, object>
                {
                    ["message"] = request.Message
                }
            };

            var analysisContext = _contextStore.GetLatestAnalysis(sessionId);
            if (analysisContext is not null)
            {
                actionRequest.Parameters["analysisSummary"] = analysisContext.Summary;
                actionRequest.Parameters["analysisSourcePrompt"] = analysisContext.SourcePrompt;
                actionRequest.Parameters["analysisCapturedAtUtc"] = analysisContext.CapturedAtUtc.ToString("O");
            }

            if (!string.IsNullOrWhiteSpace(request.AnalysisContext))
            {
                actionRequest.Parameters["explicitAnalysisContext"] = request.AnalysisContext.Trim();
            }

            try
            {
                var actionResult = await _orchestrator.ExecuteActionAsync(actionRequest);
                var actionDetails = string.IsNullOrWhiteSpace(actionResult.Details)
                    ? "Action workflow processed by MAF Action Agent."
                    : actionResult.Details;
                if (analysisContext is not null)
                {
                    actionDetails = $"{actionDetails}{Environment.NewLine}{Environment.NewLine}Used prior NeMo analysis context from this chat session.";
                }

                response.RespondedBy = "MAF Action Agent";
                response.Content = actionDetails;
                response.ContentHtml = RenderMarkdown(actionDetails);
                response.ActionsExecuted = new List<ActionResult> { actionResult };
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MAF execution failed for chat request");
            }
        }

        try
        {
            var warmupStatus = await _warmupState.WaitForAvailabilityAsync(_warmupRequestMaxWait, CancellationToken.None);
            if (warmupStatus == WarmupAvailability.WarmingUp)
            {
                response.RespondedBy = "System";
                response.Content = "NeMo Data Analysis Agent is still warming up. Please try again in a few seconds.";
                return response;
            }

            var nemoReply = await _orchestrator.SendNemoMessageAsync(request.Message, sessionId).WaitAsync(_nemoTimeout);
            var cleanedNemoReply = CleanNemoReply(nemoReply);
            _contextStore.SaveAnalysis(sessionId, request.Message, cleanedNemoReply);
            response.RespondedBy = "NeMo Data Analysis Agent";
            response.Content = cleanedNemoReply;
            response.ContentHtml = RenderMarkdown(cleanedNemoReply);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NeMo analysis failed for chat request");
        }

        response.RespondedBy = "System";
        response.Content = $"NeMo Data Analysis Agent did not return a response within {_nemoTimeout.TotalSeconds:0} seconds.";
        response.AnalysisInsights = new List<string>
        {
            "NeMo analysis timed out or returned an invalid response."
        };
        return response;
    }

    private static string ResolveSessionId(string? incomingSessionId)
    {
        if (!string.IsNullOrWhiteSpace(incomingSessionId))
        {
            return incomingSessionId.Trim();
        }

        return $"web-chat-{Guid.NewGuid():N}";
    }

    private static string RenderMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        return Markdown.ToHtml(content, MarkdownPipeline);
    }

    private static string CleanNemoReply(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var insightsMatch = Regex.Match(
            content,
            @"(?is)(?:\*\*)?Insights and Recommendations(?:\*\*)?\s*(?<body>.+)$");
        if (insightsMatch.Success)
        {
            var extracted = $"## Insights and Recommendations{Environment.NewLine}{Environment.NewLine}{insightsMatch.Groups["body"].Value.Trim()}";
            if (extracted.Length >= 40)
            {
                return extracted;
            }
        }

        var normalizedContent = content.Replace("\r\n", "\n");
        var summarySections = new List<string>();
        var recommendationLines = new List<string>();
        var recommendationHeadingAdded = false;
        var capturingRecommendations = false;

        foreach (var rawLine in normalizedContent.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (capturingRecommendations && recommendationLines.Count > 0)
                {
                    capturingRecommendations = false;
                }

                continue;
            }

            if (Regex.IsMatch(line, @"^Recommendations:?\s*$", RegexOptions.IgnoreCase))
            {
                capturingRecommendations = true;
                if (!recommendationHeadingAdded)
                {
                    recommendationLines.Add("## Recommendations");
                    recommendationHeadingAdded = true;
                }

                continue;
            }

            if (capturingRecommendations)
            {
                if (line.StartsWith("*", StringComparison.Ordinal) ||
                    line.StartsWith("-", StringComparison.Ordinal) ||
                    Regex.IsMatch(line, @"^\d+\.\s+"))
                {
                    recommendationLines.Add(line);
                    continue;
                }

                capturingRecommendations = false;
            }

            if (Regex.IsMatch(line, @"^(Based on (these results|the analysis|this analysis)|In summary|Overall|The .*trend|There (are|were) no anomalies|The mean .*|The median .*|The average .*|Average .*|Median .*|Revenue .*|This indicates)", RegexOptions.IgnoreCase))
            {
                summarySections.Add(line);
            }
        }

        if (summarySections.Count > 0 || recommendationLines.Count > 0)
        {
            var sections = new List<string>();
            if (summarySections.Count > 0)
            {
                sections.Add(string.Join($"{Environment.NewLine}{Environment.NewLine}", summarySections.Distinct(StringComparer.OrdinalIgnoreCase)));
            }

            if (recommendationLines.Count > 0)
            {
                sections.Add(string.Join(Environment.NewLine, recommendationLines));
            }

            var extractedSummary = string.Join($"{Environment.NewLine}{Environment.NewLine}", sections).Trim();
            if (extractedSummary.Length >= 40)
            {
                return extractedSummary;
            }
        }

        var cleaned = content;
        cleaned = Regex.Replace(cleaned, "```json\\s*[\\s\\S]*?```", string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*Based on the previous conversation.*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*Using .*tool.*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*\d+\.\s+\*\*.*\*\*:\s*.*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*Running the .*tool.*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*To analyze .*Here's the input:.*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*\*\*Tool:.*\*\*\s*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*\*\*Tool\s+\d+:.*\*\*\s*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*\*\*(Input|Output):\*\*\s*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*(First|Next|Then|Finally), I will.*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*I('|’)ll .*?(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*Let me know if you'd like to explore further.*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*To analyze .*available tools.*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*Here('|’)s the analysis:\s*(?:\r?\n)?", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(?im)^\s*\*\*Analysis Results:\*\*\s*$", string.Empty);
        cleaned = Regex.Replace(cleaned, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine);
        cleaned = cleaned.Trim();

        return cleaned.Length >= 40 ? cleaned : content.Trim();
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

    private static bool ShouldTriggerAction(string normalizedMessage)
    {
        var hasActionKeyword =
            normalizedMessage.Contains("alert") ||
            normalizedMessage.Contains("report") ||
            normalizedMessage.Contains("action") ||
            normalizedMessage.Contains("trigger");

        if (!hasActionKeyword)
        {
            return false;
        }

        return normalizedMessage.StartsWith("trigger", StringComparison.Ordinal) ||
               normalizedMessage.StartsWith("generate", StringComparison.Ordinal) ||
               normalizedMessage.StartsWith("send", StringComparison.Ordinal) ||
               normalizedMessage.StartsWith("create", StringComparison.Ordinal) ||
               normalizedMessage.StartsWith("execute", StringComparison.Ordinal) ||
               normalizedMessage.StartsWith("run", StringComparison.Ordinal);
    }
}

enum WarmupAvailability
{
    WarmingUp,
    Ready,
    Failed
}

class AgentWarmupState
{
    private readonly TaskCompletionSource _availability = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _sync = new();
    private WarmupAvailability _status = WarmupAvailability.WarmingUp;

    public WarmupAvailability Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public void MarkStarted()
    {
        lock (_sync)
        {
            _status = WarmupAvailability.WarmingUp;
        }
    }

    public void MarkCompleted()
    {
        lock (_sync)
        {
            _status = WarmupAvailability.Ready;
        }

        _availability.TrySetResult();
    }

    public void MarkFailed()
    {
        lock (_sync)
        {
            _status = WarmupAvailability.Failed;
        }

        _availability.TrySetResult();
    }

    public void MarkDisabled()
    {
        lock (_sync)
        {
            _status = WarmupAvailability.Ready;
        }

        _availability.TrySetResult();
    }

    public async Task<WarmupAvailability> WaitForAvailabilityAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (Status != WarmupAvailability.WarmingUp)
        {
            return Status;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return WarmupAvailability.WarmingUp;
        }

        var delayTask = Task.Delay(timeout, cancellationToken);
        await Task.WhenAny(_availability.Task, delayTask);
        return Status;
    }
}

class AgentWarmupService : BackgroundService
{
    private readonly IAgentOrchestrator _orchestrator;
    private readonly AgentWarmupState _state;
    private readonly ILogger<AgentWarmupService> _logger;
    private readonly bool _enabled;
    private readonly TimeSpan _startupDelay;
    private readonly TimeSpan _warmupTimeout;
    private readonly TimeSpan _retryDelay;
    private readonly int _maxAttempts;
    private readonly string _warmupMessage;

    public AgentWarmupService(
        IAgentOrchestrator orchestrator,
        AgentWarmupState state,
        IConfiguration configuration,
        ILogger<AgentWarmupService> logger)
    {
        _orchestrator = orchestrator;
        _state = state;
        _logger = logger;

        _enabled = !bool.TryParse(configuration["NEMO_WARMUP_ENABLED"], out var enabled) || enabled;
        _startupDelay = TimeSpan.FromSeconds(ClampSeconds(configuration["NEMO_WARMUP_DELAY_SECONDS"], 0, 0, 60));
        _warmupTimeout = TimeSpan.FromSeconds(ClampSeconds(configuration["NEMO_WARMUP_TIMEOUT_SECONDS"], 120, 10, 300));
        _retryDelay = TimeSpan.FromSeconds(ClampSeconds(configuration["NEMO_WARMUP_RETRY_DELAY_SECONDS"], 5, 1, 60));
        _maxAttempts = ClampSeconds(configuration["NEMO_WARMUP_MAX_ATTEMPTS"], 3, 1, 10);
        _warmupMessage = configuration["NEMO_WARMUP_MESSAGE"]?.Trim()
            ?? "Analyze quarterly revenue values [100, 110, 120, 130] for Q1-Q4 2024 and reply with READY only.";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _state.MarkDisabled();
            _logger.LogInformation("NeMo warm-up is disabled.");
            return;
        }

        _state.MarkStarted();

        if (_startupDelay > TimeSpan.Zero)
        {
            await Task.Delay(_startupDelay, stoppingToken);
        }

        for (var attempt = 1; attempt <= _maxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                _logger.LogInformation("Starting NeMo warm-up attempt {Attempt} of {MaxAttempts}.", attempt, _maxAttempts);

                var warmupReply = await _orchestrator
                    .SendNemoMessageAsync(_warmupMessage, $"warmup-{Guid.NewGuid():N}")
                    .WaitAsync(_warmupTimeout, stoppingToken);

                _logger.LogInformation(
                    "NeMo warm-up completed on attempt {Attempt}. Reply: {WarmupReply}",
                    attempt,
                    warmupReply);
                _state.MarkCompleted();
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("NeMo warm-up canceled during shutdown.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NeMo warm-up attempt {Attempt} failed.", attempt);
            }

            if (attempt < _maxAttempts)
            {
                await Task.Delay(_retryDelay, stoppingToken);
            }
        }

        _state.MarkFailed();
        _logger.LogWarning("NeMo warm-up did not complete after {MaxAttempts} attempts.", _maxAttempts);
    }

    private static int ClampSeconds(string? rawValue, int fallback, int min, int max)
    {
        if (!int.TryParse(rawValue, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, min, max);
    }
}
