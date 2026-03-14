using MassTransit;
using SeatGrid.Contracts;

namespace SeatGrid.PaymentService
{
    public class PaymentConsumer : IConsumer<BookingInitiated>
    {
        private readonly ILogger<PaymentConsumer> _logger;
        private readonly Random _random = new Random();

        public PaymentConsumer(ILogger<PaymentConsumer> logger)
        {
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<BookingInitiated> context)
        {
            var orderId = context.Message.OrderId;
            _logger.LogInformation("Processing payment for Order {OrderId}", orderId);

            // Simulate processing delay (2 seconds)
            await Task.Delay(2000);

            // Simulate 15% failure rate
            if (_random.NextDouble() < 0.15)
            {
                _logger.LogWarning("Payment failed for Order {OrderId}", orderId);
                await context.Publish(new PaymentFailed
                {
                    OrderId = orderId,
                    EventId = context.Message.EventId,
                    Seats = context.Message.Seats,
                    Reason = "Card Declined (Simulated)",
                    ProcessedAt = DateTime.UtcNow
                });
            }
            else
            {
                _logger.LogInformation("Payment succeeded for Order {OrderId}", orderId);
                await context.Publish(new PaymentSucceeded
                {
                    OrderId = orderId,
                    EventId = context.Message.EventId,
                    Seats = context.Message.Seats,
                    UserId = context.Message.UserId,
                    ProcessedAt = DateTime.UtcNow
                });
            }
        }
    }
}
