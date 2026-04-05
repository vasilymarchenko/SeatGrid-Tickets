# SeatGrid - High-Load Ticketing System

**SeatGrid** is a learning project designed to simulate a high-concurrency ticketing platform (similar to Ticketmaster). The goal is to build a system capable of handling "flash sales" where thousands of users compete for limited inventory simultaneously, focusing on distributed systems challenges like concurrency, consistency, and high availability.

## Project Overview

*   **Core Challenge**: Prevent double-booking while handling traffic spikes (e.g., 100k RPS).
*   **Architecture**: Evolves from a naive monolith to a distributed microservices architecture.
*   **Tech Stack**: .NET 8/9, PostgreSQL, Redis, RabbitMQ/Kafka, Kubernetes, OpenTelemetry.

## Getting Started

### Prerequisites
*   Docker Desktop
*   .NET 9 SDK
*   k6 (for load testing)

### 1. Infrastructure
Start the infrastructure (Postgres + Observability Stack):
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

**Configuration**:
The application uses a Strategy pattern for booking implementations. Configure via environment variable:
*   `Booking__Strategy=Pessimistic` (default) - Uses PostgreSQL row-level locking (FOR UPDATE) to ensure consistency in background processing.
*   `Booking__Strategy=Naive` - Basic transaction isolation without explicit locking (baseline implementation)

To switch strategies, modify the environment variable in `docker-compose.app.yml`.

### 3. Database Migrations
The application is configured to apply migrations automatically on startup.
To apply them manually:
```bash
dotnet ef database update -p src/SeatGrid.API/SeatGrid.API.csproj
```

### 4. Run the Application (Local)
```bash
dotnet run --project src/SeatGrid.API/SeatGrid.API.csproj
```
The API will be available at `http://localhost:5000`.

### 5. Testing
**Functional Testing**:
Use the `requests.http` file in VS Code (requires REST Client extension) to create events and book seats.

**Load Testing (k6)**:
Run the baseline load test:
```bash
k6 run tests/k6/baseline_test.js
```

## Documentation

*   [Project Requirements](Docs/project-requirements.md)
*   [Learning Plan](Docs/learning-project-plan.md)
*   **[Phase 2 Results](Docs/phase-2-results.md)** ✅ - Baseline performance established: 2.33s P95 latency under 2,000 concurrent users. System survived without crashes, bottlenecks identified.
*   **[Phase 3 Results](Docs/phase-3-results.md)** ✅ - Cache optimization complete: 565ms P95 latency (24x improvement), 4,130 RPS throughput (20x increase), 0% error rate. Two-layer cache architecture (available count + booked seats) eliminated 99.9% of database queries.
*   **[Phase 3.1 Results](Docs/phase-3.1-results.md)** ✅ - Reworked cache approach. Lua script and cach-first approach eliminated concurrency issues and increased throughput up to 5.500 RPS.
*   **[Phase 4 Results](Docs/phase-4-results.md)** ✅ - Distributed Transactions (Saga Pattern). Implemented async reservation flow with RabbitMQ and MassTransit. Switched to Pessimistic Locking for the finalizer to guarantee consistency after payment. Achieved 100% seat utilization with self-healing compensation logic.

**[Architecture overview (current state)](Docs/architecture-and-flow.md)**

## Project stages
[tag Phase-2.1](https://github.com/vasilymarchenko/SeatGrid-Tickets/tree/Phase-2.1)
[tag Phase-3](https://github.com/vasilymarchenko/SeatGrid-Tickets/tree/Phase-3)
[tag Phase-3.1](https://github.com/vasilymarchenko/SeatGrid-Tickets/tree/Phase-3.1)
[tag Phase-4](https://github.com/vasilymarchenko/SeatGrid-Tickets/tree/Phase-4)
