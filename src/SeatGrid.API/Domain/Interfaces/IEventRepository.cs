using SeatGrid.API.Domain.Entities;

namespace SeatGrid.API.Domain.Interfaces;

public interface IEventRepository
{
    Task AddEventAsync(Event evt, CancellationToken cancellationToken);
    Task<List<Event>> GetActiveEventsAsync(CancellationToken cancellationToken);
    Task AddSeatsAsync(IEnumerable<Seat> seats, CancellationToken cancellationToken);
    Task<List<Seat>> GetSeatsByEventIdAsync(long eventId, CancellationToken cancellationToken);
    Task<List<Seat>> GetAvailableSeatsAsync(long eventId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
