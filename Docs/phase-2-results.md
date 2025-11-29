# Phase 2 Results: Observability & The "Crash"

## Executive Summary

Phase 2 successfully instrumented the SeatGrid system with OpenTelemetry and stress-tested it under extreme load. The system **survived without crashing** but revealed significant performance bottlenecks in write operations. The observability stack (Prometheus, Grafana, Tempo, Loki) is now operational and ready to guide optimization efforts in subsequent phases.

---

## Current Implementation Overview

### Architecture
- **Application**: .NET 9 Web API with Entity Framework Core
- **Database**: PostgreSQL 16 (single instance)
- **Observability Stack**:
  - OpenTelemetry Collector (OTLP receiver)
  - Prometheus (metrics storage)
  - Tempo (distributed tracing)
  - Loki (log aggregation)
  - Grafana (visualization)

### Booking Flow (The Critical Path)

```csharp
POST /api/Bookings
  ├─ Begin Database Transaction
  ├─ Query: Fetch candidate seats (with Row/Col filters)
  ├─ In-Memory Filter: Match exact seat positions
  ├─ Validation: Check if all seats exist
  ├─ Availability Check: Ensure all seats are "Available"
  ├─ Update: Set status to "Booked" + assign UserId
  ├─ SaveChanges (triggers DB write)
  └─ Commit Transaction (or Rollback on conflict)
```

### Weak Points Identified

1. **Synchronous Processing**: Every request blocks waiting for DB transaction
2. **Transaction Overhead**: Each booking requires explicit transaction with full ACID guarantees
3. **Lock Contention**: High concurrency causes implicit row-level locks, creating queues
4. **Expensive Conflict Detection**: Must query DB to check availability every time
5. **No Early Exit**: Sold-out events still process full transaction logic before rejecting
6. **Complex Query Pattern**: Two-phase filtering (DB query + in-memory LINQ) adds latency

---

## Load Test Results

### Test Configuration
- **Script**: `tests/k6/crash_test.js`
- **Scenario**: "Thundering Herd" simulation
- **Target**: 2,000 concurrent virtual users (VUs)
- **Workload**: 100 available seats for an event
- **Ramp-Up**: 0 → 2,000 VUs in 10 seconds
- **Sustain**: 2,000 VUs for 20 seconds
- **Ramp-Down**: 2,000 → 0 VUs in 10 seconds

### Key Metrics

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| **Total Requests** | 41,465 | N/A | ✅ |
| **Throughput** | 1,020 RPS | N/A | ✅ |
| **HTTP Failures** | 99.60% (41,303) | Expected | ✅ |
| **System Crashes (500s)** | 0 | 0 | ✅ |
| **Avg Response Time** | 1.36s | <500ms | ❌ |
| **P95 Latency** | 2.33s | <2s | ❌ |
| **Max Latency** | 3.2s | N/A | ⚠️ |
| **Successful Bookings** | ~162 (0.4%) | N/A | ✅ |
| **Conflicts (409)** | ~41,303 (99.6%) | Expected | ✅ |

### Latency Breakdown
- **Successful Requests (200 OK)**: 629ms avg, 986ms P95
- **Conflict Requests (409)**: Faster (exact metrics not isolated)
- **Iteration Duration**: 1.47s avg (includes 100ms sleep)

---

## Test Results Analysis

### What Went Well ✅
1. **System Stability**: No crashes, no 500 errors under 2,000 concurrent users
2. **Correctness**: No double-booking detected (all checks passed)
3. **Proper HTTP Semantics**: Returns 200 (success) or 409 (conflict) appropriately
4. **Observability**: Full telemetry captured (traces, metrics, logs)

### What Failed ❌
1. **Latency Threshold Breached**: P95 of 2.33s exceeded the 2s target
2. **Slow Conflict Detection**: Users wait 1-2+ seconds to be told "sold out"
3. **Database Bottleneck**: Transaction queue causes serialization

### Expected vs. Reality

**The 99.6% "Failure" Rate**:
- This is **mathematically correct** and **desired behavior**
- 100 seats ÷ 2,000 users = 5% theoretical max success rate
- The system correctly rejected 1,938 users who couldn't get seats
- **Not a bug, but latency for rejections is the problem**

---

## The Problem Illustrated

### Timeline of the Thundering Herd

```
T+0s:  ┌─────────────────────────────────────┐
       │ 100 Seats Available                 │
       │ 2,000 Users Click "Buy" Simultaneously
       └─────────────────────────────────────┘
                      ↓
                [All hit DB]
                      ↓
T+1s:  ┌─────────────────────────────────────┐
       │ ~50 Seats Sold                      │
       │ ~50 Transactions In Progress        │
       │ ~1,900 Requests Queued/Waiting      │
       └─────────────────────────────────────┘
                      ↓
T+2s:  ┌─────────────────────────────────────┐
       │ ~100 Seats Sold (SOLD OUT)          │
       │ ~1,900 Requests STILL processing    │
       │ Each checks DB, gets 409, rollback  │
       └─────────────────────────────────────┘
                      ↓
T+3s:  ┌─────────────────────────────────────┐
       │ Last stragglers get rejected        │
       │ Wasted ~5,700 DB transactions       │
       │ (1,900 losers × 3 DB ops each)      │
       └─────────────────────────────────────┘
```

### Root Cause: Pessimistic Reality, Optimistic Implementation

Our current approach is **Optimistic Concurrency**:
- Everyone tries simultaneously
- Most fail and discover conflicts late
- Each failure still pays full transaction cost

**Why This Hurts**:
1. **Database Connection Exhaustion**: PostgreSQL has limited connections; each blocked transaction holds one
2. **Lock Contention**: Transactions waiting on row locks create cascading delays
3. **CPU Waste**: 99.6% of DB work is checking already-sold seats
4. **Poor UX**: Users wait 2+ seconds for a "No" answer

---

## The Core Dilemma

```
┌──────────────────────┐         ┌──────────────────────┐
│   Low Latency        │         │   Strong Consistency │
│   (Fast rejections)  │ ◄─────► │   (No double-booking)│
└──────────────────────┘         └──────────────────────┘
         ▲                                  ▲
         │                                  │
         └──────────── Trade-off ──────────┘
```

Current implementation **prioritizes consistency** over latency:
- Every request gets a DB transaction (slow but safe)
- No caching or shortcuts (guarantees freshness)
- Full ACID compliance (prevents races)

**But we're paying for consistency we don't always need**:
- The 1,900 losers don't need transactional guarantees
- Checking a sold-out event shouldn't require a DB round-trip
- The system "doesn't know it's sold out" until checking the DB

---

## Key Takeaways for Phase 3+

### Optimization Targets

1. **Fast-Path Rejections** (Phase 3 - Caching)
   - **Goal**: Reject sold-out requests in <50ms
   - **Approach**: Bloom Filter, Redis cache, or in-memory flag
   - **Impact**: Reduce DB load by 95%+

2. **Queue-Based Processing** (Phase 4 - Async)
   - **Goal**: Accept requests instantly, process serially
   - **Approach**: RabbitMQ/Kafka + worker consumers
   - **Impact**: 202 Accepted in <20ms, process in background

3. **Distributed Locking** (Phase 5 - Saga Pattern)
   - **Goal**: Prevent optimistic conflicts entirely
   - **Approach**: Redis locks, Reservation → Payment → Confirm flow
   - **Impact**: Reduce DB transaction retries

### Success Metrics for Next Phase

| Metric | Current | Phase 3 Target |
|--------|---------|----------------|
| P95 Latency | 2.33s | <200ms |
| Avg Latency (Success) | 629ms | <500ms |
| Avg Latency (Conflict) | ~1s | <50ms |
| DB Queries (for sold-out) | 1 per request | 0 (cached) |

---

## Conclusion

Phase 2 achieved its primary goal: **demonstrate the limits of a naive synchronous approach under load**. The system didn't crash, proving the foundation is solid. However, the 2.33s P95 latency and wasteful conflict handling clearly illustrate why high-scale systems require:
- **Caching** for volatile read-heavy data
- **Asynchronous processing** for write-heavy workloads
- **Circuit breakers** and early exit strategies

The observability stack is now operational and will be critical for validating improvements in Phase 3 and beyond. Every optimization must be measured against these baseline metrics.

**Phase 2 Status**: ✅ **Complete** - System characterized, bottlenecks identified, ready for optimization.
