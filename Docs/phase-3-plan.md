# Phase 3 Implementation Plan: Read Optimization (Caching & Fast Rejections)

This document outlines the step-by-step plan to optimize the read path and implement fast-path rejection mechanisms. Based on Phase 2 results, we identified that 99.6% of users wait 1-2+ seconds only to be told "sold out". Phase 3 focuses on dramatically reducing this latency through intelligent caching.

---

## Phase 3 Goals

### Primary Objectives
1. **Reduce P95 latency from 2.33s to <200ms**
2. **Fast-path rejection for sold-out events (<50ms)**
3. **Optimize seat map reads to reduce DB load by 95%+**
4. **Maintain consistency guarantees (no double-booking)**

### Success Metrics

| Metric | Baseline (Phase 2) | Phase 3 Target |
|--------|-------------------|----------------|
| P95 Latency (Overall) | 2.33s | <200ms |
| Avg Latency (Success) | 629ms | <500ms |
| Avg Latency (Conflict) | ~1s | <50ms |
| DB Queries (sold-out check) | 1 per request | 0 (cached) |
| Seat Map Read Latency | ~100-200ms | <20ms |

---

## Implementation Steps

### 1. Infrastructure - Add Redis

**Goal**: Deploy Redis as a distributed cache and session store.

- [x] **Update `docker-compose.infra.yml`**:
    - Add Redis service (latest Alpine image)
    - Port: 6379
    - Configuration: No persistence needed initially (cache-only mode)
    - Health check: `redis-cli ping`
    
- [x] **Add Redis NuGet Packages** (to `SeatGrid.API`):
    - `Microsoft.Extensions.Caching.StackExchangeRedis`
    - `StackExchange.Redis`
    
- [x] **Configure Redis in `Program.cs`**:
    - Register `IDistributedCache` with Redis connection
    - Add OpenTelemetry instrumentation for Redis (if available)
    - Configure connection resilience (retry policies)

**Verification**: 
- [x] Redis container runs and accepts connections
- [x] Health endpoint shows Redis connectivity
- [ ] Grafana can scrape Redis metrics

---

### 2. Cache-Aside Pattern for Seat Maps

**Goal**: Serve `GET /events/{id}/seats` from cache instead of database.

#### A. Read Path Optimization

- [ ] **Implement Cache Service** (`Services/SeatMapCacheService.cs`):
    - Method: `GetSeatMapAsync(long eventId)`
    - **Logic**:
        1. Try read from Redis: `cache.GetStringAsync($"event:{eventId}:seats")`
        2. If HIT → Deserialize and return (track cache hit metric)
        3. If MISS → Query DB → Serialize → Store in Redis (TTL: 30s) → Return
    - **Cache Key Pattern**: `event:{eventId}:seats`
    - **TTL Strategy**: 30 seconds (balance freshness vs. load reduction)
    - **Serialization**: JSON (simple) or MessagePack (faster)

- [ ] **Update `EventsController.GetSeats`**:
    - Inject `SeatMapCacheService`
    - Replace direct DB query with cache service call
    - Add `X-Cache-Status` header (HIT/MISS) for debugging

- [ ] **Cache Invalidation Strategy**:
    - Invalidate on booking: `cache.RemoveAsync($"event:{eventId}:seats")`
    - Publish invalidation event (Phase 4 prep: pub/sub pattern)
    - Consider: Invalidate vs. Update-in-place trade-offs

**Expected Impact**:
- Cache hit ratio: 80%+ after warm-up
- Seat map read latency: <20ms (from Redis)
- DB read queries reduced by 80%+

#### B. Cache Warming

- [ ] **Add Background Service** (`Services/CacheWarmingService.cs`):
    - Runs on startup and every 5 minutes
    - Pre-loads seat maps for "hot" events (events in next 24 hours)
    - Uses `IHostedService` pattern
    - Logs warming statistics

---

### 3. Bloom Filter for Sold-Out Detection

**Goal**: Instantly reject booking requests for fully-booked events without hitting DB or Redis.

#### A. Implementation

- [ ] **Add NuGet Package**: 
    - `BloomFilter.NetCore` or implement custom Bloom Filter

- [ ] **Create Service** (`Services/AvailabilityBloomFilter.cs`):
    - In-memory Bloom Filter (shared singleton)
    - **Methods**:
        - `MarkAsSoldOut(long eventId)`: Add to filter
        - `IsPossiblySoldOut(long eventId)`: Check membership
    - **False Positive Rate**: 1% (acceptable trade-off)
    - **Capacity**: 10,000 events

- [ ] **Integration Points**:
    - **On Booking Success**: After all seats booked → Check remaining seats → If 0, call `MarkAsSoldOut()`
    - **On Booking Request**: Before DB query → Check `IsPossiblySoldOut()` → If true, return 409 immediately
    - **Startup**: Query DB for fully-booked events → Pre-populate filter

- [ ] **Monitoring**:
    - Metric: `bloom_filter_hits` (fast rejections)
    - Metric: `bloom_filter_false_positives` (query DB, found seats)
    - Grafana panel: Bloom filter effectiveness

**Expected Impact**:
- Sold-out event requests: <5ms response time
- DB/Redis queries avoided: ~30-50% of total traffic (during late-stage sales)
- False positive rate: ~1% (these users do 1 extra DB query)

---

### 4. Optimize Booking Conflict Detection

**Goal**: Reduce latency for the 99.6% who will get 409 Conflict.

#### A. Early DB Query Optimization

- [ ] **Add Index** (Migration):
    ```sql
    CREATE INDEX idx_seats_event_status 
    ON Seats(EventId, Status)
    INCLUDE (Row, Col);
    ```
    - Purpose: Fast availability checks with covering index
    - Impact: Reduce query time from ~50ms to <10ms

- [ ] **Optimize Query in `BookingsController`**:
    - Current: Fetch candidate seats → Filter in memory
    - **New Approach**:
        1. Quick count: `SELECT COUNT(*) FROM Seats WHERE EventId = X AND Status = Available`
        2. If count < requested → Early return 409 (skip the heavy query)
        3. Else: Proceed with current logic
    - **Alternative**: Use Redis sorted set to track available seat count per event

#### B. Stale Cache Strategy (Eventual Consistency)

- [ ] **Configuration** (`appsettings.json`):
    ```json
    "Caching": {
      "SeatMapTtl": 30,
      "AllowStaleReads": true,
      "StaleGracePeriod": 60
    }
    ```

- [ ] **Logic**:
    - If cache entry expired but still present → Return stale data with `Warning` header
    - Background refresh cache asynchronously
    - Tradeoff: User might see outdated seat map, but booking endpoint is source of truth

**Expected Impact**:
- Conflict detection: 100-200ms → <50ms
- Reduced lock contention (fewer concurrent transactions)

---

### 5. Read Replica (Optional Stretch Goal)

**Goal**: Offload read traffic to a PostgreSQL replica.

- [ ] **Setup Read Replica** (Docker):
    - Add `postgres-replica` service in `docker-compose.infra.yml`
    - Configure streaming replication from primary
    - Read-only connection string

- [ ] **Connection String Strategy**:
    ```json
    "ConnectionStrings": {
      "Primary": "Host=postgres;...",
      "ReadReplica": "Host=postgres-replica;..."
    }
    ```

- [ ] **Update `SeatGridDbContext`**:
    - Add `SeatGridReadOnlyDbContext` (uses replica connection)
    - Inject in `EventsController` for GET operations
    - Primary context only for writes

**Expected Impact**:
- Primary DB CPU: -50% (all reads diverted)
- Replication lag: <100ms (acceptable for seat maps)

---

### 6. Testing & Validation

#### A. Update Load Tests

- [ ] **Create `tests/k6/phase3_cache_test.js`**:
    - **Scenario 1: Cache Hit Ratio Test**
        - 500 VUs
        - All repeatedly fetch same event's seat map
        - Expected: 99% cache hits after warm-up
        
    - **Scenario 2: Sold-Out Fast Path**
        - 5,000 VUs (harder than Phase 2)
        - 100 seats available
        - Measure: Conflict response time distribution
        - Expected: P95 <100ms, P99 <200ms
        
    - **Scenario 3: Mixed Workload**
        - 50% reads (seat maps)
        - 50% writes (bookings)
        - 3,000 VUs
        - Compare against Phase 2 baseline

- [ ] **Cache Invalidation Test**:
    - Manual test: Book seat → Verify cache invalidated → Fetch seat map → Verify freshness

#### B. Observability Enhancements

- [ ] **Add Custom Metrics** (`Program.cs`):
    - `seatgrid_cache_hits_total` (counter, labels: hit/miss)
    - `seatgrid_bloom_filter_checks_total` (counter, labels: hit/miss)
    - `seatgrid_available_seats_gauge` (gauge, label: eventId)

- [ ] **Grafana Dashboard Updates**:
    - Panel: Cache hit ratio (target: 80%+)
    - Panel: Bloom filter effectiveness
    - Panel: Latency comparison (P50/P95/P99) vs. Phase 2
    - Panel: Redis memory usage

- [ ] **Distributed Tracing**:
    - Verify traces show: HTTP → Cache Check → DB (on miss) → Response
    - Identify: Cache hit traces should skip DB span

---

### 7. Documentation & Rollback Plan

- [ ] **Update `appsettings.json`**:
    - Add caching configuration section
    - Feature flags: `EnableRedisCache`, `EnableBloomFilter`

- [ ] **Create Migration Guide**:
    - Document cache key patterns
    - TTL rationale and tuning guidance
    - Bloom filter capacity planning

- [ ] **Rollback Strategy**:
    - Feature flag to disable Redis (fall back to DB)
    - If Redis fails → Log error → Serve from DB (graceful degradation)
    - Health check: Mark service degraded (not failed) if Redis is down

---

## Execution Order

1. **Infrastructure** (Day 1):
   - Add Redis to docker-compose
   - Install NuGet packages
   - Verify connectivity

2. **Cache Service** (Day 2):
   - Implement `SeatMapCacheService`
   - Update `EventsController`
   - Test cache hit/miss manually

3. **Bloom Filter** (Day 3):
   - Implement and integrate
   - Test false positive rate
   - Monitor metrics

4. **Optimization** (Day 4):
   - Add database index
   - Optimize booking query
   - Cache warming service

5. **Testing** (Day 5):
   - Run all k6 tests
   - Compare metrics vs. Phase 2
   - Document results in `phase-3-results.md`

---

## Risk Assessment

### Technical Risks

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Cache invalidation bugs (stale data) | Medium | High | Comprehensive tests, short TTL initially |
| Redis connection failures | Low | Medium | Graceful fallback to DB |
| Bloom filter false positives | Low | Low | 1% FP rate acceptable, monitor metric |
| Over-caching (memory pressure) | Low | Medium | Set TTL, monitor Redis memory |

### Performance Risks

- **Cache Stampede**: If cache expires during high load, many requests hit DB simultaneously
  - **Mitigation**: Use "lock on miss" pattern or probabilistic early expiration
  
- **Cache Coherence**: Multiple API instances invalidating different caches
  - **Mitigation**: Redis Pub/Sub for cache invalidation (Phase 4 prep)

---

## Expected Outcomes

### Quantitative
- **Latency**: P95 reduced from 2.33s to <200ms (11x improvement)
- **Throughput**: DB can handle 3-5x more booking requests (reads offloaded)
- **User Experience**: 95% of "sold out" responses in <100ms

### Qualitative
- Cache monitoring provides visibility into read patterns
- System can now handle 5,000+ concurrent users (next load test target)
- Foundation laid for Phase 4 (async writes via queues)

---

## Dependencies for Phase 4

Phase 3 prepares the ground for async processing:
- Redis infrastructure can be reused for distributed locks
- Cache invalidation via Pub/Sub is stepping stone to event-driven architecture
- Reduced read load frees DB capacity for write-heavy queue consumers

**Phase 3 Status**: 🚧 **Ready to Implement** - All prerequisites met, design validated against Phase 2 findings.
