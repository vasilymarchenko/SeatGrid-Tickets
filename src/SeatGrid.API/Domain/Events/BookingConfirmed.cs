using SeatGrid.API.Domain.ValueObjects;

namespace SeatGrid.API.Domain.Events;

// DDD — Domain Event
// BookingConfirmed records the fact that a booking transitioned from Pending
// to Confirmed. It is named in the past tense — something that has already
// happened inside the domain, not a command asking something to happen.
//
// Why a record?
// Events are immutable data snapshots. A record enforces that by providing
// init-only properties and value equality. Two BookingConfirmed events with
// the same BookingId and OccurredAt are considered identical — there is no
// separate identity concept for an event.
//
// What belongs in the payload?
// Only what downstream handlers need to react without querying the database.
// Here: the BookingId (to look up or route the booking) and the timestamp
// (for ordering / audit). Avoid including the full aggregate state — that
// turns events into DTOs and creates tight coupling between the event and
// the current shape of the aggregate.
public sealed record BookingConfirmed(
    Guid OrderId,
    long EventId,
    string UserId,
    IReadOnlyList<SeatLocation> Seats,
    DateTimeOffset OccurredAt) : IDomainEvent;
