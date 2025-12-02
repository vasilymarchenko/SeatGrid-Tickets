namespace SeatGrid.API.Application.Interfaces;

/// <summary>
/// Cache service for tracking which seats are already booked per event.
/// Enables fast-path rejection when users attempt to book seats that are already taken.
/// This is a best-effort optimization - cache miss or stale data falls back to DB with optimistic locking.
/// </summary>
public interface IBookedSeatsCache
{
    /// <summary>
    /// Gets all booked seat keys for an event.
    /// </summary>
    /// <param name="eventId">The event identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Set of booked seat keys in format "Row-Col", or empty set if not cached</returns>
    Task<HashSet<string>> GetBookedSeatKeysAsync(long eventId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds newly booked seats to the cache.
    /// </summary>
    /// <param name="eventId">The event identifier</param>
    /// <param name="seats">List of seat row/column pairs that were booked</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddBookedSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken cancellationToken);

    /// <summary>
    /// Checks if a specific seat is marked as booked in cache.
    /// Note: For checking multiple seats, prefer GetBookedSeatKeysAsync() + in-memory filtering
    /// to avoid multiple Redis round-trips.
    /// </summary>
    /// <param name="eventId">The event identifier</param>
    /// <param name="row">Seat row</param>
    /// <param name="col">Seat column</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if seat is in booked cache, false otherwise</returns>
    Task<bool> IsSeatBookedAsync(long eventId, string row, string col, CancellationToken cancellationToken);
}
