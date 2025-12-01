using SeatGrid.API.Application.DTOs.Responses;

namespace SeatGrid.API.Application.Interfaces;

public interface IEventService
{
    Task<EventResponse> CreateEventAsync(string name, DateTime date, int rows, int cols, CancellationToken cancellationToken);
    Task<IEnumerable<SeatResponse>> GetEventSeatsAsync(long eventId, CancellationToken cancellationToken);
}
