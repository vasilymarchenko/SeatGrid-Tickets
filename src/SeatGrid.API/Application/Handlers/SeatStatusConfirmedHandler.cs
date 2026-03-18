using MediatR;
using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Domain.Enums;
using SeatGrid.API.Domain.Events;
using SeatGrid.API.Infrastructure.Persistence;

namespace SeatGrid.API.Application.Handlers;

// DDD — Domain Event Handler
// Reacts to BookingConfirmed by flipping the Seat rows in the Seats table.
// This is the persistence side-effect that makes seat availability visible to
// GET /api/events/{id}/seats and CacheReconciliationService.
//
// Why a separate handler instead of doing this in the consumer?
// The consumer drives the Booking aggregate. The Seats table is a different
// concern — it is the event's availability grid, not the order record. Putting
// Seat mutation in the consumer would couple the consumer to two separate
// persistence concerns. The domain event is the decoupling boundary: the aggregate
// raises "booking confirmed", and two independent handlers each react to that fact
// in their own bounded concern (Redis and Seats table).
//
// Why no SeatStatusCancelledHandler?
// On the PaymentFailed path, the consumer never called Booking.Cancel() — it
// released Redis directly. The Seats table was never set to Booked, so there is
// nothing to revert. A cancellation handler would be a no-op at best.
//
// Note: MassTransit serialises consumption per-consumer, so concurrent Confirm
// calls for different orders on the same event are already serialised at the
// queue level. No pessimistic lock (FOR UPDATE) is needed here.
public sealed class SeatStatusConfirmedHandler : INotificationHandler<BookingConfirmed>
{
    private readonly SeatGridDbContext _context;
    private readonly ILogger<SeatStatusConfirmedHandler> _logger;

    public SeatStatusConfirmedHandler(SeatGridDbContext context, ILogger<SeatStatusConfirmedHandler> logger)
    {
        _context = context;
        _logger  = logger;
    }

    public async Task Handle(BookingConfirmed notification, CancellationToken ct)
    {
        // Build lookup sets for efficient in-memory matching.
        // Seat.Row and Seat.Col are strings; SeatLocation.Row/Col are int.
        var rowSet = notification.Seats.Select(s => s.Row.ToString()).ToHashSet();
        var colSet = notification.Seats.Select(s => s.Col.ToString()).ToHashSet();
        var seatKeys = notification.Seats
            .Select(s => (s.Row.ToString(), s.Col.ToString()))
            .ToHashSet();

        // Narrow the DB query by candidate rows/cols first, then filter precisely in memory.
        // Avoiding string concatenation in EF keeps the query translatable across providers.
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

        // SaveChangesAsync is not called here — this handler runs inside the same
        // SaveChangesAsync call that dispatched this event. Calling it again would
        // re-dispatch the same domain events. The outer SaveChanges covers this update.

        _logger.LogInformation(
            "Seat status set to Booked for {Count} seats, Order {OrderId}",
            seats.Count, notification.OrderId);
    }
}
