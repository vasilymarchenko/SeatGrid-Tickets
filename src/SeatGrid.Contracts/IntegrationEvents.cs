using System;

namespace SeatGrid.Contracts
{
    public record BookingInitiated
    {
        public required Guid OrderId { get; init; }
        public required long EventId { get; init; }
        public required List<SeatLocation> Seats { get; init; }
        public required string UserId { get; init; }
        public required DateTime CreatedAt { get; init; }
    }

    public record SeatLocation(int Row, int Col);

    public record PaymentSucceeded
    {
        public required Guid OrderId { get; init; }
        public required long EventId { get; init; }
        public required List<SeatLocation> Seats { get; init; }
        public required string UserId { get; init; }
        public required DateTime ProcessedAt { get; init; }
    }

    public record PaymentFailed
    {
        public required Guid OrderId { get; init; }
        public required long EventId { get; init; }
        public required List<SeatLocation> Seats { get; init; }
        public required string Reason { get; init; }
        public required DateTime ProcessedAt { get; init; }
    }
}
