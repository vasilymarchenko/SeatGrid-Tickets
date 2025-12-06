# Learning Project Plan: "SeatGrid" - High-Load Ticketing System

## 1. Project Overview
**Concept**: A simplified high-concurrency ticketing platform (like Ticketmaster) where users compete for limited seats at popular events.
**Why this project?** It forces you to solve the core problems of distributed systems:
*   **Concurrency**: Preventing double-booking (Consistency).
*   **High Load**: Handling traffic spikes when sales open (Scalability).
*   **Data Volume**: Storing millions of orders and analytics events (Big Data).

**The "Time-Saver" Approach**:
*   **No Frontend**: Use Swagger, CLI tools, or Load Test scripts (k6) as clients.
*   **Simplified Domain**: A "Venue" is just a matrix of Row/Col. No complex maps.
*   **Mocked Payments**: A simple service that randomly succeeds/fails after a delay.
*   **No Auth Complexity**: Pass `UserId` in headers/payloads. Trust the client for this learning exercise.

---

## 2. Mapping to Course Modules
| Course Topic | Project Feature |
| :--- | :--- |
| **Requirements & Capacity** | Designing the SeatGrid capacity (1M users, 100k RPS). |
| **Monitoring & Metrics** | OTEL, Prometheus, Grafana setup. |
| **Scaling (Vertical/Horizontal)** | K8s HPA, Load Balancing. |
| **Data Access & Formats** | REST vs gRPC benchmarks. |
| **Caching** | Redis for Seat Maps. Bloom Filters for sold-out events. |
| **Queues & Streaming** | Kafka/RabbitMQ for booking requests. |
| **Distributed Transactions** | Saga pattern for Reservation -> Payment -> Confirmation. |
| **Sharding & Replication** | Partitioning the Orders database. |
| **Consistency & Consensus** | Optimistic Locking (ETags) vs Distributed Locks. |
| **Big Data & Analytics** | ClickHouse for analyzing seat view heatmaps. |

---

## 3. Implementation Plan

### Phase 1: The Naive Monolith (Baseline)
**Goal**: Build a functional but non-scalable system to establish a baseline.
*   **Stack**: .NET 8 Web API, PostgreSQL (Single Instance).
*   **Features**:
    *   `POST /events`: Create an event with $R \times C$ seats.
    *   `GET /events/{id}/seats`: Return all seats and their status (Available/Booked).
    *   `POST /book`: Transactional booking (Direct DB write).
*   **The "Bad" Design**:
    *   Synchronous processing.
    *   Select `FOR UPDATE` or simple transactions.
    *   Return huge JSON payloads for seat maps.

### Phase 2: Observability & The "Crash"
**Goal**: See the system fail under load.
*   **Action**:
    *   Instrument with **OpenTelemetry** (Traces, Metrics, Logs).
    *   Deploy **Prometheus**, **Grafana**, **Jaeger** (or Aspire Dashboard) in K8s.
    *   Create a **k6** script: Simulate 10,000 users trying to buy 100 seats simultaneously.
*   **Observation**:
    *   Watch DB CPU spike to 100%.
    *   Observe HTTP 503s and timeouts.
    *   Measure P99 Latency.

> [!NOTE]
> Actual results: [Phase-2](phase-2-results.md)
> Also, 3 approaches were compared: naive one, pessimistic and optimistic locking.
[tag Phase-2.1](https://github.com/vasilymarchenko/SeatGrid-Tickets/tree/Phase-2.1)

### Phase 3: Read Optimization (Caching & Formats)
**Goal**: Fix the "Read" bottleneck (viewing seat maps).
*   **Decision Point**: How to cache volatile data?
*   **Action**:
    *   **Redis**: Implement Cache-Aside for `GET /seats`.
    *   **Optimization**: Use **Protobuf** (gRPC) instead of JSON for the seat map to reduce bandwidth.
    *   **Advanced**: Add a **Bloom Filter** in memory to instantly reject requests for sold-out events.
*   **Verification**: Run k6 again. Read throughput should skyrocket.

> [!NOTE]
> Was found that read is not a bottlenack at all - DB handle it perfectly with own cache layer. Didn't touch.
> Write was rewriten with different cache approaches: [Phase-3](phase-3-results.md) and [Phase-3.1](phase-3.1-results.md)
[tag Phase-3](https://github.com/vasilymarchenko/SeatGrid-Tickets/tree/Phase-3)
[tag Phase-3.1](https://github.com/vasilymarchenko/SeatGrid-Tickets/tree/Phase-3.1)


### Phase 4: Write Optimization (Async & Queues)
**Goal**: Fix the "Write" bottleneck (The Thundering Herd).
*   **Decision Point**: Sync consistency vs. System availability.
*   **Action**:
    *   Introduce **RabbitMQ** or **Kafka**.
    *   Change `POST /book` to accept the request, publish a message `TicketRequested`, and return `202 Accepted`.
    *   Create a **Worker Service** (Consumer) that processes messages one by one (or in batches) to update the DB.
    *   Implement **SignalR** or Polling endpoint to let the client know the result.

> [!NOTE]
> TODO: Phases 4 and 5 will be rconsidered.

### Phase 5: Distributed Transactions (Sagas)
**Goal**: Handle distributed failures (e.g., Payment fails after Seat is reserved).
*   **Scenario**: Split the Monolith. Create a separate "Payment Service" (Mock).
*   **Action**:
    *   Implement the **Orchestration Saga** using **MassTransit**.
    *   **Flow**:
        1.  Inventory Service: Reserve Seat (Pending).
        2.  Payment Service: Charge User.
        3.  Inventory Service: Confirm Seat (Booked).
    *   **Compensation**: If Payment fails -> Release Seat.

### Phase 6: Sharding & High Availability
**Goal**: Scale the Database.
*   **Decision Point**: How to partition data?
*   **Action**:
    *   **Sharding**: Simulate sharding logic in the app.
        *   Shard Key: `EventId`.
        *   Events 1-1000 go to DB_Instance_A.
        *   Events 1001-2000 go to DB_Instance_B.
    *   **Replication**: Configure PostgreSQL with a Read Replica. Direct `GET` requests to the replica.

### Phase 7: Analytics (Big Data)
**Goal**: Analyze user behavior without slowing down the transactional DB.
*   **Action**:
    *   **Streaming**: Publish `SeatViewed` events to Kafka.
    *   **Ingestion**: Use a consumer to dump these events into **ClickHouse** (or Elasticsearch).
    *   **Query**: Write a query to find "Hot Seats" (most viewed but not booked).

---

## 4. Tech Stack & Local Environment
All of this can run on a standard laptop with Docker Desktop (Kubernetes enabled).

*   **Language**: C# (.NET 8/9)
*   **Orchestration**: Kubernetes (Helm or plain manifests)
*   **Gateway**: YARP or Nginx Ingress
*   **Databases**:
    *   PostgreSQL (Bitnami Helm Chart)
    *   Redis
    *   ClickHouse (Single node docker)
*   **Messaging**: RabbitMQ (easier for local) or Kafka (better for "Big Data" feel)
*   **Observability**:
    *   Prometheus & Grafana
    *   Jaeger / Zipkin
    *   Loki (for logs)
*   **Testing**: k6 (Load Testing)

## 5. How to Start (Immediate Next Steps)
1.  **Scaffold**: Create a solution `SeatGrid.sln` with `SeatGrid.API` and `SeatGrid.Domain`.
2.  **Dockerize**: Add a `docker-compose.yml` with Postgres and the API.
3.  **Baseline**: Write the "Bad" code first. Don't over-engineer early.
