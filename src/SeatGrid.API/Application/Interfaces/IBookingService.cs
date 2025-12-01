using SeatGrid.API.Application.Common;

namespace SeatGrid.API.Application.Interfaces;

public interface IBookingService
{
    Task<Result<BookingSuccess, BookingError>> BookSeatsAsync(long eventId, string userId, List<(string Row, string Col)> seats, CancellationToken cancellationToken);
}

public record BookingSuccess(int SeatCount);

public record BookingError(string Message, object? Details = null);
