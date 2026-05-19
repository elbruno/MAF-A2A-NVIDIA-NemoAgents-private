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

var builder = WebApplication.CreateBuilder(args);

// Configuration
var configuration = builder.Configuration;
configuration
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

var mafHost = configuration["MAF_HOST"] ?? "127.0.0.1";
var mafPort = configuration["MAF_PORT"] ?? "5055";
builder.WebHost.UseUrls($"http://{mafHost}:{mafPort}");

// Logging
builder.Services.AddLogging(loggingBuilder =>
{
    loggingBuilder.ClearProviders();
    loggingBuilder.AddConsole();
    loggingBuilder.AddDebug();
    var logLevel = Enum.TryParse<LogLevel>(configuration["MAF_LOG_LEVEL"] ?? "Information", out var level)
        ? level
        : LogLevel.Information;
    loggingBuilder.SetMinimumLevel(logLevel);
});

var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
logger.LogInformation("Starting MAF Action Agent...");

// OpenTelemetry Setup
var otelEnabled = bool.TryParse(configuration["ENABLE_OTEL_TRACING"] ?? "true", out var enabled) && enabled;
if (otelEnabled)
{
    logger.LogInformation("Configuring OpenTelemetry tracing...");
    
    var resource = ResourceBuilder.CreateDefault()
        .AddService(serviceName: "maf-action-agent", serviceVersion: "1.0.0");
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
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.WriteIndented = true;
    });

builder.Services.AddOpenApi();

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

// Add Health Checks
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" });

// Add Application Services
builder.Services.AddSingleton<IActionExecutor, ActionExecutor>();
builder.Services.AddSingleton<IA2ABridge, A2ABridge>();

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");
app.UseAuthorization();

// Health check endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString() })
        };
        await context.Response.WriteAsJsonAsync(response);
    }
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
});

// A2A Agent Card endpoint
app.MapGet("/.well-known/agent-card.json", GetAgentCard)
    .Produces<Dictionary<string, object>>()
    .WithName("GetAgentCard")
    .WithOpenApi();

// A2A JSON-RPC endpoint
app.MapPost("/a2a/maf-action-agent", HandleA2ARequest)
    .Produces<Dictionary<string, object>>()
    .WithName("HandleA2ARequest")
    .WithOpenApi();

// Action execution endpoints
app.MapControllers();

app.MapPost("/api/actions/execute", async (ActionRequest request, IActionExecutor executor) =>
{
    logger.LogInformation($"Executing action: {request.ActionType}");
    var result = await executor.ExecuteActionAsync(request);
    return Results.Ok(result);
})
.Produces<ActionResult>()
.WithName("ExecuteAction")
.WithOpenApi();

app.MapPost("/api/actions/trigger-alert", async (AlertRequest request, IActionExecutor executor) =>
{
    logger.LogInformation($"Triggering alert: {request.AlertLevel} - {request.Message}");
    var result = await executor.TriggerAlertAsync(request);
    return Results.Ok(result);
})
.Produces<ActionResult>()
.WithName("TriggerAlert")
.WithOpenApi();

app.MapPost("/api/actions/generate-report", async (ReportRequest request, IActionExecutor executor) =>
{
    logger.LogInformation($"Generating report: {request.ReportType}");
    var result = await executor.GenerateReportAsync(request);
    return Results.Ok(result);
})
.Produces<ActionResult>()
.WithName("GenerateReport")
.WithOpenApi();

await app.RunAsync();

// Endpoint handlers
async Task<IResult> GetAgentCard()
{
    var card = new Dictionary<string, object>
    {
        { "name", "MAF Action Agent" },
        { "description", "Executes actions based on data analysis from the NeMo agent" },
        { "version", "1.0.0" },
        { "capabilities", new[] { "execute-actions", "trigger-alerts", "generate-reports" } },
        { "endpoint", "/a2a/maf-action-agent" },
        { "a2a_version", "1.0" }
    };
    return Results.Ok(card);
}

async Task<IResult> HandleA2ARequest(HttpContext context, IA2ABridge bridge)
{
    using var reader = new System.IO.StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    var response = await bridge.ProcessA2ARequestAsync(body);
    return Results.Ok(System.Text.Json.JsonDocument.Parse(response).RootElement);
}

// Service interfaces
interface IActionExecutor
{
    Task<ActionResult> ExecuteActionAsync(ActionRequest request);
    Task<ActionResult> TriggerAlertAsync(AlertRequest request);
    Task<ActionResult> GenerateReportAsync(ReportRequest request);
}

interface IA2ABridge
{
    Task<string> ProcessA2ARequestAsync(string jsonRpcRequest);
}

// Service implementations
class ActionExecutor : IActionExecutor
{
    private readonly ILogger<ActionExecutor> _logger;

    public ActionExecutor(ILogger<ActionExecutor> logger)
    {
        _logger = logger;
    }

    public async Task<ActionResult> ExecuteActionAsync(ActionRequest request)
    {
        _logger.LogInformation($"Executing action: {request.ActionType} with params: {request.Parameters}");
        
        var result = new ActionResult
        {
            Success = true,
            ActionType = request.ActionType,
            ExecutedAt = DateTime.UtcNow,
            Details = $"Action {request.ActionType} executed successfully"
        };

        await Task.CompletedTask;
        return result;
    }

    public async Task<ActionResult> TriggerAlertAsync(AlertRequest request)
    {
        _logger.LogWarning($"Alert triggered: [{request.AlertLevel}] {request.Message}");
        
        var result = new ActionResult
        {
            Success = true,
            ActionType = "trigger-alert",
            ExecutedAt = DateTime.UtcNow,
            Details = $"Alert sent at severity: {request.AlertLevel}"
        };

        await Task.CompletedTask;
        return result;
    }

    public async Task<ActionResult> GenerateReportAsync(ReportRequest request)
    {
        _logger.LogInformation($"Generating report: {request.ReportType}");
        
        var result = new ActionResult
        {
            Success = true,
            ActionType = "generate-report",
            ExecutedAt = DateTime.UtcNow,
            Details = $"Report {request.ReportType} generated and queued for delivery"
        };

        await Task.CompletedTask;
        return result;
    }
}

class A2ABridge : IA2ABridge
{
    private readonly ILogger<A2ABridge> _logger;

    public A2ABridge(ILogger<A2ABridge> logger)
    {
        _logger = logger;
    }

    public async Task<string> ProcessA2ARequestAsync(string jsonRpcRequest)
    {
        _logger.LogDebug($"Processing A2A request: {jsonRpcRequest}");
        
        // Parse JSON-RPC request and route to appropriate action
        var response = new Dictionary<string, object>
        {
            { "jsonrpc", "2.0" },
            { "result", "processed" },
            { "timestamp", DateTime.UtcNow.ToString("O") }
        };

        await Task.CompletedTask;
        return System.Text.Json.JsonSerializer.Serialize(response);
    }
}

// Request/Response DTOs
public class ActionRequest
{
    public string ActionType { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
}

public class AlertRequest
{
    public string AlertLevel { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ReportRequest
{
    public string ReportType { get; set; } = string.Empty;
    public Dictionary<string, object>? ReportData { get; set; }
}

public class ActionResult
{
    public bool Success { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
    public string Details { get; set; } = string.Empty;
}
