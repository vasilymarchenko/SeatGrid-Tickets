namespace SeatGrid.API.Domain.ValueObjects;

// DDD — Value Object (Closed-Set Enumeration variant)
// BookingStatus is a Value Object: equality is determined by value, not reference.
// It uses the Closed-Set Enumeration pattern instead of a C# enum because:
//   1. A plain enum leaks persistence concerns into the domain (int or string mapping).
//   2. An enum can be cast from any integer: (BookingStatus)999 compiles and passes
//      invariant checks silently. This class makes that impossible.
//   3. Methods can later be added directly to BookingStatus (e.g. CanTransitionTo())
//      without touching switch statements scattered across the codebase.
//
// The private constructor enforces the closed set: the only valid instances are the
// three static fields. No code outside this class can create a fourth status.
// Equality operators are manually overridden because this is a class, not a record;
// the default object reference equality would break status comparisons in aggregates.
public sealed class BookingStatus
{
    public static readonly BookingStatus Pending   = new("Pending");
    public static readonly BookingStatus Confirmed = new("Confirmed");
    public static readonly BookingStatus Cancelled = new("Cancelled");

    public string Value { get; }

    private BookingStatus(string value) => Value = value;

    public override string ToString() => Value;

    public override bool Equals(object? obj) =>
        obj is BookingStatus other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static bool operator ==(BookingStatus? left, BookingStatus? right) =>
        left?.Value == right?.Value;

    public static bool operator !=(BookingStatus? left, BookingStatus? right) =>
        !(left == right);
}
