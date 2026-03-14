using MassTransit;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.Contracts;

namespace SeatGrid.API.Application.Consumers
{
    public class BookingFinalizerConsumer : 
        IConsumer<PaymentSucceeded>,
        IConsumer<PaymentFailed>
    {
        private readonly IBookingService _bookingService;
        private readonly IBookedSeatsCache _bookedSeatsCache;
        private readonly ILogger<BookingFinalizerConsumer> _logger;

        public BookingFinalizerConsumer(
            IBookingService bookingService,
            IBookedSeatsCache bookedSeatsCache,
            ILogger<BookingFinalizerConsumer> logger)
        {
            _bookingService = bookingService;
            _bookedSeatsCache = bookedSeatsCache;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<PaymentSucceeded> context)
        {
            var msg = context.Message;
            _logger.LogInformation("Finalizing booking for Order {OrderId}", msg.OrderId);

            var seatPairs = msg.Seats.Select(s => (s.Row.ToString(), s.Col.ToString())).ToList();

            try
            {
                // 1. Persist to DB
                var result = await _bookingService.BookSeatsAsync(
                    msg.EventId,
                    msg.UserId,
                    seatPairs,
                    CancellationToken.None);

                if (result.IsSuccess)
                {
                    // 2. Make Redis Lock Permanent (Remove TTL)
                    // We use AddBookedSeatsAsync which sets a long expiration (24h)
                    await _bookedSeatsCache.AddBookedSeatsAsync(
                        msg.EventId,
                        seatPairs,
                        CancellationToken.None);
                    
                    _logger.LogInformation("Booking confirmed for Order {OrderId}", msg.OrderId);
                }
                else
                {
                    _logger.LogError("DB Booking failed for Order {OrderId}: {Error}", msg.OrderId, result.GetErrorOrThrow());
                    // Compensation: Release Redis Lock
                    await _bookedSeatsCache.ReleaseSeatsAsync(
                        msg.EventId,
                        seatPairs,
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error finalizing booking for Order {OrderId}", msg.OrderId);
                // Retry? Or Dead Letter Queue?
                throw; // MassTransit will retry
            }
        }

        public async Task Consume(ConsumeContext<PaymentFailed> context)
        {
            var msg = context.Message;
            _logger.LogWarning("Payment failed for Order {OrderId}. Releasing seats.", msg.OrderId);

            var seatPairs = msg.Seats.Select(s => (s.Row.ToString(), s.Col.ToString())).ToList();

            // Compensation: Release Redis Lock
            await _bookedSeatsCache.ReleaseSeatsAsync(
                msg.EventId,
                seatPairs,
                CancellationToken.None);
        }
    }
}
