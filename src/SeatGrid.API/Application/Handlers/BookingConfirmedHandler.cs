using MediatR;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Events;

namespace SeatGrid.API.Application.Handlers;

// DDD — Domain Event Handler
// This handler reacts to the BookingConfirmed event raised inside the Booking aggregate.
// It is the Application layer's response to a domain fact: "a booking was confirmed".
//
// Why here and not in the consumer?
// The consumer's only job is to drive the aggregate (load → call method → save).
// Redis is a side-effect of the confirmation — it belongs in a handler that responds
// to the domain event, not in the code that caused the event. This keeps the consumer
// ignorant of Redis and makes the Redis update testable in isolation.
//
// How it is invoked:
// SaveChangesAsync in SeatGridDbContext collects domain events from all tracked aggregates
// and publishes them via IMediator.Publish(). MediatR routes BookingConfirmed to this handler
// automatically because it implements INotificationHandler<BookingConfirmed>.
// No explicit DI registration is needed — AddMediatR already scans this assembly.
public sealed class BookingConfirmedHandler : INotificationHandler<BookingConfirmed>
{
    private readonly IBookedSeatsCache _cache;
    private readonly ILogger<BookingConfirmedHandler> _logger;

    public BookingConfirmedHandler(IBookedSeatsCache cache, ILogger<BookingConfirmedHandler> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    public async Task Handle(BookingConfirmed notification, CancellationToken ct)
    {
        // SeatLocation carries int Row/Col; the cache interface expects string tuples.
        var seatPairs = notification.Seats
            .Select(s => (s.Row.ToString(), s.Col.ToString()))
            .ToList();

        // Makes the Redis reservation permanent (removes TTL / sets long expiration).
        await _cache.AddBookedSeatsAsync(notification.EventId, seatPairs, ct);

        _logger.LogInformation(
            "Seats permanently booked in cache for Order {OrderId}", notification.OrderId);
    }
}
