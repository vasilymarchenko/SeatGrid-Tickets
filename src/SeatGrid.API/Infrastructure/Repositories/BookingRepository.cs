using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Domain.Interfaces;
using SeatGrid.API.Infrastructure.Persistence;

namespace SeatGrid.API.Infrastructure.Repositories;

// DDD — Repository implementation in the Infrastructure layer
// This class is the only place in the codebase that knows how to translate between
// the domain model (Booking aggregate) and the persistence store (PostgreSQL via EF).
// All EF Core types, LINQ queries, and SQL concerns are confined here.
//
// Why sealed?
// Repository implementations are infrastructure leaf classes. They are not designed
// for inheritance — a subclass could bypass invariants. Sealing makes the intent
// explicit and prevents accidental extension.
public sealed class BookingRepository : IBookingRepository
{
    private readonly SeatGridDbContext _context;

    public BookingRepository(SeatGridDbContext context) => _context = context;

    public Task<Booking?> GetByOrderIdAsync(Guid orderId, CancellationToken ct)
        => _context.Bookings
            // DDD — Load the full aggregate, not a partial snapshot.
            // Booking.Confirm() and Booking.Cancel() iterate _seats to build the
            // seat list for domain events. Without Include, EF returns an empty
            // navigation and the domain event carries zero seats — a silent data loss.
            // Always load the aggregate root with all its child entities.
            .Include(b => b.Seats)
            .FirstOrDefaultAsync(b => b.OrderId == orderId, ct);

    public async Task AddAsync(Booking booking, CancellationToken ct)
        => await _context.Bookings.AddAsync(booking, ct);
}
