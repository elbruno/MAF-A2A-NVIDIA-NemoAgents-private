from __future__ import annotations

from datetime import datetime
import grpc
import logging
import os
from pathlib import Path
from typing import Any

import numpy as np
import pandas as pd
from opentelemetry import trace
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.sdk.resources import Resource
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor

try:
    from opentelemetry.instrumentation.langchain import LangchainInstrumentor
except ModuleNotFoundError:
    LangchainInstrumentor = None

_LOGGER = logging.getLogger(__name__)

_OTEL_CONFIGURED = False


def _configure_otel() -> None:
    global _OTEL_CONFIGURED
    if _OTEL_CONFIGURED:
        return

    otlp_endpoint = os.getenv("OTEL_EXPORTER_OTLP_ENDPOINT")
    if not otlp_endpoint:
        return

    current_provider = trace.get_tracer_provider()
    if isinstance(current_provider, TracerProvider):
        provider = current_provider
    else:
        service_name = os.getenv("NEMO_OTEL_PROJECT") or os.getenv("OTEL_SERVICE_NAME") or "nemo-data-analysis-agent"
        provider = TracerProvider(resource=Resource.create({"service.name": service_name}))
        trace.set_tracer_provider(provider)

    certificate_file: str | None = None
    ssl_cert_dir = os.getenv("SSL_CERT_DIR")
    if ssl_cert_dir:
        cert_dir = Path(ssl_cert_dir)
        if cert_dir.exists():
            pem_files = sorted(cert_dir.glob("*.pem"))
            if pem_files:
                certificate_file = str(pem_files[0])
            else:
                hash_files = sorted(cert_dir.glob("*.0"))
                if hash_files:
                    certificate_file = str(hash_files[0])

    channel_credentials: grpc.ChannelCredentials | None = None
    if certificate_file:
        channel_credentials = grpc.ssl_channel_credentials(root_certificates=Path(certificate_file).read_bytes())

    provider.add_span_processor(
        BatchSpanProcessor(
            OTLPSpanExporter(
                endpoint=otlp_endpoint,
                headers=os.getenv("OTEL_EXPORTER_OTLP_HEADERS"),
                insecure=otlp_endpoint.startswith("http://"),
                credentials=channel_credentials,
            )
        )
    )

    if LangchainInstrumentor is not None:
        LangchainInstrumentor().instrument()
    else:
        _LOGGER.warning(
            "opentelemetry-instrumentation-langchain is not installed; LLM-level spans will not be emitted."
        )
    _OTEL_CONFIGURED = True


_configure_otel()
_TRACER = trace.get_tracer("nemo-data-analysis-agent.tools")


def _to_dataframe(data_points: list[dict[str, Any]]) -> pd.DataFrame:
    if not data_points:
        raise ValueError("At least one data point is required.")

    frame = pd.DataFrame(data_points)
    missing_columns = {"timestamp", "value"} - set(frame.columns)
    if missing_columns:
        missing = ", ".join(sorted(missing_columns))
        raise ValueError(f"Missing required columns: {missing}")

    frame["timestamp"] = pd.to_datetime(frame["timestamp"], utc=True)
    frame = frame.sort_values("timestamp")
    return frame


def analyze_time_series_data(data_points: list[dict[str, Any]], metric_name: str = "unknown_metric") -> dict[str, Any]:
    """Analyze time-series data to detect trends and summarize the observed range."""
    with _TRACER.start_as_current_span(
        "nemo.tool.analyze_time_series",
        attributes={
            "gen_ai.system": "nvidia.nat.langchain",
            "gen_ai.operation.name": "analyze_time_series",
            "metric.name": metric_name,
            "data.points.count": len(data_points),
        },
    ) as span:
        try:
            frame = _to_dataframe(data_points)
            values = frame["value"].astype(float).to_numpy()

            average = float(np.mean(values))
            min_value = float(np.min(values))
            max_value = float(np.max(values))
            std_dev = float(np.std(values))

            if len(values) > 1:
                slope = float(np.polyfit(np.arange(len(values)), values, 1)[0])
            else:
                slope = 0.0

            threshold = max(std_dev * 0.1, 0.01)
            if slope > threshold:
                trend = "increasing"
                trend_strength = min(abs(slope) / (std_dev + 1e-6), 1.0)
            elif slope < -threshold:
                trend = "decreasing"
                trend_strength = min(abs(slope) / (std_dev + 1e-6), 1.0)
            else:
                trend = "stable"
                trend_strength = 0.0

            period = f"{frame['timestamp'].min().date()} to {frame['timestamp'].max().date()}"
            span.set_attribute("gen_ai.response.status", "success")
            return {
                "metric_name": metric_name,
                "trend": trend,
                "trend_strength": float(trend_strength),
                "average": average,
                "min": min_value,
                "max": max_value,
                "std_dev": std_dev,
                "period_analyzed": period,
                "data_points_count": int(len(frame)),
                "status": "success",
            }
        except Exception as exc:
            span.set_attribute("gen_ai.response.status", "error")
            span.set_attribute("error.type", type(exc).__name__)
            return {
                "metric_name": metric_name,
                "error": str(exc),
                "status": "error",
            }


def detect_data_anomalies(
    data_points: list[dict[str, Any]],
    sensitivity: float = 2.0,
    metric_name: str = "unknown_metric",
) -> dict[str, Any]:
    """Detect anomalous data points with a z-score threshold."""
    with _TRACER.start_as_current_span(
        "nemo.tool.detect_anomalies",
        attributes={
            "gen_ai.system": "nvidia.nat.langchain",
            "gen_ai.operation.name": "detect_anomalies",
            "metric.name": metric_name,
            "data.points.count": len(data_points),
            "anomaly.sensitivity": sensitivity,
        },
    ) as span:
        try:
            frame = _to_dataframe(data_points)
            values = frame["value"].astype(float).to_numpy()
            mean = float(np.mean(values))
            std_dev = float(np.std(values))

            anomalies: list[dict[str, Any]] = []
            for timestamp, value in zip(frame["timestamp"], values, strict=False):
                z_score = abs((float(value) - mean) / (std_dev + 1e-6))
                if z_score > sensitivity:
                    anomalies.append(
                        {
                            "timestamp": timestamp.isoformat(),
                            "value": float(value),
                            "z_score": float(z_score),
                            "deviation_from_mean": float(value - mean),
                        }
                    )

            span.set_attribute("anomalies.detected.count", len(anomalies))
            span.set_attribute("gen_ai.response.status", "success")
            return {
                "metric_name": metric_name,
                "anomalies_detected": len(anomalies),
                "anomaly_percentage": (len(anomalies) / len(frame) * 100.0),
                "sensitivity_threshold": sensitivity,
                "anomalies": anomalies[:10],
                "mean": mean,
                "std_dev": std_dev,
                "status": "success",
            }
        except Exception as exc:
            span.set_attribute("gen_ai.response.status", "error")
            span.set_attribute("error.type", type(exc).__name__)
            return {
                "metric_name": metric_name,
                "error": str(exc),
                "status": "error",
            }


def calculate_metrics(
    data_points: list[dict[str, Any]],
    metric_name: str = "unknown_metric",
    include_percentiles: bool = True,
) -> dict[str, Any]:
    """Calculate descriptive statistics for a time series."""
    with _TRACER.start_as_current_span(
        "nemo.tool.calculate_metrics",
        attributes={
            "gen_ai.system": "nvidia.nat.langchain",
            "gen_ai.operation.name": "calculate_metrics",
            "metric.name": metric_name,
            "data.points.count": len(data_points),
        },
    ) as span:
        try:
            frame = _to_dataframe(data_points)
            values = frame["value"].astype(float).to_numpy()

            metrics: dict[str, Any] = {
                "metric_name": metric_name,
                "count": int(len(values)),
                "mean": float(np.mean(values)),
                "median": float(np.median(values)),
                "std_dev": float(np.std(values)),
                "variance": float(np.var(values)),
                "min": float(np.min(values)),
                "max": float(np.max(values)),
                "range": float(np.max(values) - np.min(values)),
            }

            if include_percentiles:
                metrics.update(
                    {
                        "p25": float(np.percentile(values, 25)),
                        "p50": float(np.percentile(values, 50)),
                        "p75": float(np.percentile(values, 75)),
                        "p90": float(np.percentile(values, 90)),
                        "p95": float(np.percentile(values, 95)),
                    }
                )

            if len(values) > 1:
                metrics["percent_change"] = float(((values[-1] - values[0]) / (abs(values[0]) + 1e-6)) * 100.0)

            metrics["status"] = "success"
            span.set_attribute("gen_ai.response.status", "success")
            return metrics
        except Exception as exc:
            span.set_attribute("gen_ai.response.status", "error")
            span.set_attribute("error.type", type(exc).__name__)
            return {
                "metric_name": metric_name,
                "error": str(exc),
                "status": "error",
            }


def generate_insights(
    analysis_results: dict[str, Any],
    anomalies: dict[str, Any],
    metrics: dict[str, Any],
) -> dict[str, Any]:
    """Generate business-facing insights from prior analysis outputs."""
    with _TRACER.start_as_current_span(
        "nemo.tool.generate_insights",
        attributes={
            "gen_ai.system": "nvidia.nat.langchain",
            "gen_ai.operation.name": "generate_insights",
        },
    ) as span:
        try:
            insights: list[str] = []
            recommendations: list[str] = []

            if analysis_results.get("status") == "success":
                trend = analysis_results.get("trend", "unknown")
                strength = float(analysis_results.get("trend_strength", 0.0))
                if trend == "increasing" and strength > 0.5:
                    insights.append("Strong upward trend detected.")
                    recommendations.append("Plan for sustained growth and verify capacity ahead of demand.")
                elif trend == "increasing":
                    insights.append("Moderate upward trend detected.")
                    recommendations.append("Continue monitoring for acceleration or new demand drivers.")
                elif trend == "decreasing" and strength > 0.5:
                    insights.append("Strong downward trend detected.")
                    recommendations.append("Investigate the cause of the decline and assess business impact.")
                elif trend == "decreasing":
                    insights.append("Moderate downward trend detected.")
                    recommendations.append("Watch for stabilization and identify early reversal signals.")
                else:
                    insights.append("The metric is broadly stable.")
                    recommendations.append("Maintain baseline monitoring and alert on sudden deviations.")

            if anomalies.get("status") == "success":
                anomaly_percentage = float(anomalies.get("anomaly_percentage", 0.0))
                if anomaly_percentage > 5.0:
                    insights.append(f"Significant anomalies detected ({anomaly_percentage:.1f}% of points).")
                    recommendations.append("Investigate the anomalies and validate the incoming data path.")
                elif anomaly_percentage > 1.0:
                    insights.append(f"Minor anomalies detected ({anomaly_percentage:.1f}% of points).")
                    recommendations.append("Monitor anomaly frequency for any upward trend.")

            if metrics.get("status") == "success":
                mean = float(metrics.get("mean", 0.0))
                std_dev = float(metrics.get("std_dev", 0.0))
                coefficient_of_variation = (std_dev / (abs(mean) + 1e-6)) * 100.0
                if coefficient_of_variation > 30.0:
                    insights.append(f"High volatility detected (CV: {coefficient_of_variation:.1f}%).")
                    recommendations.append("Review variability drivers and consider smoothing or aggregation.")
                elif coefficient_of_variation > 10.0:
                    insights.append(f"Moderate volatility detected (CV: {coefficient_of_variation:.1f}%).")
                    recommendations.append("Current variability is manageable but should stay under watch.")
                else:
                    insights.append(f"Low volatility detected (CV: {coefficient_of_variation:.1f}%).")
                    recommendations.append("The metric is stable; treat abrupt changes as high-signal events.")

            span.set_attribute("gen_ai.response.status", "success")
            span.set_attribute("insights.count", len(insights))
            return {
                "insights": insights,
                "recommendations": recommendations,
                "confidence": "high" if insights else "low",
                "generated_at": datetime.utcnow().isoformat(),
                "status": "success",
            }
        except Exception as exc:
            span.set_attribute("gen_ai.response.status", "error")
            span.set_attribute("error.type", type(exc).__name__)
            return {
                "error": str(exc),
                "status": "error",
            }
