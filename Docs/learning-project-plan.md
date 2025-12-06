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


### Phase 4: Distributed Transactions (Async & Sagas)
**Goal**: Implement the "Reservation Pattern" to handle high-latency payments.
*   **Scenario**:
    1.  **Booking (Fast)**: User grabs a seat. It is reserved for 2 minutes.
    2.  **Payment (Slow)**: User pays. If successful, booking is confirmed. If failed/timeout, seat is released.
*   **Action**:
    *   **API Update**: `POST /book` now does:
        *   **Redis**: `SETNX` with **TTL (120s)**. (The Reservation).
        *   **Bus**: Publish `BookingInitiated`.
        *   **Return**: `202 Accepted`.
    *   **Payment Service (Consumer)**:
        *   Consumes `BookingInitiated`.
        *   **Logic**: Sleep 2s. Randomly fail 15% (simulate card decline).
        *   **Retry Policy**: Configure MassTransit to retry transient failures 3 times.
    *   **Completion (Saga)**:
        *   **Success**: Insert into Postgres `Bookings`. Update Redis (Remove TTL).
        *   **Failure**: Delete Redis Key (Compensation).
*   **Key Learning**: Handling "Ghost Records" (Redis keys that expire before payment completes) and eventual consistency.

### Phase 5: Resilience & Scaling
**Goal**: Scale the consumers and survive crashes.
*   **Action**:
    *   **Horizontal Scaling**: Run multiple instances of the Booking/Payment consumers.
    *   **Resilience**: Kill the Payment Service during a load test. Verify that messages persist in RabbitMQ and are processed when the service returns.
    *   **K8s HPA**: (Optional) Configure Horizontal Pod Autoscaler based on CPU or Queue Depth.

### Phase 6: Analytics (Big Data)
**Goal**: Analyze user behavior without slowing down the transactional DB.
*   **Action**:
    *   **Streaming**: Publish `SeatViewed` events to Kafka/RabbitMQ.
    *   **Ingestion**: Use a consumer to dump these events into **ClickHouse**.
    *   **Query**: Write a query to find "Hot Seats" (most viewed but not booked).

### Phase 7: (Optional) Sharding
**Goal**: Scale the Database if single-node limits are reached.
*   **Note**: With 5.5k RPS on a single node, this is likely unnecessary for the learning scope unless targeting >50k RPS.
*   **Action**: Simulate application-side sharding based on `EventId`.

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
