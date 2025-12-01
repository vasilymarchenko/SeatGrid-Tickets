using SeatGrid.API.Domain.Entities;

namespace SeatGrid.API.Application.Interfaces;

public interface ISeatRepository
{
    Task<List<Seat>> GetSeatsForBookingAsync(long eventId, List<(string Row, string Col)> seatPairs, CancellationToken cancellationToken);
}
