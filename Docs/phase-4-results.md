# Phase 4: Distributed Transactions (Saga Pattern) Results

## Overview
We successfully migrated the booking system from a synchronous "Booking First" approach to an asynchronous "Reservation First" (Saga) pattern. This allows the system to handle high concurrency and slow payment processing without blocking the user or holding database connections.

## Architecture Changes

### 1. Reservation Pattern
- **Initial Request**: The API now performs a lightweight reservation in Redis with a TTL (120s).
- **Response**: Returns `202 Accepted` immediately to the client.
- **Event**: Publishes `BookingInitiated` to RabbitMQ.

### 2. Payment Service
- **New Service**: `SeatGrid.PaymentService` listens for `BookingInitiated`.
- **Simulation**: Simulates a 2-second processing delay.
- **Failure Injection**: Randomly fails 15% of payments to test compensation logic.
- **Events**: Publishes `PaymentSucceeded` or `PaymentFailed`.

### 3. Saga Orchestration (Choreography)
- **Finalizer**: `SeatGrid.API` (`BookingFinalizerConsumer`) listens for payment results via RabbitMQ.
- **Success path** (`PaymentSucceeded`):
  1. Persists the booking to PostgreSQL via `IBookingService`.
  2. On DB success: calls `AddBookedSeatsAsync`, which overwrites the Redis field value to `DateTime.MaxValue.Ticks` — marking the seat as permanently booked (never expires in the stale-detection logic). The Redis key itself retains a 24h expiry.
  3. On DB failure: calls `ReleaseSeatsAsync` as compensation (seat freed for others).
- **Failure path** (`PaymentFailed`): calls `ReleaseSeatsAsync` — removes the 120s TTL reservation so the seat becomes available again immediately.

### 4. Customer Notification Gap
The `202 Accepted` response tells the client *"Booking initiated. Please wait for payment confirmation."* — but **no push notification exists**. The customer has no way to know the final outcome without polling:
- There is no `GET /api/orders/{orderId}` status endpoint.
- There is no WebSocket / SignalR channel.
- The only indirect check is `GET /api/events/{id}/seats` — if the seat status transitions to `Booked`, payment succeeded; if it returns to `Available`, payment failed.

This is a known architectural gap. A production system would require a query-side order status resource or a push mechanism (SignalR, webhooks, email/SMS).

### 5. Consistency Strategy (Refinement)
We switched from Optimistic to **Pessimistic Locking** for the background consumer (`BookingFinalizerConsumer`) to prioritize consistency over latency:
- **Guaranteed Success**: Once a user has paid, the system must make every effort to fulfill the order. Pessimistic locking (`FOR UPDATE`) waits for locks rather than failing fast, ensuring the transaction completes even under contention.
- **Idempotency**: The service handles duplicate messages gracefully. If a seat is already booked by the *same* user, it is treated as a success.
- **Simplicity**: Avoids complex retry logic required by optimistic locking in a background process.

## Load Test Results

We ran a load test with **2000 VUs** for **40 seconds**, targeting a single event with **100 seats**.

### Metrics
- **Total Requests**: 214,112
- **Accepted (202)**: 117 (Initial successful reservations)
- **Conflict (409)**: 213,995 (Users hitting locked seats)
- **Errors (500)**: 0 (System remained stable)

### Functional Validation
- **Total Seats**: 100
- **Booked**: 100 (100% utilization)
- **Available**: 0

### Resilience Verification
The test confirmed that the system self-heals. Even with a **15% payment failure rate**, the compensation logic successfully released the seats, and subsequent requests from other users claimed them. This resulted in **100% seat occupancy** despite the payment failures.

## Conclusion
The distributed transaction implementation is robust and handles high concurrency effectively. The "Reservation Pattern" ensures that seats are not held indefinitely by failed transactions, maximizing inventory utilization.

The main open item is the customer notification gap: clients currently have no push-based way to learn the final booking outcome and must resort to polling the seat-status endpoint.
