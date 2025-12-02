namespace SeatGrid.API.Application.Interfaces;

/// <summary>
/// Cache service for tracking available seat counts per event.
/// Enables fast-path rejection for sold-out events without database queries.
/// </summary>
public interface IAvailabilityCache
{
    /// <summary>
    /// Gets the available seat count for an event from cache.
    /// </summary>
    /// <param name="eventId">The event identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Available seat count, or null if not cached</returns>
    Task<int?> GetAvailableCountAsync(long eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the available seat count for an event in cache.
    /// </summary>
    /// <param name="eventId">The event identifier</param>
    /// <param name="count">The available seat count</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SetAvailableCountAsync(long eventId, int count, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically decrements the available seat count for an event.
    /// </summary>
    /// <param name="eventId">The event identifier</param>
    /// <param name="delta">The number of seats to decrement</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successful, false if key doesn't exist</returns>
    Task<bool> DecrementAvailableCountAsync(long eventId, int delta, CancellationToken cancellationToken);
}
