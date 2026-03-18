using SeatGrid.API.Domain.Entities;

namespace SeatGrid.API.Domain.Interfaces;

// DDD — Repository interface in the Domain layer
// In DDD the repository is a domain concept: it is the contract that the domain
// uses to retrieve and persist aggregates. The interface belongs here, in Domain,
// not in Application or Infrastructure, because the domain defines what it needs
// — it does not care how persistence is implemented.
//
// Dependency inversion: the Domain layer defines IBookingRepository; the
// Infrastructure layer implements it. This means the domain has zero knowledge
// of EF Core, PostgreSQL, or any persistence technology. You could swap the
// implementation to an in-memory store and the domain code would not change.
//
// One aggregate, one repository:
// There is one repository per aggregate root — not per entity. Booking is the
// aggregate root; BookedSeat is a child entity managed through Booking.
// There is no IBookedSeatRepository because BookedSeat has no independent identity
// outside of its Booking; it cannot be retrieved or persisted on its own.
public interface IBookingRepository
{
    // Load a Booking aggregate by the saga correlation key.
    // Returns null if no booking was initiated for this OrderId.
    // Must load the full aggregate including child BookedSeats so that Confirm()
    // and Cancel() can build the seat list for domain events.
    Task<Booking?> GetByOrderIdAsync(Guid orderId, CancellationToken ct);

    // Persist a newly created Booking aggregate.
    // Does not call SaveChangesAsync — the caller controls the unit of work boundary.
    Task AddAsync(Booking booking, CancellationToken ct);
}
