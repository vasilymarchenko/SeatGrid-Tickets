namespace SeatGrid.API.Application.Interfaces;

/// <summary>
/// Cache service for tracking which seats are already booked per event.
/// Enables fast-path rejection when users attempt to book seats that are already taken.
/// This is a best-effort optimization - cache miss or stale data falls back to DB with optimistic locking.
/// </summary>
public interface IBookedSeatsCache
{
    /// <summary>
    /// Adds newly booked seats to the cache.
    /// Used for cache warm-up or manual synchronization.
    /// </summary>
    /// <param name="eventId">The event identifier</param>
    /// <param name="seats">List of seat row/column pairs that were booked</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task AddBookedSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically checks and reserves specific seats in the cache.
    /// If ANY of the requested seats are already present in the cache, the operation fails (returns false)
    /// and NO seats are reserved.
    /// If ALL seats are free, they are added to the cache with the current timestamp.
    /// </summary>
    /// <param name="eventId">The event identifier</param>
    /// <param name="seats">List of seat row/column pairs to reserve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if reservation succeeded (token acquired), False if any seat was already taken</returns>
    Task<bool> TryReserveSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken cancellationToken);

    /// <summary>
    /// Releases (removes) specific seats from the cache.
    /// Used for compensation when a DB transaction fails after a successful cache reservation.
    /// </summary>
    /// <param name="eventId">The event identifier</param>
    /// <param name="seats">List of seat row/column pairs to release</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ReleaseSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all seats that have been reserved in cache for longer than the specified threshold.
    /// Note: This returns ALL reservations (valid and invalid) older than the threshold.
    /// The caller is responsible for verifying against the DB to identify "Ghost Seats".
    /// </summary>
    /// <param name="eventId">The event identifier</param>
    /// <param name="staleThreshold">Time threshold for staleness</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of stale seat keys (Row-Col)</returns>
    Task<List<string>> GetStaleSeatKeysAsync(long eventId, TimeSpan staleThreshold, CancellationToken cancellationToken);
}
