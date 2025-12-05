using SeatGrid.API.Application.DTOs.Responses;

namespace SeatGrid.API.Application.Interfaces;

public interface IEventService
{
    Task<EventResponse> CreateEventAsync(string name, DateTime date, int rows, int cols, CancellationToken cancellationToken);
    Task<IEnumerable<SeatResponse>> GetEventSeatsAsync(long eventId, CancellationToken cancellationToken);
    
    /// <summary>
    /// Warms up the Redis cache by loading all currently booked seats from the Database.
    /// Useful after system restart or Redis flush.
    /// </summary>
    Task WarmupCacheAsync(long eventId, CancellationToken cancellationToken);
}
