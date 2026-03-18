# Booking Flow — Deep Dive

A technical walkthrough of the full booking lifecycle in SeatGrid, from the initial HTTP request to the final seat lock in the database. Covers the involved technologies, where DDD patterns appear, and why each one is there.

---

## Overview

A booking passes through three distinct execution contexts:

1. **HTTP hot path** — the `BookingsController` handles the request in under 5 ms. It touches only Redis. No SQL.
2. **Payment service** — an independent microservice processes payment asynchronously over RabbitMQ. Takes ~2 seconds.
3. **Finalizer consumer** — `BookingFinalizerConsumer` reacts to the payment outcome, drives the `Booking` aggregate, and persists a single record to PostgreSQL.

The two most important design constraints that shape every detail below:

- **No database write on the hot path.** The controller must stay at Redis speed to survive 5,000+ RPS.
- **The `Booking` aggregate is born Confirmed, never persisted as Pending.** The Redis TTL is the pending state. This eliminates an entire class of compensating transactions.

---

## Technology Stack

| Technology | Role in this flow |
|---|---|
| **ASP.NET Core** | HTTP endpoint, request validation |
| **Redis** | Fast gatekeeper — atomic seat reservation and TTL-based pending state |
| **MassTransit** | Message bus abstraction over RabbitMQ — publishing, consuming, retry, and idempotency transport |
| **RabbitMQ** | Message broker — decouples the API from the Payment service |
| **EF Core** | ORM — maps the `Booking` aggregate to PostgreSQL; used exclusively in the consumer |
| **MediatR** | In-process domain event dispatcher — wired into `SaveChangesAsync` |
| **PostgreSQL** | Durable storage — `Bookings` (order record) and `Seats` (availability grid) |

---

## Step 1 — HTTP Request arrives

```
POST /api/Bookings
{
  "eventId": 42,
  "userId": "user-123",
  "seats": [{ "row": "5", "col": "12" }, { "row": "5", "col": "13" }]
}
```

`BookingsController` receives the request. It has two dependencies: `IBookedSeatsCache` (Redis) and `IPublishEndpoint` (MassTransit). It does **not** depend on `SeatGridDbContext`, any repository, or any booking service. This is intentional — the controller is ignorant of persistence.

---

## Step 2 — Redis: Atomic Seat Reservation

```csharp
var reserved = await _bookedSeatsCache.TryReserveSeatsAsync(
    request.EventId,
    seatPairs,
    TimeSpan.FromSeconds(120),
    CancellationToken.None);
```

`TryReserveSeatsAsync` executes a **Lua script** against Redis. Lua scripts run atomically on the Redis server — no other command can interleave. The script checks each seat key in one round-trip. If any seat is already reserved (key exists), the entire operation fails and no seats are locked. If all seats are free, it sets all keys with a 120-second TTL.

The 120-second TTL is the "pending" state. No row is written to PostgreSQL yet. If the payment takes longer than 120 seconds, the keys expire and the seats become available again automatically.

**Why Redis and not Postgres here?**  
A SQL `SELECT ... FOR UPDATE` under 5,000 RPS causes lock contention and connection exhaustion. Redis can handle this in microseconds and rejects conflicts without a database round-trip.

If the seats are already reserved, the controller returns `409 Conflict` immediately. The request ends here — nothing is published, nothing is written.

---

## Step 3 — Publish `BookingInitiated` to RabbitMQ

```csharp
var orderId = Guid.NewGuid();

await _publishEndpoint.Publish(new BookingInitiated
{
    OrderId   = orderId,
    EventId   = request.EventId,
    Seats     = request.Seats.Select(s => new SeatLocation(...)).ToList(),
    UserId    = request.UserId,
    CreatedAt = DateTime.UtcNow
});
```

`BookingInitiated` is an **integration event** — a message in the `SeatGrid.Contracts` shared library, defined as a plain C# `record`. It crosses the service boundary between `SeatGrid.API` and `SeatGrid.PaymentService`.

`IPublishEndpoint` is **MassTransit's** abstraction over the broker. Publishing is fire-and-forget from the controller's perspective. MassTransit serialises the record to JSON and routes it to the RabbitMQ exchange. MassTransit handles retries, dead-letter queues, and correlationIds transparently.

A new `OrderId` (`Guid.NewGuid()`) is generated here. It becomes the **saga correlation key** — every message in the chain carries it so that the consumer can match the payment result to the original booking attempt.

If `Publish` throws (broker unreachable), the catch block calls `ReleaseSeatsAsync` to remove the Redis keys. The compensation runs synchronously — no saga is needed here because the controller hasn't committed anything to the database yet.

The controller returns `202 Accepted`. The HTTP request is complete. Total time: dominated by the Redis round-trip, typically sub-5 ms.

---

## Step 4 — Payment Service processes the message

`SeatGrid.PaymentService` is a separate process. Its `PaymentConsumer` implements `IConsumer<BookingInitiated>` (MassTransit).

```csharp
public async Task Consume(ConsumeContext<BookingInitiated> context)
{
    await Task.Delay(2000); // simulate processing

    if (_random.NextDouble() < 0.15)
        await context.Publish(new PaymentFailed  { OrderId = ..., ... });
    else
        await context.Publish(new PaymentSucceeded { OrderId = ..., ... });
}
```

MassTransit delivers `BookingInitiated` to this consumer from the RabbitMQ queue. The consumer simulates 2 seconds of processing and then publishes either `PaymentSucceeded` or `PaymentFailed` back to the exchange. Both messages carry the same `OrderId`, `EventId`, and `Seats` from the original message — no database lookup needed.

The result goes back into RabbitMQ. `SeatGrid.API`'s `BookingFinalizerConsumer` is subscribed to both outcome messages.

---

## Step 5 — `BookingFinalizerConsumer` receives the outcome

`BookingFinalizerConsumer` implements both `IConsumer<PaymentSucceeded>` and `IConsumer<PaymentFailed>`. MassTransit creates it from the DI container for each message delivery.

### Step 5a — `PaymentSucceeded` path

#### Idempotency check

```csharp
var existing = await _bookingRepository.GetByOrderIdAsync(msg.OrderId, context.CancellationToken);
if (existing is not null)
{
    _logger.LogWarning("Booking for Order {OrderId} already processed — skipping", msg.OrderId);
    return;
}
```

MassTransit guarantees **at-least-once delivery**. If the consumer crashes after `SaveChangesAsync` but before it acknowledges the message, RabbitMQ redelivers it. Without this check, the second delivery would attempt to insert a `Booking` with the same `OrderId`, hit the unique index, and throw.

`GetByOrderIdAsync` calls EF Core: `_context.Bookings.Include(b => b.Seats).FirstOrDefaultAsync(b => b.OrderId == orderId, ct)`. If the row exists, the record was already committed and the consumer returns safely. This is the **idempotency guard**.

#### Building the Booking aggregate — DDD in action

```csharp
var booking = Domain.Entities.Booking.Create(msg.OrderId, msg.EventId, msg.UserId);

foreach (var seat in msg.Seats)
    booking.AddSeat(new DomainSeatLocation(seat.Row, seat.Col));

booking.Confirm();
```

This is where DDD patterns are applied:

**`Booking.Create(...)` — Named Constructor / Static Factory**  
The `Booking` constructor is `private`. The only way to create a valid `Booking` from outside the class is through `Create()`. This enforces that a `Booking` is always created with the correct initial state (`Status = Pending`) and a valid `BookingId` (`Guid`-based value object with `BookingId.New()`). It is structurally impossible to create a `Booking` without an `OrderId` or with a null `UserId`.

**`booking.AddSeat(location)` — Invariant enforcement**  
`AddSeat` is a method on the aggregate root, not a setter on a collection. Internally it checks:

```csharp
if (_seats.Any(s => s.Location == location))
    throw new DomainException($"Seat {location} is already booked in this booking.");
```

The "no duplicate seat in a booking" invariant lives here, not in a service or handler. It cannot be bypassed because `_seats` is a `private readonly List<BookedSeat>` — external code cannot reach it. This is the **aggregate protecting its own invariants**.

`SeatLocation` is a **Value Object**: `record struct SeatLocation(int Row, int Col)`. It has no identity — two `SeatLocation(5, 12)` instances are equal by value. EF Core maps it as an **owned entity** (no separate table, columns embedded in `BookedSeats`).

**`booking.Confirm()` — State transition + Domain Event**

```csharp
public void Confirm()
{
    if (Status != BookingStatus.Pending)
        throw new DomainException("Only a Pending booking can be confirmed.");

    Status = BookingStatus.Confirmed;

    AddDomainEvent(new BookingConfirmed(
        OrderId,
        EventId,
        UserId,
        _seats.Select(s => s.Location).ToList().AsReadOnly(),
        DateTimeOffset.UtcNow));
}
```

`Confirm()` guards the transition: only a `Pending` booking can move to `Confirmed`. This guard is the aggregate's contract. Calling `Confirm()` twice — or on a `Cancelled` booking — is a domain error.

When the guard passes, the aggregate calls `AddDomainEvent(...)`. The `BookingConfirmed` record is stored in the aggregate's private `List<IDomainEvent> _domainEvents`. Nothing is published yet. The event just accumulates in memory.

`BookingConfirmed` is a **Domain Event** — a `sealed record` that implements `IDomainEvent`. It is named in the past tense ("something happened"). It carries exactly the data handlers need: `OrderId`, `EventId`, `UserId`, `Seats`, `OccurredAt`. Handlers do not need to query the database separately.

#### Persisting the aggregate — EF Core and the Repository

```csharp
await _bookingRepository.AddAsync(booking, context.CancellationToken);
await _context.SaveChangesAsync(context.CancellationToken);
```

**`IBookingRepository` — Repository Pattern**  
`IBookingRepository` is defined in the **Domain layer**. The `Booking` aggregate has no knowledge of EF Core, `DbContext`, or SQL. The repository is the domain's contract: "give me a way to load and save bookings — I don't care how."

`BookingRepository` (Infrastructure layer) implements it:

```csharp
public async Task AddAsync(Booking booking, CancellationToken ct)
    => await _context.Bookings.AddAsync(booking, ct);
```

`AddAsync` calls EF Core's `AddAsync`, which puts the `Booking` — and its child `BookedSeat` entities — into the EF change tracker with state `Added`. No SQL is executed yet.

**`SaveChangesAsync` — EF Core + Domain Event Dispatch**  
`SeatGridDbContext.SaveChangesAsync` is overridden:

```csharp
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    // 1. Write to PostgreSQL
    var result = await base.SaveChangesAsync(ct);

    // 2. Collect domain events from all tracked aggregate roots
    var aggregates = ChangeTracker
        .Entries<AggregateRoot>()          // non-generic base — finds all aggregates
        .Select(e => e.Entity)
        .Where(e => e.DomainEvents.Count > 0)
        .ToList();

    var events = aggregates
        .SelectMany(a => a.DomainEvents)
        .ToList();

    // 3. Clear BEFORE dispatching — prevents re-dispatch on a second SaveChangesAsync
    foreach (var aggregate in aggregates)
        aggregate.ClearDomainEvents();

    // 4. Dispatch via MediatR
    foreach (var evt in events)
        await _mediator.Publish(evt, ct);

    return result;
}
```

Step by step:

1. **`base.SaveChangesAsync(ct)`** — EF Core computes the change set from the tracker (the `Booking` + its `BookedSeats`), generates `INSERT` SQL, and commits it to PostgreSQL in a transaction. The `Bookings` table now has a row with `Status = Confirmed` and `OrderId = <saga key>`. The `BookedSeats` table has a row per seat. The `OrderId` unique index prevents duplicates.

2. **`ChangeTracker.Entries<AggregateRoot>()`** — EF Core's `ChangeTracker` holds all entities that were touched during this unit of work. The query uses the **non-generic `AggregateRoot` base class**. This is a subtle but important detail: if the query used `Entries<AggregateRoot<BookingId>>()`, C# generic invariance would cause it to silently return zero entries (because `AggregateRoot<BookingId>` is not assignment-compatible with `AggregateRoot<SomeOtherId>`). The non-generic base resolves this — every aggregate satisfies it regardless of its `TId`.

3. **Collect and clear before dispatching** — if a handler calls `SaveChangesAsync` again (e.g., `SeatStatusConfirmedHandler` mutates `Seat` rows), clearing first ensures the original `BookingConfirmed` event is not published a second time.

4. **`_mediator.Publish(evt, ct)`** — MediatR receives the `BookingConfirmed` notification and routes it to every registered `INotificationHandler<BookingConfirmed>`. In this system, there are two.

---

## Step 6 — Domain Event Handlers

Both handlers run synchronously inside the same `SaveChangesAsync` call, on the same thread, within the same DI scope (same `SeatGridDbContext` instance).

### `BookingConfirmedHandler` — Redis update

```csharp
public async Task Handle(BookingConfirmed notification, CancellationToken ct)
{
    var seatPairs = notification.Seats
        .Select(s => (s.Row.ToString(), s.Col.ToString()))
        .ToList();

    await _cache.AddBookedSeatsAsync(notification.EventId, seatPairs, ct);
}
```

`AddBookedSeatsAsync` makes the Redis reservation **permanent** — it removes the 120-second TTL (or sets a much longer one). The seats were already locked since Step 2; this step ensures they stay locked after the TTL would have expired.

Why here instead of in the consumer? The consumer's job is to drive the aggregate and save it. Redis is a side-effect of confirmation. Separating it into a handler means the consumer has no knowledge of Redis, and the Redis update is independently testable.

### `SeatStatusConfirmedHandler` — Seats table update

```csharp
public async Task Handle(BookingConfirmed notification, CancellationToken ct)
{
    var rowSet  = notification.Seats.Select(s => s.Row.ToString()).ToHashSet();
    var colSet  = notification.Seats.Select(s => s.Col.ToString()).ToHashSet();
    var seatKeys = notification.Seats
        .Select(s => (s.Row.ToString(), s.Col.ToString()))
        .ToHashSet();

    // Narrow DB query by candidate rows/cols, filter precisely in memory
    var candidateSeats = await _context.Seats
        .Where(s => s.EventId == notification.EventId
                 && rowSet.Contains(s.Row)
                 && colSet.Contains(s.Col))
        .ToListAsync(ct);

    var seats = candidateSeats
        .Where(s => seatKeys.Contains((s.Row, s.Col)))
        .ToList();

    foreach (var seat in seats)
    {
        seat.Status          = SeatStatus.Booked;
        seat.CurrentHolderId = notification.UserId;
    }

    // No SaveChangesAsync here — the outer call covers this
}
```

The `Seats` table is the event availability grid — it is what `GET /api/events/{id}/seats` reads. `SeatStatusConfirmedHandler` flips `Seat.Status` from `Available` to `Booked` and stamps `CurrentHolderId`.

Because this handler runs on the **same `SeatGridDbContext` instance** that is still in the middle of `SaveChangesAsync`, EF Core is already tracking the `Seat` entities. Mutating them here marks them as `Modified` in the change tracker. The `SaveChangesAsync` call that dispatched this event will **not** call `base.SaveChangesAsync` again — that already ran in step 1. The `Seat` mutations are picked up by the **next** `SaveChangesAsync` call.

> **Important:** there is no explicit second `SaveChangesAsync` call for these seat mutations in the current flow. The seat mutations are flushed when EF Core's DbContext is disposed at the end of the MassTransit consumer scope, or they can be made explicit by adding a `SaveChangesAsync` call after `_mediator.Publish` in the override. This is a known tradeoff of the "dispatch inside `SaveChangesAsync`" pattern — Stage 6 (Outbox) addresses this by moving dispatch outside the override entirely.

The query is written in two stages (candidate rows from DB, then precise filter in memory) to avoid passing a `HashSet` of string tuples into LINQ-to-SQL, which EF Core cannot translate to a SQL `IN` clause over composite keys.

---

## Step 5b — `PaymentFailed` path

```csharp
public async Task Consume(ConsumeContext<PaymentFailed> context)
{
    var seatPairs = msg.Seats.Select(s => (s.Row.ToString(), s.Col.ToString())).ToList();
    await _bookedSeatsCache.ReleaseSeatsAsync(msg.EventId, seatPairs, context.CancellationToken);
}
```

No aggregate is created. No database is touched. The `Booking` aggregate was never persisted for a failed payment — the Redis TTL was the only pending state. `ReleaseSeatsAsync` deletes the Redis keys immediately rather than waiting for the TTL to expire. Seats are available again within milliseconds.

---

## Data Layer at Rest

After a successful booking, the state is distributed across three stores:

| Store | What is stored | Source of truth for |
|---|---|---|
| **Redis** | Seat keys with no TTL (permanent) | `GET /api/events/{id}/seats` — available seats |
| **PostgreSQL `Bookings`** | One row: `OrderId`, `EventId`, `UserId`, `Status=Confirmed` | Booking audit record, order history |
| **PostgreSQL `BookedSeats`** | One row per seat: `BookingId`, `Row`, `Col` | Seat-to-booking mapping |
| **PostgreSQL `Seats`** | `Status=Booked`, `CurrentHolderId` on each seat row | Seat grid, reconciliation source |

---

## EF Core Configuration

EF Core knows how to map the `Booking` aggregate because of `BookingConfiguration`:

```csharp
public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasConversion(...);   // BookingId value object to Guid
        builder.Property(b => b.OrderId).IsRequired();
        builder.HasIndex(b => b.OrderId).IsUnique();      // idempotency + saga correlation
        builder.OwnsMany(b => b.Seats, seat => { ... });  // BookedSeat as owned entity
    }
}
```

`IEntityTypeConfiguration<T>` classes are discovered automatically via `ApplyConfigurationsFromAssembly(...)` in `OnModelCreating`. Each aggregate owns its mapping — `OnModelCreating` itself stays empty.

The `OrderId` unique index is the database-level idempotency guardrail. Even if the consumer's in-memory check (Step 5a) is somehow bypassed, the `INSERT` will fail with a constraint violation rather than creating a duplicate.

---

## Migration Strategy

Migrations are managed with `dotnet ef migrations`. The current schema was built in three migrations:

| Migration | Change |
|---|---|
| `InitialCreate` | `Events`, `Seats` tables |
| `DDD_Refactor` | `Bookings`, `BookedSeats` tables |
| `DDD_AddOrderIdToBooking` | `OrderId` column + unique index on `Bookings` |

`db.Database.Migrate()` is called in `Program.cs` at startup:

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SeatGridDbContext>();
    db.Database.Migrate();
}
```

This applies any pending migrations automatically when the service starts. No manual step is needed in Docker or on `dotnet run`.

---

## Why the `Booking` Aggregate Exists

Before Phase 5, there was no `Booking` class. Services (`BookingPessimisticService`, etc.) queried `Seat` rows directly and mutated their status. This is the **anemic domain model** anti-pattern: data in one place, behaviour scattered across services.

The `Booking` aggregate replaces that with a single object that owns both the data and the rules:

| Before (anemic) | After (DDD aggregate) |
|---|---|
| `BookingPessimisticService.FinalizeBookingAsync` mutates `Seat.Status` directly | `Booking.Confirm()` transitions state and raises a domain event |
| "No double seat" enforced in a service method | `AddSeat()` enforces the invariant on the collection |
| Construction requires knowing which fields are required | `Booking.Create()` is the only valid construction path |
| Domain events were not a concept | `BookingConfirmed` / `BookingCancelled` decouple behaviour from persistence |
| Repository was a DbContext method | `IBookingRepository` is a domain interface; EF is an implementation detail |

The aggregate is the reason the consumer can be written in terms of business actions (`Create`, `AddSeat`, `Confirm`) rather than SQL operations.
