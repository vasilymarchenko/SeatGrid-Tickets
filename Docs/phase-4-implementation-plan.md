# Phase 4 Implementation Plan: Distributed Transactions & Sagas

## 1. Objective
Transition the system from a synchronous "Book & Commit" model to an asynchronous "Reserve & Pay" model. This is necessary to handle high-latency external dependencies (Payments) and ensure data consistency across distributed services without blocking the high-throughput booking API.

## 2. Architecture Changes
*   **New Infrastructure**: RabbitMQ (Message Broker).
*   **New Service**: `SeatGrid.PaymentService` (Worker Service).
*   **Modified Service**: `SeatGrid.API` (Becomes the "Initiator" and "Finalizer").
*   **Communication**: MassTransit (Abstraction over RabbitMQ).

## 3. The Workflow (The Saga)
We will implement a **Choreography-based Saga** (or simple Event-Driven Architecture) for this learning phase.

### Step 1: Reservation (Fast)
*   **Actor**: `SeatGrid.API`
*   **Action**:
    1.  User requests seats.
    2.  **Redis Gatekeeper**: Locks seats with a **TTL (e.g., 120 seconds)**.
    3.  **Bus**: Publishes `BookingInitiated` event.
    4.  **Response**: Returns `202 Accepted` to the user immediately.

### Step 2: Payment (Slow)
*   **Actor**: `SeatGrid.PaymentService`
*   **Trigger**: Consumes `BookingInitiated`.
*   **Action**:
    1.  Simulates processing delay (e.g., 2 seconds).
    2.  Simulates outcome:
        *   **Success (85%)**: Publishes `PaymentSucceeded`.
        *   **Failure (15%)**: Publishes `PaymentFailed`.

### Step 3: Finalization
*   **Actor**: `SeatGrid.API` (or a dedicated Booking Worker)
*   **Trigger**: Consumes Payment Events.
*   **Action**:
    *   **On `PaymentSucceeded`**:
        1.  Persist booking to PostgreSQL (Permanent Record).
        2.  Update Redis: Remove TTL (Make lock permanent).
    *   **On `PaymentFailed`**:
        1.  **Compensation**: Delete Redis Key (Release seats immediately).

## 4. Implementation Steps

### Step 1: Infrastructure Setup
*   Add **RabbitMQ** to `docker-compose.infra.yml`.
*   Add **MassTransit** NuGet packages to the API project.

### Step 2: The Payment Service
*   Create a new .NET Worker Service project (`SeatGrid.PaymentService`).
*   Implement the "Mock Payment" logic (Delay + Random Failure).
*   Configure MassTransit consumer.

### Step 3: Refactor Booking Logic
*   Define Integration Events: `BookingInitiated`, `PaymentSucceeded`, `PaymentFailed`.
*   Modify `BookingsController`:
    *   Switch from "Write to DB" to "Publish to Bus".
    *   Add TTL to the Redis locking mechanism.
*   Create Consumers in the API project to handle the finalization logic.

### Step 4: Testing & Observability
*   **Load Test Strategy (2-Phase)**:
    1.  **Phase 1: The Assault (Reservation)**
        *   Generate high concurrency booking requests (2000 users / 100 seats).
        *   **Expectation**: API returns `202 Accepted` immediately.
        *   **Metric**: High RPS, low latency (since we only hit Redis).
    2.  **Phase 2: The Settlement (Validation)**
        *   Wait for the "Payment Window" (e.g., 5-10 seconds).
        *   **Validation**: Query `GET /events/{id}/seats`.
        *   **Success Criteria**:
            *   **Booked**: ~85 seats (matching the 85% success rate).
            *   **Available**: ~15 seats (released after payment failure).
            *   **Stuck/Reserved**: 0 seats (Consistency check).
*   **Tracing**: Ensure OpenTelemetry traces connect the API request -> RabbitMQ -> Payment Service.
*   **Resilience**: Verify that if the Payment Service is down, messages pile up in RabbitMQ and are processed when it comes back online.

## 5. Success Criteria
*   **Throughput**: API remains fast (low latency) even when "Payments" take 2 seconds.
*   **Consistency**: "Ghost Bookings" (failed payments) are automatically released after TTL or Compensation.
*   **Visibility**: We can see the full lifecycle of a booking across services in Jaeger/Grafana.
