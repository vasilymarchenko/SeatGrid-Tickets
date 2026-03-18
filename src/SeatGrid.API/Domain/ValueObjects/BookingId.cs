namespace SeatGrid.API.Domain.ValueObjects;

// DDD — Strongly-Typed Identity / Value Object
// Wrapping a raw Guid in a dedicated type is the "primitive obsession" fix.
// Without this, a method like Reserve(Guid bookingId, Guid eventId) compiles
// even when the arguments are passed in the wrong order — the type system cannot
// catch it. With BookingId and EventId as distinct types, swapping them is a
// compile error. This is purely a domain concern; no infrastructure knowledge needed.
//
// It is a Value Object because two BookingIds with the same Guid ARE equal —
// identity is determined by the value, not by reference. C# records give us
// structural equality (==, Equals, GetHashCode) for free.
public sealed record BookingId(Guid Value)
{
    // Factory method keeps Guid.NewGuid() out of callers and makes intent explicit.
    public static BookingId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
