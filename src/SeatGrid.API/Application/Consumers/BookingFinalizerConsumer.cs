using MassTransit;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Interfaces;
using SeatGrid.API.Infrastructure.Persistence;
using SeatGrid.Contracts;
using DomainSeatLocation = SeatGrid.API.Domain.ValueObjects.SeatLocation;

namespace SeatGrid.API.Application.Consumers;

public sealed class BookingFinalizerConsumer :
    IConsumer<PaymentSucceeded>,
    IConsumer<PaymentFailed>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly SeatGridDbContext _context;
    private readonly IBookedSeatsCache _bookedSeatsCache;
    private readonly ILogger<BookingFinalizerConsumer> _logger;

    public BookingFinalizerConsumer(
        IBookingRepository bookingRepository,
        SeatGridDbContext context,
        IBookedSeatsCache bookedSeatsCache,
        ILogger<BookingFinalizerConsumer> logger)
    {
        _bookingRepository = bookingRepository;
        _context           = context;
        _bookedSeatsCache  = bookedSeatsCache;
        _logger            = logger;
    }

    public async Task Consume(ConsumeContext<PaymentSucceeded> context)
    {
        var msg = context.Message;
        _logger.LogInformation("Finalizing booking for Order {OrderId}", msg.OrderId);

        // Idempotency guard: MassTransit delivers at-least-once.
        // If the consumer crashes after SaveChangesAsync but before the message is acked,
        // the same message is redelivered. The Booking already exists in Confirmed state.
        // Detect it here and return rather than hitting the unique-index constraint.
        var existing = await _bookingRepository.GetByOrderIdAsync(msg.OrderId, context.CancellationToken);
        if (existing is not null)
        {
            _logger.LogWarning("Booking for Order {OrderId} already processed — skipping", msg.OrderId);
            return;
        }

        // DDD — Create and confirm the aggregate in one shot, from the message payload.
        // The controller never writes to Postgres; it stays fast (Redis + Publish only).
        // The Booking is born in Confirmed state here, after payment is known to have succeeded.
        // There is no persisted Pending record — the Redis TTL served that role.
        var booking = Domain.Entities.Booking.Create(msg.OrderId, msg.EventId, msg.UserId);
        foreach (var seat in msg.Seats)
            booking.AddSeat(new DomainSeatLocation(seat.Row, seat.Col));

        // DDD — State transition: Pending → Confirmed raises BookingConfirmed domain event.
        // The event carries OrderId, EventId, and the seat list so handlers need not query the DB.
        booking.Confirm();

        await _bookingRepository.AddAsync(booking, context.CancellationToken);
        await _context.SaveChangesAsync(context.CancellationToken);
        // SaveChangesAsync dispatches BookingConfirmed →
        //   BookingConfirmedHandler    → AddBookedSeatsAsync (makes Redis lock permanent)
        //   SeatStatusConfirmedHandler → flips Seat.Status = Booked in the Seats table

        _logger.LogInformation("Booking confirmed for Order {OrderId}", msg.OrderId);
    }

    public async Task Consume(ConsumeContext<PaymentFailed> context)
    {
        var msg = context.Message;
        _logger.LogWarning("Payment failed for Order {OrderId}. Releasing seats.", msg.OrderId);

        // No Booking aggregate exists for a failed payment — the controller never persisted one.
        // The Seats table row was never mutated, so nothing to revert there either.
        // Release the Redis reservation directly so seats become available immediately
        // rather than waiting for the TTL to expire.
        var seatPairs = msg.Seats.Select(s => (s.Row.ToString(), s.Col.ToString())).ToList();
        await _bookedSeatsCache.ReleaseSeatsAsync(msg.EventId, seatPairs, context.CancellationToken);
    }
}

