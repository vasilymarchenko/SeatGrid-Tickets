using SeatGrid.API.Domain.Entities;

namespace SeatGrid.API.Application.Interfaces;

public interface IEventService
{
    Task<Event> CreateEventAsync(string name, DateTime date, int rows, int cols, CancellationToken cancellationToken);
    Task<IEnumerable<object>> GetEventSeatsAsync(long eventId, CancellationToken cancellationToken);
}
