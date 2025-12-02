# Phase 3 Implementation Plan (Revised): Booking Write-Path Optimization

## Executive Summary

Phase 3 focuses on **optimizing the booking write path** to address the critical finding from Phase 2: **99% of booking requests (8,900+ of 9,296) unnecessarily execute expensive database queries only to discover seats are unavailable**. This phase implements intelligent caching layers to provide fast-path rejection, reducing P95 latency from 13-15 seconds to <100ms for conflicts and <500ms overall.

### Key Architectural Insight

The bottleneck is NOT the read path (GET /events/{id}/seats) - PostgreSQL buffer cache already handles those efficiently. The bottleneck is the **booking write path** where thousands of concurrent requests compete for the same seats, each executing:
1. DB query for candidate seats (50-100ms)
2. In-memory availability check
3. Optimistic lock attempt at SaveChanges
4. Conflict detection (after wasting DB resources)

**Solution**: Cache availability state (available count + booked seats) to enable fast rejection BEFORE hitting the database.

---

## Current State Assessment

### ✅ Already Implemented (Foundation Ready)

1. **Infrastructure**
   - Redis 7-alpine deployed with LRU eviction policy (256MB max memory)
   - Health checks for Redis connectivity
   - OpenTelemetry Redis instrumentation configured

2. **Application Architecture**
   - Strategy pattern for booking services (Naive, Pessimistic, Optimistic)
   - Configuration-driven strategy selection via `appsettings.json`
   - Distributed cache DI (`IDistributedCache`) with StackExchange.Redis

3. **Observability Stack**
   - Prometheus, Grafana, Tempo, Loki operational
   - Custom metrics infrastructure ready
   - Distributed tracing showing full request path

### ❌ Missing Components (Phase 3 Scope)

1. **Availability Cache (Critical)**
   - No available seat count tracking per event
   - No fast-path rejection for sold-out events
   - 8,900+ requests waste DB queries discovering "sold out"

2. **Booked Seats Cache (High Impact)**
   - No cache of already-booked seats for fast conflict detection
   - Every booking request queries DB even for seats already known to be booked
   - Cache would enable <5ms rejection vs. 100ms+ DB query

3. **Fast-Path Rejection Logic**
   - No early-exit before expensive DB operations
   - BookingService always executes GetSeatsAsync() regardless of likelihood of success

4. **Monitoring**
   - No custom metrics for cache effectiveness
   - No visibility into fast-path vs. DB-path split
   - No dashboard panels for booking optimization performance

---

## Phase 3 Goals & Success Metrics

### Primary Objectives

| Goal | Current (Phase 2) | Phase 3 Target | Rationale |
|------|-------------------|----------------|-----------|
| **P95 Latency (Conflict)** | 13-15s | <50ms | Fast rejection for unavailable seats |
| **P95 Latency (Success)** | 6-7s | <500ms | Maintain acceptable booking time |
| **Avg Latency (Conflict)** | ~6s | <20ms | Cache-based rejection path |
| **DB Query Reduction (Conflicts)** | 100% | <5% | 95%+ rejected via cache |
| **Booked Seats Cache Hit Ratio** | N/A | >90% | After first few bookings |
| **Available Count Cache Hit** | N/A | 100% | Sold-out detection |
| **False Cache Miss Rate** | N/A | <1% | Stale cache → fallback to DB |

### Non-Goals (Deferred to Phase 4)

- Asynchronous booking processing (queue-based)
- Distributed locks for reservation flow
- Multi-region cache replication
- Cache stampede protection (acceptable risk at current scale)

---

## Implementation Plan

### Track 1: Available Seat Count Cache (Week 1 - Critical Path)

#### **Task 1.1: Create Availability Cache Service**

**File**: `src/SeatGrid.API/Application/Services/AvailabilityCache.cs`

```csharp
public interface IAvailabilityCache
{
    Task<int?> GetAvailableCountAsync(long eventId, CancellationToken ct);
    Task SetAvailableCountAsync(long eventId, int count, CancellationToken ct);
    Task<bool> DecrementAvailableCountAsync(long eventId, int delta, CancellationToken ct);
}
```

**Implementation Details**:
- **Redis Key**: `event:{eventId}:available` → Integer count
- **Operations**: GET (check), DECRBY (atomic decrement), SET (initialize)
- **TTL**: Event end time + 1 hour (auto-cleanup)
- **Thread Safety**: Redis atomic operations (DECRBY is inherently thread-safe)
- **Cache Miss**: Return null → Caller queries DB and sets cache

**Fast-Path Flow**:
```
POST /api/Bookings (eventId=123, 2 seats)
  → GetAvailableCountAsync(123)
    → Redis GET "event:123:available" → 0
    → Return Conflict("Event sold out") in <5ms
```

**Normal Flow**:
```
POST /api/Bookings (eventId=123, 2 seats)
  → GetAvailableCountAsync(123)
    → Redis GET "event:123:available" → 47
    → Proceed to booked seats cache check
    → [After successful booking]
    → DecrementAvailableCountAsync(123, 2) → Redis DECRBY → 45
```

**Expected Impact**:
- Sold-out rejections: 13s → <5ms (2600x faster)
- Database queries for sold-out events: -100%
- Memory: 8 bytes per event
- Risk: None (cache miss = DB fallback)

---

#### **Task 1.2: Integrate Availability Cache in BookingsController**

**Changes**:

1. **Register Service** (`Program.cs`):
   ```csharp
   builder.Services.AddScoped<IAvailabilityCache, AvailabilityCache>();
   ```

2. **Update BookingsController** (`Controllers/BookingsController.cs`):
   ```csharp
   [HttpPost]
   public async Task<IActionResult> BookSeats([FromBody] BookingRequest request)
   {
       // Fast-path: Check if event has enough available seats
       var availableCount = await _availabilityCache.GetAvailableCountAsync(request.EventId);
       
       if (availableCount == 0)
       {
           return Conflict(new BookingErrorResponse(false, "Event is sold out."));
       }
       
       if (availableCount.HasValue && availableCount < request.Seats.Count)
       {
           return Conflict(new BookingErrorResponse(false, 
               $"Only {availableCount} seats available, requested {request.Seats.Count}."));
       }
       
       // Proceed to booking service (existing logic)
       var result = await _bookingService.BookSeatsAsync(...);
       
       // Update cache on successful booking
       if (result.IsSuccess)
       {
           await _availabilityCache.DecrementAvailableCountAsync(
               request.EventId, 
               result.Value.SeatCount);
       }
       
       return result.Match<IActionResult>(...);
   }
   ```

3. **Initialize Cache on Event Creation** (`EventService.cs`):
   ```csharp
   public async Task<EventResponse> CreateEventAsync(...)
   {
       var eventEntity = new Event { ... };
       var totalSeats = rows * cols;
       
       // Create event and seats in DB
       await _repository.CreateEventAsync(eventEntity, totalSeats);
       
       // Initialize availability cache
       await _availabilityCache.SetAvailableCountAsync(eventEntity.Id, totalSeats);
       
       return new EventResponse(...);
   }
   ```

**Testing Strategy**:
- Unit test: Mock cache, verify fast-path logic
- Integration test: Verify sold-out rejection without DB query
- Manual test: Monitor Redis with `MONITOR` command during booking

---

#### **Task 1.3: Add Custom Metrics**

**File**: `src/SeatGrid.API/Application/Observability/CacheMetrics.cs`

```csharp
public class CacheMetrics
{
    private static readonly Counter<long> CacheOperations = 
        Meter.CreateCounter<long>("seatgrid.cache.operations", "operations", 
            "Number of cache operations");
    
    public static void RecordCacheHit(string cacheType) => 
        CacheOperations.Add(1, new KeyValuePair<string, object?>("operation", "hit"),
                               new KeyValuePair<string, object?>("cache_type", cacheType));
    
    public static void RecordCacheMiss(string cacheType) => 
        CacheOperations.Add(1, new KeyValuePair<string, object?>("operation", "miss"),
                               new KeyValuePair<string, object?>("cache_type", cacheType));
}
```

**Prometheus Metrics**:
- `seatgrid_cache_operations_total{operation="hit", cache_type="seat_map"}`
- `seatgrid_cache_operations_total{operation="miss", cache_type="seat_map"}`
- Derived: `cache_hit_ratio = sum(rate(hit)) / sum(rate(hit + miss))`

**Grafana Panel** (Add to existing dashboard):
```promql
# Cache Hit Ratio (5min avg)
sum(rate(seatgrid_cache_operations_total{operation="hit"}[5m])) 
/ 
sum(rate(seatgrid_cache_operations_total[5m])) * 100
```

---

### Track 2: Booked Seats Cache (Week 2 - High Impact Optimization)

#### **Task 2.1: Create Booked Seats Cache Service**

**File**: `src/SeatGrid.API/Application/Services/BookedSeatsCache.cs`

```csharp
public interface IBookedSeatsCache
{
    Task<HashSet<string>> GetBookedSeatKeysAsync(long eventId, CancellationToken ct);
    Task AddBookedSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken ct);
    Task<bool> IsSeatBookedAsync(long eventId, string row, string col, CancellationToken ct);
}
```

**Implementation Details**:
- **Redis Data Structure**: SET (Redis native set for O(1) membership)
- **Key Format**: `event:{eventId}:booked` → Set of "Row-Col" strings
- **Operations**: SADD (add seats), SISMEMBER (check single), SMEMBERS (get all)
- **TTL**: Event end time + 1 hour
- **Cache-as-Hint**: Cache miss or stale = fallback to DB query (no correctness impact)

**Key Insight**: This is a **best-effort optimization layer**. If cache is stale/missing, the existing optimistic locking in BookingService handles correctness.

**Fast-Path Flow**:
```
POST /api/Bookings (eventId=123, seats=["A-1", "A-2"])
  → GetBookedSeatKeysAsync(123)
    → Redis SMEMBERS "event:123:booked" → {"A-1", "B-5", "C-3", ...}
    → Check: "A-1" in bookedSeats? YES
    → Return Conflict("Seat A-1 already booked") in <5ms (NO DB QUERY!)
```

**Cache Miss Flow**:
```
POST /api/Bookings (eventId=123, seats=["A-1"])
  → GetBookedSeatKeysAsync(123)
    → Redis SMEMBERS "event:123:booked" → (empty or partial)
    → Proceed to BookingService.GetSeatsAsync() [existing DB query]
    → Optimistic lock handles correctness
    → After successful booking → AddBookedSeatsAsync() updates cache
```

**Expected Impact**:
- 90%+ of unavailable seat requests: <5ms rejection (vs. 100ms DB query)
- Database queries reduced by 90-95% during active booking phase
- Memory: ~1KB per 100-seat event (10KB for 1000 events)
- Risk: Low (cache miss = existing flow, no new failure modes)

---

#### **Task 2.2: Integrate Booked Seats Cache in BookingService**

**Changes**:

1. **Register Service** (`Program.cs`):
   ```csharp
   builder.Services.AddScoped<IBookedSeatsCache, BookedSeatsCache>();
   ```

2. **Update BookingOptimisticService** (Add fast-path BEFORE GetSeatsAsync):
   ```csharp
   public async Task<Result<BookingSuccess, BookingError>> BookSeatsAsync(...)
   {
       if (seatPairs == null || !seatPairs.Any())
           return Result.Failure(new BookingError("No seats specified."));

       var distinctSeatPairs = seatPairs.Distinct().ToList();

       // NEW: Fast-path check via booked seats cache
       var bookedSeats = await _bookedSeatsCache.GetBookedSeatKeysAsync(eventId, cancellationToken);
       if (bookedSeats.Any())
       {
           var alreadyBooked = distinctSeatPairs
               .Where(p => bookedSeats.Contains($"{p.Row}-{p.Col}"))
               .ToList();
           
           if (alreadyBooked.Any())
           {
               return Result.Failure(new BookingError(
                   "One or more seats are already booked (cached).", 
                   new { AlreadyBooked = alreadyBooked }));
           }
       }

       try
       {
           // EXISTING: Database query and optimistic lock flow (unchanged)
           var seats = await GetSeatsAsync(eventId, distinctSeatPairs, cancellationToken);
           
           // ... rest of existing validation and update logic ...
           
           var affectedRows = await _context.SaveChangesAsync(cancellationToken);

           // NEW: Update cache after successful booking
           await _bookedSeatsCache.AddBookedSeatsAsync(eventId, distinctSeatPairs, cancellationToken);

           return Result.Success(new BookingSuccess(seats.Count));
       }
       catch (DbUpdateConcurrencyException)
       {
           // Existing optimistic lock conflict handling (unchanged)
           return Result.Failure(new BookingError("Booking conflict: ..."));
       }
   }
   ```

**Critical Design Decision**: Cache check happens BEFORE `GetSeatsAsync()`, but database remains the source of truth. Cache miss or stale data simply means we fall through to the existing battle-tested optimistic locking flow.

**Trade-offs**:
- **Pro**: 90-95% of conflict requests avoid DB query entirely
- **Pro**: No new correctness issues (DB + optimistic lock remains authoritative)
- **Pro**: Graceful degradation (cache failure = existing Phase 2 behavior)
- **Con**: ~1% cache staleness window (acceptable - optimistic lock catches it)
- **Con**: Extra Redis round-trip adds ~2-5ms (negligible vs. 100ms DB query saved)

---

### Track 3: Observability & Metrics (Week 2 - Essential for Validation)

#### **Task 3.1: Add Custom Metrics for Cache Performance**

**File**: `src/SeatGrid.API/Application/Observability/BookingMetrics.cs`

```csharp
public static class BookingMetrics
{
    private static readonly Meter Meter = new("SeatGrid.API");
    
    private static readonly Counter<long> CacheChecks = 
        Meter.CreateCounter<long>("seatgrid.booking.cache_checks", "checks",
            "Number of booking cache checks");
    
    private static readonly Counter<long> DatabaseQueries = 
        Meter.CreateCounter<long>("seatgrid.booking.db_queries", "queries",
            "Number of booking database queries");
    
    private static readonly Histogram<double> BookingDuration = 
        Meter.CreateHistogram<double>("seatgrid.booking.duration", "ms",
            "Booking request duration");
    
    public static void RecordCacheHit(string cacheType) =>
        CacheChecks.Add(1, 
            new KeyValuePair<string, object?>("result", "hit"),
            new KeyValuePair<string, object?>("cache_type", cacheType));
    
    public static void RecordCacheMiss(string cacheType) =>
        CacheChecks.Add(1,
            new KeyValuePair<string, object?>("result", "miss"),
            new KeyValuePair<string, object?>("cache_type", cacheType));
    
    public static void RecordDatabaseQuery(string queryType) =>
        DatabaseQueries.Add(1,
            new KeyValuePair<string, object?>("query_type", queryType));
    
    public static void RecordBookingDuration(double durationMs, string outcome) =>
        BookingDuration.Record(durationMs,
            new KeyValuePair<string, object?>("outcome", outcome));
}
```

**Prometheus Metrics**:
- `seatgrid_booking_cache_checks_total{result="hit|miss", cache_type="availability|booked_seats"}`
- `seatgrid_booking_db_queries_total{query_type="availability|seat_lookup"}`
- `seatgrid_booking_duration_seconds_bucket{outcome="success|conflict_cache|conflict_db"}`

**Key Derived Metrics**:
```promql
# Cache effectiveness - percentage of requests avoiding DB
rate(seatgrid_booking_cache_checks_total{result="hit"}[5m]) 
/ 
rate(seatgrid_booking_cache_checks_total[5m]) * 100

# Database load reduction
rate(seatgrid_booking_db_queries_total[5m])
```

---

#### **Task 3.2: Update Grafana Dashboard**

**New Panels to Add**:

1. **Booking Cache Effectiveness**
   ```promql
   # Cache hit ratio over time
   sum(rate(seatgrid_booking_cache_checks_total{result="hit"}[5m])) 
   / 
   sum(rate(seatgrid_booking_cache_checks_total[5m])) * 100
   ```

2. **Database Query Reduction**
   ```promql
   # Queries per second comparison
   sum(rate(seatgrid_booking_db_queries_total[5m]))
   ```

3. **Booking Latency by Path**
   ```promql
   # P95 latency split by cache vs. DB path
   histogram_quantile(0.95,
     sum(rate(seatgrid_booking_duration_seconds_bucket[5m])) 
     by (le, outcome))
   ```

4. **Fast-Path Rejection Rate**
   ```promql
   # Percentage of requests rejected via cache
   sum(rate(seatgrid_booking_cache_checks_total{result="hit", cache_type="booked_seats"}[5m]))
   /
   sum(rate(http_server_request_duration_seconds_count{endpoint="/api/Bookings"}[5m]))
   * 100
   ```

---

### Track 4: Database Optimization (Optional - Low Priority)

#### **Task 4.1: Add Covering Index (Only if Phase 3 tests show DB still bottlenecked)**

**Migration**: `src/SeatGrid.API/Migrations/YYYYMMDD_AddSeatQueryOptimizationIndex.cs`

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql(@"
        CREATE INDEX IF NOT EXISTS idx_seats_event_status_covering 
        ON ""Seats"" (""EventId"", ""Status"") 
        INCLUDE (""Row"", ""Col"", ""CurrentHolderId"");
    ");
}
```

**When to Implement**:
- Only if load tests show `GetSeatsAsync()` DB query is still slow (>50ms P95)
- With caching in place, 95%+ of requests won't hit this query
- Low priority - may not be needed at all

**Expected Impact (if needed)**:
- DB query time: 50-100ms → 10-20ms
- But only affects the <5% of requests that bypass cache

---

## Testing Strategy

### Test 1: Cache Effectiveness (Baseline)

**Script**: `tests/k6/phase3_cache_baseline.js`

```javascript
export default function () {
  const eventId = 1;
  const response = http.get(`${BASE_URL}/api/Events/${eventId}/seats`);
  
  check(response, {
    'status is 200': (r) => r.status === 200,
    'cache header present': (r) => r.headers['X-Cache-Status'] !== undefined,
  });
  
  cacheHits.add(response.headers['X-Cache-Status'] === 'HIT' ? 1 : 0);
}

// Scenario: 500 VUs, 30 seconds, same event
// Expected: 95%+ cache hits after first 5 seconds
```

**Success Criteria**:
- Cache hit ratio: >75% after 10 seconds
- P95 latency: <50ms
- P99 latency: <100ms

---

### Test 2: Sold-Out Fast Path

**Script**: `tests/k6/phase3_sold_out_test.js`

```javascript
export default function () {
  const soldOutEventId = 99; // Pre-create sold-out event
  const seats = [{ row: 'A', col: '1' }];
  
  const start = Date.now();
  const response = http.post(`${BASE_URL}/api/Bookings`, JSON.stringify({
    eventId: soldOutEventId,
    userId: `user_${__VU}_${__ITER}`,
    seats: seats,
  }), { headers: { 'Content-Type': 'application/json' } });
  const duration = Date.now() - start;
  
  check(response, {
    'status is 409': (r) => r.status === 409,
    'fast rejection (<50ms)': () => duration < 50,
  });
  
  conflictLatency.add(duration);
}

// Scenario: 5,000 VUs, 20 seconds
// Expected: P95 conflict latency <50ms, P99 <100ms
```

**Success Criteria**:
- P95 conflict latency: <50ms
- P99 conflict latency: <100ms
- No database queries (verify via Prometheus `pg_stat_user_tables` metrics)

---

### Test 3: Mixed Workload (Regression Test)

**Script**: `tests/k6/phase3_mixed_load.js`

```javascript
export default function () {
  const rand = Math.random();
  
  if (rand < 0.7) {
    // 70% reads (seat maps)
    http.get(`${BASE_URL}/api/Events/${randomEventId()}/seats`);
  } else {
    // 30% writes (bookings)
    http.post(`${BASE_URL}/api/Bookings`, JSON.stringify({
      eventId: activeEventId,
      userId: `user_${__VU}_${__ITER}`,
      seats: randomSeats(2),
    }), { headers: { 'Content-Type': 'application/json' } });
  }
}

// Scenario: 3,000 VUs, 30 seconds, 100 seats available
// Compare: Phase 2 baseline vs. Phase 3 with caching
```

**Success Criteria**:
- Overall P95 latency: <500ms (vs. 13-15s in Phase 2)
- Booking success latency: <500ms
- Booking conflict latency: <100ms
- Database read queries: <20% of Phase 2 baseline

---

## Monitoring & Observability

### New Grafana Dashboard Panels

1. **Cache Performance**
   ```promql
   # Hit Ratio
   sum(rate(seatgrid_cache_operations_total{operation="hit"}[5m])) 
   / sum(rate(seatgrid_cache_operations_total[5m])) * 100
   ```

2. **Bloom Filter Effectiveness**
   ```promql
   # Fast Rejections Per Second
   rate(seatgrid_sold_out_checks_total{result="fast_reject"}[5m])
   ```

3. **Latency Distribution by Outcome**
   ```promql
   # P95 latency by response code
   histogram_quantile(0.95, 
     sum(rate(http_server_request_duration_seconds_bucket[5m])) 
     by (le, code))
   ```

4. **Database Query Load Reduction**
   ```promql
   # Queries per second (before/after comparison)
   rate(pg_stat_user_tables_seq_tup_read[5m])
   ```

### Alerts (Optional)

```yaml
- alert: CacheHitRatioLow
  expr: |
    (sum(rate(seatgrid_cache_operations_total{operation="hit"}[10m])) 
    / sum(rate(seatgrid_cache_operations_total[10m]))) < 0.5
  for: 5m
  annotations:
    summary: "Cache hit ratio below 50% for 5 minutes"
```

---

## Rollout Plan

### Week 1: Available Count Cache (Sold-Out Fast Path)

**Day 1**: 
- ✅ Implement `IAvailabilityCache` interface and Redis-based implementation
- ✅ Add unit tests with mocked Redis

**Day 2**:
- ✅ Integrate into `BookingsController` (fast-path check)
- ✅ Initialize cache on event creation in `EventService`
- ✅ Manual test: Create event, book all seats, verify instant rejection

**Day 3**:
- ✅ Add `BookingMetrics` instrumentation
- ✅ Update `Program.cs` to register metrics
- ✅ Verify metrics appear in Prometheus

**Day 4**:
- ✅ Create k6 test: `phase3_sold_out_test.js` (5,000 VUs on sold-out event)
- ✅ Run test and collect metrics
- ✅ **Go/No-Go Decision Point**: P95 <50ms? Cache hit 100%?

**Day 5**: 
- ✅ Buffer day for fixes or documentation

**Go/No-Go Criteria**:
- Sold-out rejection latency: <50ms P95
- Zero database queries for sold-out events
- No errors in application logs

---

### Week 2: Booked Seats Cache (Active Competition Optimization)

**Day 6-7**:
- ✅ Implement `IBookedSeatsCache` with Redis SET operations
- ✅ Add unit tests (mock Redis, verify logic)
- ✅ Integration test with real Redis

**Day 8**:
- ✅ Integrate into `BookingOptimisticService` (pre-check before GetSeatsAsync)
- ✅ Update cache after successful SaveChanges
- ✅ Add metrics for cache hit/miss on booked seats

**Day 9**:
- ✅ Create k6 test: `phase3_active_competition.js` (2,000 VUs, 100 seats)
- ✅ Run test: Compare DB query count vs. Phase 2 baseline
- ✅ **Expected**: 90-95% reduction in DB queries

**Day 10**:
- ✅ Mixed workload regression test (ensure no double-bookings)
- ✅ Update Grafana dashboard with cache panels
- ✅ Document results in `phase-3-results.md`

**Success Criteria for Phase 3 Completion**:
- P95 latency (conflict): <50ms (was 13-15s) = **260x improvement**
- Database queries reduced: >90%
- Booked seats cache hit ratio: >90%
- Zero double-bookings (correctness maintained)
- All metrics visible in Grafana

---

## Risk Assessment & Mitigation

### Technical Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **Cache invalidation bugs** | Medium | High | Comprehensive tests, short TTL (30s), manual verification |
| **Redis failure causes outage** | Low | High | Graceful fallback to DB, health check alerts |
| **Sold-out service memory leak** | Low | Medium | Use `HashSet` (bounded growth), monitor memory |
| **Index slows down writes** | Low | Low | Index is on read-heavy columns, minimal write impact |
| **Cache stampede on expiry** | Medium | Medium | Acceptable at current scale, address in Phase 4 |

### Operational Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| **Complex rollback** | Low | Medium | Feature flags (`EnableRedisCache`, `EnableBloomFilter`) |
| **Increased debugging complexity** | Medium | Low | Add `X-Cache-Status` headers, comprehensive logging |
| **Redis memory exhaustion** | Low | Medium | Configure LRU eviction, monitor memory usage |

### Fallback Strategy

```csharp
// Graceful degradation in SeatMapCacheService
try
{
    var cachedData = await _cache.GetStringAsync(key, ct);
    if (cachedData != null) return JsonSerializer.Deserialize<List<SeatResponse>>(cachedData);
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Cache read failed, falling back to database");
    // Fall through to database query
}

// Always have DB fallback
return await _eventService.GetEventSeatsAsync(eventId, ct);
```

---

## Expected Outcomes

### Quantitative Improvements

| Metric | Phase 2 Baseline | Phase 3 Target | Expected Actual | Improvement |
|--------|------------------|----------------|-----------------|-------------|
| **P95 Latency (Conflict - Sold Out)** | 13-15s | <50ms | 10-20ms | **650x faster** |
| **P95 Latency (Conflict - Booked Seat)** | 13-15s | <50ms | 20-40ms | **325x faster** |
| **P95 Latency (Success)** | 6-7s | <500ms | 200-400ms | **15-30x faster** |
| **DB Queries (Sold-Out Requests)** | 100% | 0% | 0% | **-100%** |
| **DB Queries (Conflict Requests)** | 100% | <5% | 2-5% | **-95%** |
| **Booked Seats Cache Hit Ratio** | N/A | >90% | 92-96% | N/A |
| **Available Count Cache Hit** | N/A | 100% | 100% | N/A |

### Qualitative Improvements

1. **User Experience**: 99% of unavailable seat requests resolve in <50ms (was 13-15 seconds)
2. **Database Load**: 95% reduction in queries during high-concurrency booking phases
3. **System Capacity**: Database resources freed up to handle 10-20x more actual successful bookings
4. **Architectural Foundation**: Best-effort cache layer pattern proven, ready for Phase 4 complexity
5. **Risk Profile**: Zero new correctness issues - optimistic locking remains the safety net

---

## Dependencies & Prerequisites

### Required Before Starting

- ✅ Phase 2 completed and validated
- ✅ Redis infrastructure deployed and healthy
- ✅ Observability stack operational
- ✅ Load testing scripts functional

### Required During Phase 3

- Docker Desktop running (for Redis)
- k6 installed for load testing
- Database with sample events and seats
- Grafana access for dashboard updates

---

## Success Criteria Summary

**Phase 3 is considered successful if:**

1. ✅ P95 latency reduced from 13-15s to <500ms (20x improvement)
2. ✅ Cache hit ratio consistently >75% after warm-up
3. ✅ Sold-out event requests respond in <100ms (P95)
4. ✅ Database read load reduced by 80%+
5. ✅ No double-booking bugs introduced
6. ✅ All load tests pass with new targets
7. ✅ Grafana dashboards updated with cache metrics
8. ✅ Documentation complete (`phase-3-results.md`)

---

## Post-Phase 3 Roadmap

### Phase 4: Asynchronous Processing

- Replace synchronous booking with queue-based processing (RabbitMQ/Kafka)
- Accept request → Return 202 Accepted → Process asynchronously
- Implement notification mechanism (WebSocket/SignalR or polling)

### Phase 5: Distributed Transactions

- Implement Saga pattern for Reservation → Payment → Confirmation
- Add compensating transactions for failures
- Use distributed locks (Redis) for critical sections

### Phase 6: Horizontal Scaling

- Database read replicas for GET operations
- Database sharding by EventId range
- Multi-region cache replication

---

## Appendices

### A. Configuration Reference

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=seatgrid;Username=postgres;Password=postgres",
    "Redis": "localhost:6379,abortConnect=false"
  },
  "Caching": {
    "SeatMapTtl": 30,              // Cache TTL in seconds
    "EnableRedisCache": true,       // Feature flag for caching
    "EnableBloomFilter": false      // Feature flag for sold-out service
  },
  "Booking": {
    "Strategy": "Optimistic"        // Naive | Pessimistic | Optimistic
  }
}
```

### B. Key Decision Log

| Decision | Rationale | Trade-offs |
|----------|-----------|------------|
| **HashSet vs. Bloom Filter** | No false positives, simpler debugging | Higher memory, but acceptable at scale |
| **30s Cache TTL** | Balance freshness vs. load reduction | Stale data risk (mitigated by invalidation) |
| **Cache-Aside Pattern** | Simple, proven, good for read-heavy | Cache stampede risk (acceptable) |
| **EF Core LINQ vs. FromSqlRaw** | Better indexing, parameterized queries | Slightly more verbose code |

---

**Phase 3 Status**: 🚀 **Ready to Implement**  
**Estimated Duration**: 2 weeks (10 working days)  
**Risk Level**: Low-Medium (foundation already in place)  
**Expected ROI**: Very High (20x latency improvement for minimal complexity)
