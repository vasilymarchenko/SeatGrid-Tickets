using SeatGrid.API.Domain.Exceptions;

namespace SeatGrid.API.Domain.ValueObjects;

// DDD — Value Object
// SeatLocation captures where a seat is inside an event venue.
// It has no identity of its own — two locations with the same Row and Col
// are the same location. That makes it a Value Object, not an Entity.
//
// Key DDD rules applied:
//   Immutability  — init-only properties mean the object cannot change after
//                   construction. This is essential: if location could mutate,
//                   the double-booking check inside the Booking aggregate could
//                   be silently bypassed by changing a seat's location after adding it.
//
//   Self-validation — the invariant (row and col must be >= 1) lives here, not
//                     in a service or controller. This means it is impossible to
//                     create an invalid SeatLocation anywhere in the system.
//                     The domain enforces its own rules; no external guard needed.
//
//   Structural equality — C# records compare by value automatically, so
//                         SeatLocation(1, 2) == SeatLocation(1, 2) is true.
//                         This is exactly what the double-booking check relies on.
public sealed record SeatLocation(int Row, int Col)
{
    public int Row { get; init; } = Row >= 1
        ? Row
        : throw new DomainException("Row must be >= 1.");

    public int Col { get; init; } = Col >= 1
        ? Col
        : throw new DomainException("Col must be >= 1.");
}
