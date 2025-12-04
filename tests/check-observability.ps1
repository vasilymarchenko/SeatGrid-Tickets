#!/usr/bin/env pwsh
# Observability Pipeline Health Check
# Run this script to verify your telemetry is flowing correctly

Write-Host "🔍 SeatGrid Observability Pipeline Health Check" -ForegroundColor Cyan
Write-Host "=" * 60

# 1. Check OTEL Collector
Write-Host "`n📦 Checking OTEL Collector..." -ForegroundColor Yellow
try {
    $otelHealth = Invoke-RestMethod -Uri "http://localhost:4318/v1/metrics" -Method Get -TimeoutSec 3 -ErrorAction Stop
    Write-Host "✅ OTEL Collector is reachable" -ForegroundColor Green
} catch {
    Write-Host "❌ OTEL Collector NOT reachable: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "   Fix: docker-compose -f docker-compose.infra.yml up -d" -ForegroundColor Yellow
}

# 2. Check Prometheus
Write-Host "`n📊 Checking Prometheus..." -ForegroundColor Yellow
try {
    $promHealth = Invoke-RestMethod -Uri "http://localhost:9090/-/healthy" -Method Get -TimeoutSec 3 -ErrorAction Stop
    Write-Host "✅ Prometheus is healthy" -ForegroundColor Green
    
    # Check if Prometheus can scrape OTEL Collector
    $targets = Invoke-RestMethod -Uri "http://localhost:9090/api/v1/targets" -Method Get -TimeoutSec 3
    $otelTarget = $targets.data.activeTargets | Where-Object { $_.job -eq "otel-collector" }
    
    if ($otelTarget.health -eq "up") {
        Write-Host "✅ Prometheus scraping OTEL Collector successfully" -ForegroundColor Green
    } else {
        Write-Host "❌ Prometheus cannot scrape OTEL Collector" -ForegroundColor Red
        Write-Host "   Last error: $($otelTarget.lastError)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "❌ Prometheus NOT reachable: $($_.Exception.Message)" -ForegroundColor Red
}

# 3. Check if app metrics are flowing
Write-Host "`n📈 Checking SeatGrid.API metrics..." -ForegroundColor Yellow
try {
    # Query for any SeatGrid.API metrics
    $query = 'http_server_request_duration_seconds_count{service_name="SeatGrid.API"}'
    $encodedQuery = [System.Web.HttpUtility]::UrlEncode($query)
    $result = Invoke-RestMethod -Uri "http://localhost:9090/api/v1/query?query=$encodedQuery" -Method Get -TimeoutSec 3
    
    if ($result.data.result.Count -gt 0) {
        $totalRequests = ($result.data.result | Measure-Object -Property value -Sum).Sum[1]
        Write-Host "✅ SeatGrid.API metrics flowing: $totalRequests total HTTP requests recorded" -ForegroundColor Green
    } else {
        Write-Host "⚠️  No metrics from SeatGrid.API yet" -ForegroundColor Yellow
        Write-Host "   This is normal if app just started or hasn't received traffic" -ForegroundColor Gray
        Write-Host "   Trigger traffic: curl http://localhost:5000/api/events" -ForegroundColor Yellow
    }
} catch {
    Write-Host "❌ Cannot query metrics: $($_.Exception.Message)" -ForegroundColor Red
}

# 4. Check custom cache metrics
Write-Host "`n💾 Checking cache metrics..." -ForegroundColor Yellow
try {
    $query = 'seatgrid_api_cache_checks_total'
    $encodedQuery = [System.Web.HttpUtility]::UrlEncode($query)
    $result = Invoke-RestMethod -Uri "http://localhost:9090/api/v1/query?query=$encodedQuery" -Method Get -TimeoutSec 3
    
    if ($result.data.result.Count -gt 0) {
        Write-Host "✅ Custom cache metrics flowing:" -ForegroundColor Green
        foreach ($metric in $result.data.result) {
            $cacheType = $metric.metric.cache_type
            $resultType = $metric.metric.result
            $count = $metric.value[1]
            Write-Host "   - $cacheType ($resultType): $count checks" -ForegroundColor Gray
        }
    } else {
        Write-Host "⚠️  No cache metrics yet (trigger booking requests)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "❌ Cannot query cache metrics: $($_.Exception.Message)" -ForegroundColor Red
}

# 5. Check Prometheus alerts
Write-Host "`n🚨 Checking Prometheus alerts..." -ForegroundColor Yellow
try {
    $alerts = Invoke-RestMethod -Uri "http://localhost:9090/api/v1/alerts" -Method Get -TimeoutSec 3
    $activeAlerts = $alerts.data.alerts | Where-Object { $_.state -eq "firing" }
    
    if ($activeAlerts.Count -eq 0) {
        Write-Host "✅ No active alerts (system healthy)" -ForegroundColor Green
    } else {
        Write-Host "❌ $($activeAlerts.Count) alert(s) firing:" -ForegroundColor Red
        foreach ($alert in $activeAlerts) {
            Write-Host "   - $($alert.labels.alertname): $($alert.annotations.summary)" -ForegroundColor Yellow
        }
    }
} catch {
    Write-Host "❌ Cannot check alerts: $($_.Exception.Message)" -ForegroundColor Red
}

# 6. Check observability health endpoint
Write-Host "`n🏥 Checking observability health endpoint..." -ForegroundColor Yellow
try {
    $obHealth = Invoke-RestMethod -Uri "http://localhost:5000/health/observability" -Method Get -TimeoutSec 3 -ErrorAction Stop
    Write-Host "✅ Observability health endpoint responding" -ForegroundColor Green
    Write-Host "   Timestamp: $($obHealth.timestamp)" -ForegroundColor Gray
} catch {
    Write-Host "⚠️  App not reachable: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host "   Start app: docker-compose -f docker-compose.app.yml up -d" -ForegroundColor Yellow
}

# Summary
Write-Host "`n" + "=" * 60
Write-Host "💡 Recommendations:" -ForegroundColor Cyan
Write-Host "   1. Set up monitoring to poll /health/observability every minute"
Write-Host "   2. Alert on TelemetryDataLoss (no metrics for 5+ minutes)"
Write-Host "   3. View alerts: http://localhost:9090/alerts"
Write-Host "   4. View metrics: http://localhost:9090/graph"
Write-Host "   5. Grafana dashboard: http://localhost:3001"
