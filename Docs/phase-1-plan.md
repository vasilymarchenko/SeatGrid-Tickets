# Phase 1 Implementation Plan: The Naive Monolith

This document outlines the step-by-step plan to build the initial "Naive Monolith" version of SeatGrid. The goal is to establish a functional baseline that we can later break and optimize.

## 1. Solution Scaffolding
**Goal**: Set up the .NET solution structure.
- [x] **Create Solution**: `SeatGrid.sln`
- [x] **Create Projects**:
    - `src/SeatGrid.API`: ASP.NET Core Web API (.NET 8).
    - `src/SeatGrid.Domain`: Class library for entities and interfaces (keeping it simple but clean).
- [x] **Dependencies**:
    - Add reference from API to Domain.
    - Install Nuget packages: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`.

## 2. Infrastructure (Docker)
**Goal**: Run the database locally.
- [x] **Create `docker-compose.yml`**:
    - Service: `postgres` (Latest Alpine image).
    - Configuration: Port 5432, Environment variables for User/Pass/DB.
    - Volume: Persist data locally.
- [ ] **Verify**: Ensure we can connect to the DB using a tool (e.g., DBeaver or VS Code Database extension).

## 3. Domain & Data Layer
**Goal**: Define the data model and set up Entity Framework Core.
- [x] **Define Entities** (in `SeatGrid.Domain`):
    - `Event`: Id, Name, Date, Venue config (Rows, Cols).
    - `Seat`: Id, EventId, Row, Col, Status (Available, Booked), CurrentHolderId.
- [x] **Setup EF Core** (in `SeatGrid.API`):
    - Create `SeatGridDbContext`.
    - Configure Entity relationships (One Event -> Many Seats).
    - Add Connection String to `appsettings.json`.
- [x] **Migrations**:
    - Generate Initial Migration.
    - Apply migration on startup (or via CLI).

## 4. API Implementation (The "Naive" Logic)
**Goal**: Implement the core endpoints with synchronous, locking logic.

### A. Event Management
- [x] **Endpoint**: `POST /api/events`
- [x] **Logic**:
    - Input: Name, Rows, Cols.
    - Action: Save Event to DB. **Synchronously** generate `Rows * Cols` Seat records and save them. (Note: This will be slow for large venues, which is intended).

### B. Seat Map (The Read Bottleneck)
- [x] **Endpoint**: `GET /api/events/{id}/seats`
- [x] **Logic**:
    - Fetch all seats for the event from Postgres.
    - Return the full list as a large JSON array.
    - *Intentionally* do not use pagination or caching yet.

### C. Booking (The Write Bottleneck)
- [x] **Endpoint**: `POST /api/bookings`
- [x] **Logic**:
    - Input: EventId, List of Seat positions (Row, Col), UserId.
    - **Transaction**:
        1. Start DB Transaction.
        2. Select requested seats `FOR UPDATE` (Pessimistic Locking) or just standard select (Optimistic concurrency issues). *Decision: Let's use standard select first to see race conditions, or `FOR UPDATE` to see locking performance hits.* -> **Let's use a standard transaction to demonstrate correctness but poor performance.**
        3. Check if ALL seats are `Available`.
        4. If yes: Update status to `Booked`, set `UserId`.
        5. If no: Rollback and return 409 Conflict.
        6. Commit.

## 5. Testing & Verification
**Goal**: Verify functionality and prepare for load testing.

### A. Functional Testing
- [x] **REST Client**: Create a `requests.http` file in the root.
    - Scenarios: Create Event, Get Seats, Book Seats, Verify Double Booking prevention.

### B. Load Testing (Baseline)
- [x] **Setup k6**: Install k6 locally (or use the docker image).
- [x] **Script**: Create `tests/k6/baseline_test.js`.
    - Scenario:
        - 1. Create an event (setup).
        - 2. Ramp up VUs (Virtual Users).
        - 3. Users constantly fetch seat map.
        - 4. Users randomly try to book available seats.
