using SeatGrid.API.Domain.Entities;

namespace SeatGrid.API.Application.Interfaces;

public interface IBookingService
{
    Task<BookingResult> BookSeatsAsync(long eventId, string userId, List<(string Row, string Col)> seats, CancellationToken cancellationToken);
}

public record BookingResult(bool Success, string Message, object? Data = null);
