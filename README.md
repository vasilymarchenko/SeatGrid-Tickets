# SeatGrid - High-Load Ticketing System

**SeatGrid** is a learning project designed to simulate a high-concurrency ticketing platform (similar to Ticketmaster). The goal is to build a system capable of handling "flash sales" where thousands of users compete for limited inventory simultaneously, focusing on distributed systems challenges like concurrency, consistency, and high availability.

## Project Overview

*   **Core Challenge**: Prevent double-booking while handling traffic spikes (e.g., 100k RPS).
*   **Architecture**: Evolves from a naive monolith to a distributed microservices architecture.
*   **Tech Stack**: .NET 8/9, PostgreSQL, Redis, RabbitMQ/Kafka, Kubernetes, OpenTelemetry.

## Roadmap

This project follows a phased implementation plan:

1.  **Phase 1: The Naive Monolith** - A baseline implementation to establish functionality.
2.  **Phase 2: Observability & The "Crash"** - Stress testing with k6 to identify bottlenecks.
3.  **Phase 3: Read Optimization** - Implementing caching (Redis) and efficient data formats.
4.  **Phase 4: Write Optimization** - Handling the "Thundering Herd" with message queues.
5.  **Phase 5: Distributed Transactions** - Implementing Sagas for data consistency.
6.  **Phase 6: Sharding & HA** - Database scaling strategies.
7.  **Phase 7: Analytics** - Big data ingestion with ClickHouse.

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
To run the application along with the infrastructure:
```bash
docker compose -f docker-compose.infra.yml -f docker-compose.app.yml up -d --build
```
The API will be available at `http://localhost:5000`.

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
