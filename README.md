# SeatGrid - High-Load Ticketing System

**SeatGrid** is a learning project designed to simulate a high-concurrency ticketing platform (similar to Ticketmaster). The goal is to build a system capable of handling "flash sales" where thousands of users compete for limited inventory simultaneously, focusing on distributed systems challenges like concurrency, consistency, and high availability.

## Project Overview

*   **Core Challenge**: Prevent double-booking while handling traffic spikes (e.g., 100k RPS).
*   **Architecture**: Evolves from a naive monolith to a distributed microservices architecture with a DDD domain model.
*   **Tech Stack**: .NET 9, PostgreSQL, Redis, RabbitMQ, MassTransit, MediatR, Kubernetes, OpenTelemetry.

## Roadmap

This project follows a phased implementation plan:

1.  **Phase 1: The Naive Monolith** - A baseline implementation to establish functionality.
2.  **Phase 2: Observability & The "Crash"** - Stress testing with k6 to identify bottlenecks.
3.  **Phase 3: Read Optimization** - Implementing caching (Redis) and efficient data formats.
4.  **Phase 4: Write Optimization** - Handling the "Thundering Herd" with message queues.
5.  **Phase 5: Domain-Driven Design** - Booking aggregate, domain events, repository pattern.
6.  **Phase 6: Sharding & HA** - Database scaling strategies (Outbox pattern, sharding).
7.  **Phase 7: Analytics** - Big data ingestion with ClickHouse.

## Getting Started

### Prerequisites
*   Docker Desktop
*   .NET 9 SDK
*   k6 (for load testing)

### 1. Infrastructure
Start the infrastructure (Postgres, Redis, RabbitMQ + Observability Stack):
```bash
docker compose -f docker-compose.infra.yml up -d
```

**Observability Services**:
*   **Grafana** (Dashboards): [http://localhost:3000](http://localhost:3000)
*   **Prometheus** (Metrics): [http://localhost:9090](http://localhost:9090)
*   **Tempo** (Traces): Visualized in Grafana
*   **Loki** (Logs): Visualized in Grafana

### 2. Run the Application (Docker)
To run the application (API + Payment Service) along with the infrastructure:
```bash
docker compose -f docker-compose.infra.yml -f docker-compose.app.yml up -d --build
```
The API will be available at `http://localhost:5000`.

### 3. Database Migrations
Migrations are applied **automatically on startup** via `db.Database.Migrate()` in `Program.cs`. No manual step is needed when running via Docker or `dotnet run`.

To apply or review migrations manually:
```bash
# Apply pending migrations
dotnet ef database update --project src/SeatGrid.API/SeatGrid.API.csproj

# Generate SQL script to review before applying (e.g. in staging/prod)
dotnet ef migrations script --project src/SeatGrid.API/SeatGrid.API.csproj
```

Current migrations in order:
| Migration | What it does |
|-----------|-------------|
| `InitialCreate` | `Events`, `Seats` tables |
| `DDD_Refactor` | `Bookings`, `BookedSeats` tables; `BookingStatus` column; `BookingId` as UUID PK |
| `DDD_AddOrderIdToBooking` | `OrderId` UUID column + unique index on `Bookings` (saga correlation key) |

### 4. Run the Application (Local)
```bash
dotnet run --project src/SeatGrid.API/SeatGrid.API.csproj
```
The API will be available at `http://localhost:5000`.

### 5. Testing
**Unit Tests**:
```bash
dotnet test tests/SeatGrid.Domain.Tests
```
Covers `Booking` aggregate invariants and `SeatLocation` value object — no database or DI container required.

**Functional Testing**:
Use the `requests.http` file in VS Code (requires REST Client extension) to create events and book seats.

**Load Testing (k6)**:
```bash
k6 run tests/k6/baseline_test.js
k6 run tests/k6/async_saga_test.js
```

## Documentation

*   [Project Requirements](Docs/project-requirements.md)
*   [Learning Plan](Docs/learning-project-plan.md)
*   **[Phase 2 Results](Docs/phase-2-results.md)** ✅ - Baseline performance established: 2.33s P95 latency under 2,000 concurrent users. System survived without crashes, bottlenecks identified.
*   **[Phase 3 Results](Docs/phase-3-results.md)** ✅ - Cache optimization complete: 565ms P95 latency (24x improvement), 4,130 RPS throughput (20x increase), 0% error rate. Two-layer cache architecture (available count + booked seats) eliminated 99.9% of database queries.
*   **[Phase 3.1 Results](Docs/phase-3.1-results.md)** ✅ - Reworked cache approach. Lua script and cache-first approach eliminated concurrency issues and increased throughput up to 5,500 RPS.
*   **[Phase 4 Results](Docs/phase-4-results.md)** ✅ - Distributed Transactions (Saga Pattern). Implemented async reservation flow with RabbitMQ and MassTransit. Switched to Pessimistic Locking for the finalizer to guarantee consistency after payment. Achieved 100% seat utilization with self-healing compensation logic.
*   **[Phase 5 — DDD Plan](Docs/stage5-ddd-plan.md)** ✅ - Domain-Driven Design refactor. `Booking` promoted to aggregate root with value objects (`BookingId`, `BookingStatus`, `SeatLocation`), domain events (`BookingConfirmed`, `BookingCancelled`), and repository pattern.
*   **[Phase 5 — Integration](Docs/stage5-phase5-integration.md)** ✅ - Wired aggregate into the live flow. Consumer creates and confirms `Booking` aggregate on `PaymentSucceeded`. Domain events dispatched via MediatR drive Redis and `Seats` table updates. Controller hot path unchanged (Redis + Publish only, no DB write).

## Architecture Notes (Phase 5)

The booking flow after the DDD refactor:

```
POST /api/Bookings
  → Redis SET NX (gatekeeper, rejects conflicts instantly)
  → Publish BookingInitiated to RabbitMQ
  → 202 Accepted                          ← no DB write on the hot path

BookingFinalizerConsumer (async, off hot path):
  PaymentSucceeded →
    Booking.Create() + AddSeat() + Confirm()  ← aggregate born Confirmed
    SaveChangesAsync()                         ← single DB write
      └─ BookingConfirmedHandler     → Redis lock made permanent
      └─ SeatStatusConfirmedHandler  → Seat.Status = Booked in Seats table

  PaymentFailed →
    ReleaseSeatsAsync()               ← Redis lock released, no DB write
```

The `Seats` table (event availability grid) and Redis remain the source of truth for `GET /api/events/{id}/seats`. The `Bookings` table is the audit record for confirmed orders.

## Project stages
[tag Phase-2.1](https://github.com/vasilymarchenko/SeatGrid-Tickets/tree/Phase-2.1)
[tag Phase-3](https://github.com/vasilymarchenko/SeatGrid-Tickets/tree/Phase-3)
[tag Phase-3.1](https://github.com/vasilymarchenko/SeatGrid-Tickets/tree/Phase-3.1)
[tag Phase-4](https://github.com/vasilymarchenko/SeatGrid-Tickets/tree/Phase-4)
