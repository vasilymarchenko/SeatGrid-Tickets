# Phase 2 Implementation Plan: Observability & The "Crash"

This document outlines the step-by-step plan to instrument the SeatGrid system with OpenTelemetry and stress-test it to failure. The goal is to visualize the internal state of the application and observe how it behaves under extreme load (The "Thundering Herd").

## 1. Infrastructure - Observability Stack
**Goal**: Spin up the necessary containers to collect and visualize telemetry data.

- [ ] **Configuration Files**:
    - Create `deploy/observability/otel-collector-config.yaml`: Configure receivers (OTLP) and exporters (Prometheus, Tempo, Loki).
    - Create `deploy/observability/prometheus.yaml`: Configure scraping from the OTEL collector.
- [ ] **Update `docker-compose.yml`**:
    - Add **Tempo** (Tracing).
    - Add **Prometheus** (Metrics).
    - Add **Loki** (Logs).
    - Add **Grafana** (Visualization).
    - Add **OpenTelemetry Collector** (Central telemetry hub).
    - Ensure all services are on the same network.

## 2. Application Instrumentation (.NET)
**Goal**: Emit Traces, Metrics, and Logs from the `SeatGrid.API`.

- [ ] **Install NuGet Packages**:
    - `OpenTelemetry.Extensions.Hosting`
    - `OpenTelemetry.Instrumentation.AspNetCore`
    - `OpenTelemetry.Instrumentation.EntityFrameworkCore`
    - `OpenTelemetry.Exporter.OpenTelemetryProtocol`
- [ ] **Configure OpenTelemetry in `Program.cs`**:
    - **Tracing**: Capture HTTP requests and EF Core commands.
    - **Metrics**: Capture Runtime metrics (CPU, RAM) and ASP.NET Core metrics (Requests/sec).
    - **Logging**: Route ILogger logs to OpenTelemetry.
    - **Exporter**: Configure OTLP Exporter to send data to the OTEL Collector.

## 3. Visualization & Dashboards
**Goal**: Verify that data is flowing and create a view to monitor the crash.

- [ ] **Configure Grafana Datasources**:
    - Add `deploy/observability/grafana/datasources.yaml` to provision Prometheus, Tempo, and Loki automatically.
- [ ] **Create "System Health" Dashboard**:
    - **Metrics to track**:
        - HTTP Request Rate (RPS).
        - HTTP Response Time (P95, P99).
        - Error Rate (HTTP 5xx).
        - Active DB Connections (or Container CPU usage).

## 4. The "Crash" Load Test
**Goal**: Simulate the "Thundering Herd" to break the system.

- [ ] **Create Script**: `tests/k6/crash_test.js`.
    - **Scenario**: 10,000 users (VUs) attempting to buy 100 seats simultaneously.
    - **Stages**:
        1.  **Warm-up**: 100 users for 30s.
        2.  **Spike**: Ramp up to 10,000 users in 1 minute.
        3.  **Sustain**: Hold for 2 minutes.
- [ ] **Define Failure Thresholds**:
    - Expect high failure rate (this is a success for this phase).
    - Log specific errors (Timeouts vs 503s vs DB Locking errors).

## Execution Order
1.  **Infrastructure**: Get the containers running first.
2.  **App Instrumentation**: Connect the app to the infrastructure.
3.  **Dashboards**: Verify visibility.
4.  **Crash Test**: Run the test and watch the dashboards light up red.
