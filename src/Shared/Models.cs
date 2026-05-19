namespace Shared.Models;

// ============================================================================
// Analysis Models
// ============================================================================

public class DataPoint
{
    public string Timestamp { get; set; } = string.Empty;
    public double Value { get; set; }
    public string MetricName { get; set; } = string.Empty;
}

public class AnalysisRequest
{
    public string MetricName { get; set; } = string.Empty;
    public List<DataPoint> DataPoints { get; set; } = new();
    public string AnalysisType { get; set; } = "full"; // trend, anomalies, metrics, insights
}

public class AnalysisResult
{
    public string MetricName { get; set; } = string.Empty;
    public string Trend { get; set; } = string.Empty;
    public double TrendStrength { get; set; }
    public double Average { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double StdDev { get; set; }
    public string PeriodAnalyzed { get; set; } = string.Empty;
    public int DataPointsCount { get; set; }
}

public class AnomalyResult
{
    public string MetricName { get; set; } = string.Empty;
    public int AnomaliesDetected { get; set; }
    public double AnomalyPercentage { get; set; }
    public List<AnomalyPoint> Anomalies { get; set; } = new();
}

public class AnomalyPoint
{
    public string Timestamp { get; set; } = string.Empty;
    public double Value { get; set; }
    public double ZScore { get; set; }
    public double DeviationFromMean { get; set; }
}

public class InsightResult
{
    public List<string> Insights { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public string Confidence { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
}

// ============================================================================
// Action Models
// ============================================================================

public class ActionRequest
{
    public string ActionType { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
    public string? CorrelationId { get; set; }
}

public class ActionResult
{
    public bool Success { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
    public string Details { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
}

public class AlertRequest
{
    public string AlertLevel { get; set; } = string.Empty; // Critical, High, Medium, Low
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object>? AlertData { get; set; }
    public string? CorrelationId { get; set; }
}

public class ReportRequest
{
    public string ReportType { get; set; } = string.Empty;
    public Dictionary<string, object>? ReportData { get; set; }
    public List<string>? Recipients { get; set; }
    public string? CorrelationId { get; set; }
}

// ============================================================================
// Agent Discovery Models
// ============================================================================

public class AgentCard
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public List<string> Capabilities { get; set; } = new();
    public string Endpoint { get; set; } = string.Empty;
    public string A2AVersion { get; set; } = string.Empty;
}

public class ServiceDiscoveryResult
{
    public string ServiceName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Running, Stopped, Error
    public string Endpoint { get; set; } = string.Empty;
    public AgentCard? AgentCard { get; set; }
    public string? LastError { get; set; }
    public DateTime LastChecked { get; set; }
}

// ============================================================================
// Chat Models
// ============================================================================

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = string.Empty; // user, agent, system
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

public class ChatSession
{
    public string Id { get; set; } = string.Empty;
    public List<ChatMessage> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

public class ChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string? AnalysisContext { get; set; }
}

public class ChatResponse
{
    public string MessageId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ContentHtml { get; set; }
    public string RespondedBy { get; set; } = "Web Chat Orchestrator";
    public List<string> AnalysisInsights { get; set; } = new();
    public List<ActionResult>? ActionsExecuted { get; set; }
    public DateTime ResponseTime { get; set; } = DateTime.UtcNow;
}

// ============================================================================
// Orchestration Models
// ============================================================================

public class WorkflowTrigger
{
    public string TriggerType { get; set; } = string.Empty; // manual, scheduled, data-driven
    public Dictionary<string, object>? Payload { get; set; }
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
}

public class WorkflowExecution
{
    public string ExecutionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // Running, Completed, Failed
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<string> Steps { get; set; } = new();
    public Dictionary<string, object>? Results { get; set; }
}

// ============================================================================
// Health & Status Models
// ============================================================================

public class HealthStatus
{
    public string Status { get; set; } = "Healthy"; // Healthy, Degraded, Unhealthy
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, ServiceStatus> Services { get; set; } = new();
}

public class ServiceStatus
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Endpoint { get; set; }
    public string? LastError { get; set; }
}
