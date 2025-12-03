# Metrics Testing Guide

## Overview

This guide shows how to verify the custom metrics implemented via manual decorators for Phase 3 cache optimization.

## What Metrics Are Available

### 1. Cache Operation Counters
**Metric**: `seatgrid_booking_cache_checks_total`

**Labels**:
- `result`: "hit" or "miss"
- `cache_type`: "availability" or "booked_seats"

**Purpose**: Track cache effectiveness (hit ratio calculation)

---

### 2. Database Query Counters
**Metric**: `seatgrid_booking_db_queries_total`

**Labels**:
- `query_type`: "seat_lookup", "seat_lookup_naive", "seat_lookup_pessimistic"

**Purpose**: Measure database load reduction

---

### 3. Booking Duration Histogram
**Metric**: `seatgrid_booking_duration_milliseconds`

**Labels**:
- `outcome`: "success", "conflict_cache", "conflict_db", "conflict_lock"

**Purpose**: Compare latency by request path (cache vs. database)

---

### 4. Cache Operation Duration Histogram
**Metric**: `seatgrid_cache_operation_duration_milliseconds`

**Labels**:
- `cache_type`: "availability", "booked_seats"
- `operation`: "get", "set", "increment", "decrement", "get_bulk", "add", "check_single"

**Purpose**: Monitor cache operation performance

---

## Testing Steps

### 1. Start the Infrastructure and Application

```powershell
# Start infrastructure (Postgres, Redis, OTEL Collector, Prometheus, Grafana)
docker-compose -f docker-compose.infra.yml up -d

# Start application
docker-compose -f docker-compose.app.yml up -d --build

# Verify all containers are running
docker-compose ps
```

**Expected output:**
```
NAME                       STATUS
seatgrid-api               Up (healthy)
seatgrid-postgres          Up (healthy)
seatgrid-redis             Up (healthy)
seatgrid-otel-collector    Up
seatgrid-prometheus        Up
seatgrid-grafana           Up
```

---

### 2. Verify OpenTelemetry Pipeline

#### **Step 2.1: Check Application is Exporting Metrics**

```powershell
# Check application logs for OTEL initialization
docker logs seatgrid-api | Select-String "OpenTelemetry"

# Expected output should include:
# - "OpenTelemetry resource configured"
# - No errors about OTLP exporter connection
```

#### **Step 2.2: Verify OTEL Collector is Receiving Data**

```powershell
# Check OTEL Collector logs
docker logs seatgrid-otel-collector --tail 50

# Look for:
# - "Everything is ready. Begin running and processing data."
# - No "connection refused" errors
# - Periodic "Metric" entries showing received data points
```

**Test the OTLP endpoint directly:**
```powershell
# OTEL Collector should respond on port 4317 (gRPC)
Test-NetConnection -ComputerName localhost -Port 4317

# Expected: TcpTestSucceeded : True
```

#### **Step 2.3: Verify Prometheus is Scraping OTEL Collector**

Open Prometheus UI: http://localhost:9090

1. Go to **Status → Targets**
2. Find target: `otel-collector (0/1 up)`
3. Verify:
   - **State**: `UP` (green)
   - **Endpoint**: `http://otel-collector:8889/metrics`
   - **Last Scrape**: Within last 15 seconds
   - **Scrape Duration**: < 100ms

**If target is DOWN:**
```powershell
# Check OTEL Collector Prometheus exporter port
docker exec seatgrid-otel-collector wget -O- http://localhost:8889/metrics

# Should return OpenMetrics format data
```

---

### 3. Generate Test Traffic

```powershell
# Create an event first
curl -X POST http://localhost:5000/api/Events `
  -H "Content-Type: application/json" `
  -d '{"Name":"Test Event","Date":"2025-12-10T19:00:00Z","Rows":10,"Cols":10}'

# Run load test to generate metrics
k6 run tests/k6/crash_test.js
```

**What this does:**
- Creates 100 seats for an event
- Generates ~165,000 booking requests
- Triggers all metric collection paths (cache hits, DB queries, etc.)

---

### 4. Verify Custom Metrics Exist in Prometheus

Open Prometheus at http://localhost:9090 and run these verification queries:

### 4. Verify Custom Metrics Exist in Prometheus

Open Prometheus at http://localhost:9090 and run these verification queries:

#### **Verification Query 1: Check All SeatGrid Metrics**
```promql
{__name__=~"seatgrid.*"}
```

**Expected results** (should see all these metrics):
- `seatgrid_booking_cache_checks_total` ✅
- `seatgrid_booking_db_queries_total` ✅
- `seatgrid_booking_duration_milliseconds_bucket` ✅
- `seatgrid_booking_duration_milliseconds_count` ✅
- `seatgrid_booking_duration_milliseconds_sum` ✅
- `seatgrid_cache_operation_duration_milliseconds_bucket` ✅
- `seatgrid_cache_operation_duration_milliseconds_count` ✅
- `seatgrid_cache_operation_duration_milliseconds_sum` ✅
- `seatgrid_http_server_request_duration_seconds_*` (from ASP.NET Core instrumentation) ✅

**If you don't see these metrics:**
- Check Step 2 (OTEL pipeline verification)
- Ensure you ran the k6 test (metrics only appear after traffic)
- Wait 15-30 seconds for Prometheus scrape interval

---

#### **Verification Query 2: Cache Checks Are Recording**
```promql
# Should show counts for each result type
sum by (cache_type, result) (seatgrid_booking_cache_checks_total)
```

**Expected output:**
| cache_type | result | value |
|------------|--------|-------|
| availability | found | ~165,000 |
| availability | empty | ~100 |
| booked_seats | found | ~50,000 |
| booked_seats | empty | ~50,000 |
| booked_seats | error | 0 (or small number if Redis had issues) |

**If all values are 0:**
- Decorators aren't being called → check DI registration in `Program.cs`
- Metrics aren't being recorded → check `BookingMetrics.RecordCacheCheck()` calls

---

#### **Verification Query 3: Database Queries Are Recording**
```promql
# Should show counts by query type
sum by (query_type) (seatgrid_booking_db_queries_total)
```

**Expected output** (if using optimistic strategy):
| query_type | value |
|------------|-------|
| seat_lookup | ~100 (only successful bookings hit DB) |

**If value is 0:**
- `RecordDatabaseQuery()` isn't being called in booking services
- Check that you added metrics to `BookingOptimisticService.cs`

---

#### **Verification Query 4: Booking Duration Is Recording**
```promql
# Should show counts by outcome
sum by (outcome) (seatgrid_booking_duration_milliseconds_count)
```

**Expected output:**
| outcome | value |
|---------|-------|
| conflict_cache | ~165,000 (most conflicts rejected by cache) |
| conflict_db | ~500 (some conflicts reached DB) |
| success | ~100 (actual bookings) |

**If all values are 0:**
- `RecordBookingDuration()` isn't being called
- Check that you added stopwatch + metrics to booking services

---

### 5. Useful Analysis Queries

### 5. Useful Analysis Queries

#### **Business Cache Hit Ratio** (Most Important)
```promql
# Percentage of cache checks that provided useful data
sum(rate(seatgrid_booking_cache_checks_total{result="found"}[5m]))
/
sum(rate(seatgrid_booking_cache_checks_total[5m])) * 100
```
**Target**: >85% (cache is effectively reducing DB load)  
**Interpretation**: 
- 90%+ = Excellent (cache is providing value most of the time)
- 50-90% = Good (cache is helping but needs tuning)
- <50% = Poor (cache isn't warming up properly or events are too new)

---

#### **Redis Availability**
```promql
# Percentage of cache operations that succeeded (found + empty)
sum(rate(seatgrid_booking_cache_checks_total{result=~"found|empty"}[5m]))
/
sum(rate(seatgrid_booking_cache_checks_total[5m])) * 100
```
**Target**: >99.5% (Redis should be highly available)  
**Interpretation**:
- <99% = Infrastructure problem, investigate Redis health

---

#### **Database Query Rate**
```promql
# Queries per second
sum(rate(seatgrid_booking_db_queries_total[5m]))
```
**Compare to total request rate:**
```promql
# Total booking requests per second
sum(rate(seatgrid_booking_duration_milliseconds_count[5m]))
```
**Target**: DB query rate should be <5% of total requests in Phase 3

---

#### **Fast-Path Effectiveness** (Phase 3 KPI)
```promql
# Percentage of conflicts resolved via cache (no DB query)
sum(rate(seatgrid_booking_duration_milliseconds_count{outcome="conflict_cache"}[5m]))
/
sum(rate(seatgrid_booking_duration_milliseconds_count{outcome=~"conflict.*"}[5m])) * 100
```
**Target**: >95% (most conflicts should be caught by cache)  
**This directly proves Phase 3's "99.9% DB query reduction" claim**

---

#### **Cache Operation Latency (P95)**
```promql
# 95th percentile duration for each cache operation
histogram_quantile(0.95, 
  sum(rate(seatgrid_cache_operation_duration_milliseconds_bucket[5m])) 
  by (le, cache_type, operation))
```
**Target**: 
- `get` operations: <5ms
- `set/add` operations: <10ms
- `increment/decrement`: <5ms

---

#### **Booking Latency by Path (P95)**
```promql
# Compare latency for cache vs DB conflicts
histogram_quantile(0.95,
  sum(rate(seatgrid_booking_duration_milliseconds_bucket[5m])) 
  by (le, outcome))
```
**Expected values:**
- `conflict_cache`: 20-50ms (cache check + response)
- `conflict_db`: 100-300ms (DB query + response)
- `success`: 500-1000ms (full transaction + commit)

**This proves the Phase 3 claim: "Cache conflicts resolve in <50ms"**

---

#### **Cache Hit Ratio by Type**
```promql
# Separate analysis for each cache type
sum(rate(seatgrid_booking_cache_checks_total{result="found"}[5m])) by (cache_type)
/
sum(rate(seatgrid_booking_cache_checks_total[5m])) by (cache_type) * 100
```
**Expected:**
- `availability`: ~99% (very stable, rarely changes)
- `booked_seats`: ~85-95% (depends on how many new events vs active events)

---

#### **Database Load Reduction** (Phase 2 vs Phase 3)
```promql
# Current DB query rate
sum(rate(seatgrid_booking_db_queries_total[5m]))

# In Phase 2, this would equal total request rate
# In Phase 3, this should be ~1% of total request rate
```

**To calculate reduction percentage:**
```promql
# DB queries avoided by cache (%)
(1 - (
  sum(rate(seatgrid_booking_db_queries_total[5m]))
  /
  sum(rate(seatgrid_booking_duration_milliseconds_count[5m]))
)) * 100
```
**Target**: >95% (Phase 3 should avoid 95%+ of DB queries)

---

#### **Request Rate (Overall System Health)**
```promql
# Total booking requests per second
sum(rate(seatgrid_http_server_request_duration_seconds_count{http_route="/api/Bookings"}[5m]))
```
**Compare to Phase 2 baseline:**
- Phase 2: ~200 RPS before system degraded
- Phase 3 Target: >4,000 RPS sustained

---

### 6. Grafana Dashboard Setup (Recommended)

Create a dashboard with these panels to visualize the metrics:

**Panel 1: Cache Effectiveness Over Time**
```promql
sum(rate(seatgrid_booking_cache_checks_total{result="found"}[1m])) by (cache_type)
/
sum(rate(seatgrid_booking_cache_checks_total[1m])) by (cache_type) * 100
```
- Visualization: Time series
- Y-axis: 0-100%
- Shows: Real-time cache hit ratio trending

**Panel 2: Database vs Cache Conflicts**
```promql
sum(rate(seatgrid_booking_duration_milliseconds_count[1m])) by (outcome)
```
- Visualization: Stacked area chart
- Shows: Visual confirmation that `conflict_cache` >> `conflict_db`

**Panel 3: Latency Comparison (Cache vs DB)**
```promql
histogram_quantile(0.95,
  sum(rate(seatgrid_booking_duration_milliseconds_bucket[1m])) 
  by (le, outcome))
```
- Visualization: Time series (multiple lines)
- Shows: P95 latency gap between cache and DB paths

**Panel 4: System Throughput**
```promql
sum(rate(seatgrid_booking_duration_milliseconds_count[1m]))
```
- Visualization: Single stat + sparkline
- Shows: Current RPS (should be 10-20x Phase 2)

---

## Quick Validation Checklist

Use this checklist to verify your observability setup is working correctly:

### ✅ Phase 1: Infrastructure
- [ ] All containers running: `docker-compose ps` shows 6 services UP
- [ ] OTEL Collector responding: `curl http://localhost:8889/metrics` returns data
- [ ] Prometheus UI accessible: http://localhost:9090 loads
- [ ] Grafana UI accessible: http://localhost:3000 loads

### ✅ Phase 2: OTEL Pipeline
- [ ] Application exporting metrics: `docker logs seatgrid-api` shows no OTLP errors
- [ ] OTEL Collector receiving data: `docker logs seatgrid-otel-collector` shows "MetricsExporter" entries
- [ ] Prometheus scraping OTEL: http://localhost:9090/targets shows "otel-collector" UP (green)
- [ ] Prometheus has data: Query `up` returns `1` for all targets

### ✅ Phase 3: Custom Metrics
- [ ] Metrics exist: Query `{__name__=~"seatgrid.*"}` returns >5 metrics
- [ ] Cache checks recording: Query `sum(seatgrid_booking_cache_checks_total)` > 0
- [ ] DB queries recording: Query `sum(seatgrid_booking_db_queries_total)` > 0
- [ ] Booking duration recording: Query `sum(seatgrid_booking_duration_milliseconds_count)` > 0

### ✅ Phase 4: Business Metrics (After Load Test)
- [ ] Cache hit ratio >85%: See "Business Cache Hit Ratio" query
- [ ] Fast-path effectiveness >95%: See "Fast-Path Effectiveness" query
- [ ] DB query reduction >95%: See "Database Load Reduction" query
- [ ] Cache latency <50ms P95: See "Cache Operation Latency" query

---

## Expected Results

### Phase 3 Success Indicators

1. **Cache Hit Ratio**: Should be >90% after first few bookings
   - Availability cache: ~100% (once initialized)
   - Booked seats cache: ~90-95% (after warm-up)

2. **Database Query Reduction**: 
   - Phase 2: Every request hits DB
   - Phase 3: <5% of requests hit DB (95% reduction)

3. **Cache Operation Duration**:
   - GET operations: <5ms P95
   - SET/ADD operations: <10ms P95

4. **Booking Duration**:
   - `outcome="conflict_cache"`: <50ms P95 (fast rejection)
   - `outcome="conflict_db"`: 100-200ms P95 (fallback path)
   - `outcome="success"`: 200-500ms P95 (actual booking)

---

## Grafana Dashboard (Optional)

If you want to visualize these metrics in Grafana:

1. Open Grafana at http://localhost:3000
2. Create a new dashboard
3. Add panels with the PromQL queries above

### Example Panel Configurations

**Panel 1: Cache Hit Ratio Over Time**
- Visualization: Time series
- Query: Cache hit ratio overall
- Y-axis: Percentage (0-100)

**Panel 2: Database Load Reduction**
- Visualization: Stat (single value)
- Query: `sum(rate(seatgrid_booking_db_queries_total[5m]))`
- Compare to Phase 2 baseline

**Panel 3: Latency by Path**
- Visualization: Time series (multi-line)
- Query: P95 booking duration by outcome
- Shows cache vs. DB path performance

---

## Troubleshooting

### Issue 1: No Metrics Appearing in Prometheus

**Symptom**: `{__name__=~"seatgrid.*"}` returns no results

**Diagnostic steps:**

```powershell
# Step 1: Check if application is running
docker ps | Select-String "seatgrid-api"
# Should show: Up (healthy)

# Step 2: Check OTEL Collector is receiving data
docker logs seatgrid-otel-collector --tail 20
# Look for: "MetricsExporter" entries with non-zero data points

# Step 3: Test OTEL Collector Prometheus exporter directly
curl http://localhost:8889/metrics | Select-String "seatgrid"
# Should return metrics in OpenMetrics format

# Step 4: Check Prometheus target status
# Open: http://localhost:9090/targets
# otel-collector target should be UP (green)
```

**Common causes:**

1. **OTEL Collector not started**: `docker-compose -f docker-compose.infra.yml up -d otel-collector`
2. **Wrong OTLP endpoint**: Check `OTEL_EXPORTER_OTLP_ENDPOINT` in docker-compose.app.yml
3. **Meter not registered**: Verify `Program.cs` has `.AddMeter("SeatGrid.API")`
4. **No traffic generated**: Run k6 test to generate metrics

---

### Issue 2: Metrics Exist But All Values Are Zero

**Symptom**: Metrics appear in Prometheus but show `0` values

**Diagnostic steps:**

```powershell
# Check if decorators are registered in DI
docker logs seatgrid-api | Select-String "InstrumentedAvailabilityCache"
# Should show: DI registration logs

# Check if events were created
curl http://localhost:5000/api/Events | ConvertFrom-Json
# Should return list of events

# Check if k6 test ran successfully
# Look for: "165911 requests" in k6 output
```

**Common causes:**

1. **Decorators not registered**: Check `Program.cs` DI configuration
2. **No test traffic**: Run `k6 run tests/k6/crash_test.js`
3. **Metrics code not called**: Verify `RecordCacheCheck()` / `RecordBookingDuration()` calls exist

---

### Issue 3: High Cache Miss Rate (Low "found" Percentage)

**Symptom**: Business cache hit ratio <50%

**Diagnostic steps:**

```promql
# Check distribution of cache results
sum by (result) (seatgrid_booking_cache_checks_total{cache_type="booked_seats"})
```

**Common causes:**

1. **Cache not warming up**:
   ```powershell
   # Check if cache is being populated after bookings
   docker exec seatgrid-redis redis-cli KEYS "event:*"
   # Should show keys like: event:1:available, event:1:booked
   ```

2. **TTL too short**:
   ```powershell
   # Check TTL on cache keys
   docker exec seatgrid-redis redis-cli TTL "event:1:booked"
   # Should show: ~86400 (24 hours)
   ```

3. **Redis memory full (evicting keys)**:
   ```powershell
   # Check Redis memory usage
   docker exec seatgrid-redis redis-cli INFO memory
   # used_memory should be < 256MB (maxmemory limit)
   ```

4. **Testing with too many events** (cache spread thin):
   - Each event creates separate cache keys
   - Test with 1-5 events to see proper cache behavior

---

### Issue 4: Redis Connection Errors

**Symptom**: High `result="error"` count in cache checks

**Diagnostic steps:**

```powershell
# Check Redis is running
docker ps | Select-String "redis"
# Should show: Up (healthy)

# Test Redis connectivity from application
docker exec seatgrid-api curl http://redis:6379
# Should respond (connection test)

# Check Redis logs for errors
docker logs seatgrid-redis --tail 50
# Look for: connection errors, OOM errors
```

**Common causes:**

1. **Redis container down**: `docker-compose -f docker-compose.infra.yml up -d redis`
2. **Network issues**: Check `docker network inspect` for connectivity
3. **Connection string wrong**: Verify `ConnectionStrings:Redis` in appsettings.json

---

### Issue 5: OTEL Collector Not Forwarding to Prometheus

**Symptom**: Application logs show metrics, but Prometheus has none

**Diagnostic steps:**

```powershell
# Check OTEL Collector config
docker exec seatgrid-otel-collector cat /etc/otel-collector-config.yaml
# Verify prometheus exporter endpoint: "0.0.0.0:8889"

# Test Prometheus exporter directly
curl http://localhost:8889/metrics
# Should return OpenMetrics format data

# Check if Prometheus can reach OTEL Collector
docker exec seatgrid-prometheus wget -O- http://otel-collector:8889/metrics
# Should succeed (inside Docker network)
```

**Common causes:**

1. **Exporter not configured**: Check `exporters.prometheus` in otel-collector-config.yaml
2. **Wrong port exposed**: Verify `8889:8889` in docker-compose.infra.yml
3. **Prometheus scrape config wrong**: Check `prometheus.yaml` has `otel-collector:8889` target

---

### Issue 6: Database Query Count Seems Wrong

**Symptom**: `seatgrid_booking_db_queries_total` doesn't match expectations

**Quick validation:**

```promql
# Compare DB queries to total requests
sum(rate(seatgrid_booking_db_queries_total[5m]))
/
sum(rate(seatgrid_booking_duration_milliseconds_count[5m])) * 100
```

**Expected**: <5% in Phase 3 (cache prevents most DB queries)

**If too high (>10%):**
- Cache isn't working → check cache hit ratio
- Cache isn't being checked → verify fast-path code in BookingOptimisticService

**If zero (0%):**
- `RecordDatabaseQuery()` calls missing → add to booking services

---

### Issue 7: Histogram Buckets Not Recording

**Symptom**: `histogram_quantile()` queries return no data

**Diagnostic steps:**

```promql
# Check if histogram samples exist
sum(seatgrid_booking_duration_milliseconds_count)
# Should be > 0

# Check if buckets exist
sum by (le) (seatgrid_booking_duration_milliseconds_bucket)
# Should show multiple buckets: +Inf, 1000, 500, 100, etc.
```

**Common causes:**

1. **No histogram.Record() calls**: Verify `RecordBookingDuration()` / `RecordCacheOperationDuration()` are called
2. **Wrong metric type**: Histogram vs Counter confusion (check `BookingMetrics.cs` definitions)

---

## Next Steps

After verifying metrics work:

1. **Add Grafana Dashboard**: Create visual representation of Phase 3 improvements
2. **Set Alerts**: Configure Prometheus alerts for low cache hit ratio
3. **Document Baseline**: Compare Phase 2 vs. Phase 3 metrics in results doc
4. **Consider Source Generators**: If managing 5+ decorators, automate with generators

---

## Key Insights from Decorator Pattern

### Benefits Observed
- ✅ **Separation of Concerns**: Business logic (cache) separate from observability (metrics)
- ✅ **Zero Coupling**: Cache services don't know about metrics
- ✅ **Easy Testing**: Can test cache logic without metrics overhead
- ✅ **Flexible**: Can add/remove metrics by changing DI registration

### Trade-offs
- ⚠️ **Manual Work**: Each interface needs a decorator class
- ⚠️ **Boilerplate**: Similar code across decorators (good candidate for generators!)
- ⚠️ **Runtime Cost**: Extra method calls (negligible ~nanoseconds vs. ms operations)

### When to Use Source Generators
- Multiple similar decorators (5+ interfaces)
- Frequent interface changes (methods added/removed)
- Team prefers code generation over manual maintenance
- Want compile-time safety over runtime reflection
