using SeatGrid.API.Domain.ValueObjects;

namespace SeatGrid.API.Domain.Entities;

// DDD — Entity (child of the Booking aggregate)
// BookedSeat represents a single seat that has been claimed within a booking.
// It is an Entity rather than a Value Object because it has its own identity
// (Id) and its lifecycle is tied to the Booking — it is created and destroyed
// as part of the Booking aggregate, never independently.
//
// Aggregate boundary rule: the constructor is internal so that BookedSeat can
// only be created from within the same assembly. The only legitimate creator is
// the Booking aggregate root via its AddSeat() method. External code must go
// through Booking — it can never directly instantiate a BookedSeat. This keeps
// the aggregate boundary intact: all invariants (e.g. no duplicates) are
// enforced centrally in the root.
//
// No public setters: the entity's state is set once at construction and never
// mutated. External code can read it, but cannot change it.
public sealed class BookedSeat
{
    // Parameterless constructor required for object materialisation via reflection
    // (used by ORMs, serialisers, etc.). The domain entity has no knowledge of
    // any specific framework — this is a pure C# construct. No EF type is imported.
    private BookedSeat() { }

    internal BookedSeat(SeatLocation location)
    {
        Location = location;
    }

    // EF Core-managed surrogate key. The domain does not care about this value —
    // it exists only so EF can track and persist this row.
    public long Id { get; private set; }

    public SeatLocation Location { get; private set; } = null!;
}
