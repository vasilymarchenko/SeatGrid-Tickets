using MediatR;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Events;

namespace SeatGrid.API.Application.Handlers;

// DDD — Domain Event Handler
// Reacts to BookingCancelled. The consumer already handles PaymentFailed by calling
// ReleaseSeatsAsync directly (no aggregate exists at that point). This handler handles
// the case where Cancel() is called on an existing aggregate — which is not currently
// exercised in the happy-path flow, but is the correct hook for future cancellation
// features (e.g. user-initiated cancellation after confirmation).
public sealed class BookingCancelledHandler : INotificationHandler<BookingCancelled>
{
    private readonly IBookedSeatsCache _cache;
    private readonly ILogger<BookingCancelledHandler> _logger;

    public BookingCancelledHandler(IBookedSeatsCache cache, ILogger<BookingCancelledHandler> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    public async Task Handle(BookingCancelled notification, CancellationToken ct)
    {
        var seatPairs = notification.Seats
            .Select(s => (s.Row.ToString(), s.Col.ToString()))
            .ToList();

        await _cache.ReleaseSeatsAsync(notification.EventId, seatPairs, ct);

        _logger.LogInformation(
            "Seats released in cache for Order {OrderId}", notification.OrderId);
    }
}
