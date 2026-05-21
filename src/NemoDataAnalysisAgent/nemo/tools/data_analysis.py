"""
Data Analysis Tools for NeMo Agent
Provides tools for time-series analysis, anomaly detection, and insights generation
"""

import json
import os
from typing import Any, Dict, List
from datetime import datetime, timedelta
import numpy as np
import pandas as pd
from pydantic import BaseModel, Field
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor


def _configure_otel() -> None:
    otlp_endpoint = os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT")
    if not otlp_endpoint:
        return

    current_provider = trace.get_tracer_provider()
    if isinstance(current_provider, TracerProvider):
        provider = current_provider
    else:
        service_name = os.getenv("NEMO_OTEL_PROJECT", "nemo-data-analysis-agent")
        provider = TracerProvider(resource=Resource.create({"service.name": service_name}))
        trace.set_tracer_provider(provider)

    normalized_endpoint = otlp_endpoint
    insecure_transport = otlp_endpoint.startswith("http://")
    if otlp_endpoint.startswith("https://localhost"):
        normalized_endpoint = f"http://{otlp_endpoint[len('https://'):]}"
        insecure_transport = True

    exporter = OTLPSpanExporter(
        endpoint=normalized_endpoint,
        headers=os.getenv("OTEL_EXPORTER_OTLP_HEADERS"),
        insecure=insecure_transport,
    )
    provider.add_span_processor(BatchSpanProcessor(exporter))


_configure_otel()
_TRACER = trace.get_tracer("nemo-data-analysis-tools")


class DataPoint(BaseModel):
    """Represents a single data point in time-series data"""
    timestamp: str = Field(..., description="ISO format timestamp")
    value: float = Field(..., description="Numeric value")
    metric_name: str = Field(..., description="Name of the metric")


class AnalysisResult(BaseModel):
    """Results from data analysis"""
    metric_name: str
    trend: str  # "increasing", "decreasing", "stable"
    trend_strength: float  # 0.0 to 1.0
    average: float
    min: float
    max: float
    std_dev: float
    period_analyzed: str


def analyze_time_series_data(
    data_points: List[Dict[str, Any]],
    metric_name: str = "unknown_metric"
) -> Dict[str, Any]:
    """
    Analyze time-series data to detect trends and patterns.
    
    Args:
        data_points: List of dicts with 'timestamp' and 'value' keys
        metric_name: Name of the metric being analyzed
    
    Returns:
        Analysis result with trend, statistics, and insights
    """
    with _TRACER.start_as_current_span(
        "nemo.tool.analyze_time_series",
        attributes={
            "gen_ai.system": "nvidia.nemo.agent-toolkit",
            "gen_ai.operation.name": "analyze_time_series",
            "metric.name": metric_name,
            "data.points.count": len(data_points),
        },
    ):
        try:
        # Convert to DataFrame
            df = pd.DataFrame(data_points)
            df['timestamp'] = pd.to_datetime(df['timestamp'])
            df = df.sort_values('timestamp')
        
        # Calculate statistics
            values = df['value'].values
            avg = float(np.mean(values))
            min_val = float(np.min(values))
            max_val = float(np.max(values))
            std = float(np.std(values))
        
        # Detect trend using linear regression
            x = np.arange(len(values))
            slope = float(np.polyfit(x, values, 1)[0])
        
        # Determine trend direction
            if slope > std * 0.1:
                trend = "increasing"
                strength = min(abs(slope) / (std + 1e-6), 1.0)
            elif slope < -std * 0.1:
                trend = "decreasing"
                strength = min(abs(slope) / (std + 1e-6), 1.0)
            else:
                trend = "stable"
                strength = 0.0
        
            period = f"{df['timestamp'].min().date()} to {df['timestamp'].max().date()}"
        
            return {
                "metric_name": metric_name,
                "trend": trend,
                "trend_strength": float(strength),
                "average": avg,
                "min": min_val,
                "max": max_val,
                "std_dev": std,
                "period_analyzed": period,
                "data_points_count": len(df),
                "status": "success"
            }
        except Exception as e:
            return {
                "metric_name": metric_name,
                "error": str(e),
                "status": "error"
            }


def detect_data_anomalies(
    data_points: List[Dict[str, Any]],
    sensitivity: float = 2.0,
    metric_name: str = "unknown_metric"
) -> Dict[str, Any]:
    """
    Detect anomalies in time-series data using statistical methods.
    
    Args:
        data_points: List of dicts with 'timestamp' and 'value' keys
        sensitivity: Standard deviations threshold (default: 2.0)
        metric_name: Name of the metric being analyzed
    
    Returns:
        Dictionary with anomaly detection results
    """
    with _TRACER.start_as_current_span(
        "nemo.tool.detect_anomalies",
        attributes={
            "gen_ai.system": "nvidia.nemo.agent-toolkit",
            "gen_ai.operation.name": "detect_anomalies",
            "metric.name": metric_name,
            "data.points.count": len(data_points),
            "anomaly.sensitivity": sensitivity,
        },
    ):
        try:
            df = pd.DataFrame(data_points)
            df['timestamp'] = pd.to_datetime(df['timestamp'])
            df = df.sort_values('timestamp')
        
            values = df['value'].values
            mean = np.mean(values)
            std = np.std(values)
        
        # Detect anomalies
            anomalies = []
            for idx, (ts, val) in enumerate(zip(df['timestamp'], values)):
                z_score = abs((val - mean) / (std + 1e-6))
                if z_score > sensitivity:
                    anomalies.append({
                        "timestamp": ts.isoformat(),
                        "value": float(val),
                        "z_score": float(z_score),
                        "deviation_from_mean": float(val - mean)
                    })
        
            return {
                "metric_name": metric_name,
                "anomalies_detected": len(anomalies),
                "anomaly_percentage": (len(anomalies) / len(df) * 100) if len(df) > 0 else 0,
                "sensitivity_threshold": sensitivity,
                "anomalies": anomalies[:10],  # Return top 10
                "mean": float(mean),
                "std_dev": float(std),
                "status": "success"
            }
        except Exception as e:
            return {
                "metric_name": metric_name,
                "error": str(e),
                "status": "error"
            }


def calculate_metrics(
    data_points: List[Dict[str, Any]],
    metric_name: str = "unknown_metric",
    include_percentiles: bool = True
) -> Dict[str, Any]:
    """
    Calculate comprehensive statistical metrics.
    
    Args:
        data_points: List of dicts with 'timestamp' and 'value' keys
        metric_name: Name of the metric being analyzed
        include_percentiles: Whether to include percentile calculations
    
    Returns:
        Dictionary with various statistical metrics
    """
    with _TRACER.start_as_current_span(
        "nemo.tool.calculate_metrics",
        attributes={
            "gen_ai.system": "nvidia.nemo.agent-toolkit",
            "gen_ai.operation.name": "calculate_metrics",
            "metric.name": metric_name,
            "data.points.count": len(data_points),
        },
    ):
        try:
            df = pd.DataFrame(data_points)
            df['timestamp'] = pd.to_datetime(df['timestamp'])
            df = df.sort_values('timestamp')
        
            values = df['value'].values
        
            metrics = {
                "metric_name": metric_name,
                "count": len(values),
                "mean": float(np.mean(values)),
                "median": float(np.median(values)),
                "std_dev": float(np.std(values)),
                "variance": float(np.var(values)),
                "min": float(np.min(values)),
                "max": float(np.max(values)),
                "range": float(np.max(values) - np.min(values)),
            }
        
            if include_percentiles:
                metrics.update({
                    "p25": float(np.percentile(values, 25)),
                    "p50": float(np.percentile(values, 50)),
                    "p75": float(np.percentile(values, 75)),
                    "p90": float(np.percentile(values, 90)),
                    "p95": float(np.percentile(values, 95)),
                })
        
        # Calculate change metrics
            if len(values) > 1:
                pct_change = ((values[-1] - values[0]) / abs(values[0] + 1e-6)) * 100
                metrics["percent_change"] = float(pct_change)
        
            metrics["status"] = "success"
        
            return metrics
        except Exception as e:
            return {
                "metric_name": metric_name,
                "error": str(e),
                "status": "error"
            }


def generate_insights(
    analysis_results: Dict[str, Any],
    anomalies: Dict[str, Any],
    metrics: Dict[str, Any]
) -> Dict[str, Any]:
    """
    Generate business insights from analysis results.
    
    Args:
        analysis_results: Results from analyze_time_series_data
        anomalies: Results from detect_data_anomalies
        metrics: Results from calculate_metrics
    
    Returns:
        Dictionary with generated insights and recommendations
    """
    with _TRACER.start_as_current_span(
        "nemo.tool.generate_insights",
        attributes={
            "gen_ai.system": "nvidia.nemo.agent-toolkit",
            "gen_ai.operation.name": "generate_insights",
        },
    ):
        try:
            insights = []
            recommendations = []
        
        # Trend-based insights
            if analysis_results.get("status") == "success":
                trend = analysis_results.get("trend", "unknown")
                strength = analysis_results.get("trend_strength", 0)
            
                if trend == "increasing" and strength > 0.5:
                    insights.append("Strong upward trend detected - continued growth or escalation expected")
                    recommendations.append("Monitor for continued growth; ensure resources are scaled accordingly")
                elif trend == "increasing":
                    insights.append("Moderate upward trend detected")
                    recommendations.append("Track trend continuation; plan for gradual capacity increase")
                elif trend == "decreasing" and strength > 0.5:
                    insights.append("Strong downward trend detected - possible improvement or decline")
                    recommendations.append("Investigate root cause of decline to determine if positive or concerning")
                elif trend == "decreasing":
                    insights.append("Moderate downward trend detected")
                    recommendations.append("Monitor for stabilization or further decline")
                else:
                    insights.append("Data shows stable pattern - no significant trend")
                    recommendations.append("Continue routine monitoring; alert on deviations from baseline")
        
        # Anomaly-based insights
            if anomalies.get("status") == "success":
                anomaly_pct = anomalies.get("anomaly_percentage", 0)
                if anomaly_pct > 5:
                    insights.append(f"Significant anomalies detected ({anomaly_pct:.1f}% of data points)")
                    recommendations.append("Investigate anomalies; implement additional data validation or alerts")
                elif anomaly_pct > 1:
                    insights.append(f"Minor anomalies detected ({anomaly_pct:.1f}% of data points)")
                    recommendations.append("Monitor anomaly patterns; escalate if frequency increases")
        
        # Volatility insights
            if metrics.get("status") == "success":
                std_dev = metrics.get("std_dev", 0)
                mean = metrics.get("mean", 1)
                cv = (std_dev / (mean + 1e-6)) * 100  # Coefficient of variation
            
                if cv > 30:
                    insights.append(f"High volatility detected (CV: {cv:.1f}%) - data is highly variable")
                    recommendations.append("Investigate sources of variability; consider smoothing or aggregation")
                elif cv > 10:
                    insights.append(f"Moderate volatility detected (CV: {cv:.1f}%)")
                    recommendations.append("Normal variation; maintain current monitoring level")
                else:
                    insights.append(f"Low volatility detected (CV: {cv:.1f}%) - stable metric")
                    recommendations.append("Excellent stability; investigate if sudden changes occur")
        
            return {
                "insights": insights,
                "recommendations": recommendations,
                "confidence": "high" if len(insights) > 0 else "low",
                "generated_at": datetime.utcnow().isoformat(),
                "status": "success"
            }
        except Exception as e:
            return {
                "error": str(e),
                "status": "error"
            }


# Dictionary mapping tool names to functions
# Names must match workflow.yml tool definitions exactly
TOOLS = {
    "analyze_time_series": analyze_time_series_data,
    "detect_anomalies": detect_data_anomalies,
    "calculate_metrics": calculate_metrics,
    "generate_insights": generate_insights,
}
