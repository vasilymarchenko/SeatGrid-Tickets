using SeatGrid.API.Domain.ValueObjects;

namespace SeatGrid.API.Domain.Events;

// DDD — Domain Event
// BookingCancelled records the fact that a booking was cancelled.
// See BookingConfirmed for the full rationale behind domain events as records.
//
// Both events carry the same payload here. In a real system they might diverge —
// e.g. BookingCancelled could carry a CancellationReason. That decision belongs
// to the domain when the need arises, not today.
public sealed record BookingCancelled(
    Guid OrderId,
    long EventId,
    IReadOnlyList<SeatLocation> Seats,
    DateTimeOffset OccurredAt) : IDomainEvent;
