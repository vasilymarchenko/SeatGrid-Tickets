using SeatGrid.API.Domain.Entities;

namespace SeatGrid.API.Application.Interfaces;

public interface IEventRepository
{
    Task AddEventAsync(Event evt, CancellationToken cancellationToken);
    Task AddSeatsAsync(IEnumerable<Seat> seats, CancellationToken cancellationToken);
    Task<List<Seat>> GetSeatsByEventIdAsync(long eventId, CancellationToken cancellationToken);
}
