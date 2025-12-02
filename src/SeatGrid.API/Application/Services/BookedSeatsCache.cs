using SeatGrid.API.Application.Interfaces;
using StackExchange.Redis;

namespace SeatGrid.API.Application.Services;

/// <summary>
/// Redis-based implementation of booked seats cache using Redis SET data structure.
/// Provides O(1) membership checking for fast conflict detection.
/// This is a best-effort cache - staleness is acceptable as DB optimistic lock is the authority.
/// </summary>
public class BookedSeatsCache : IBookedSeatsCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<BookedSeatsCache> _logger;

    // Cache key pattern: "event:{eventId}:booked"
    private const string KeyPrefix = "event";
    private const string KeySuffix = "booked";

    public BookedSeatsCache(
        IConnectionMultiplexer redis,
        ILogger<BookedSeatsCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<HashSet<string>> GetBookedSeatKeysAsync(long eventId, CancellationToken cancellationToken)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetCacheKey(eventId);

            // Redis SMEMBERS returns all members of a set
            var members = await db.SetMembersAsync(key);

            var bookedSeats = members
                .Select(m => m.ToString())
                .ToHashSet();

            _logger.LogDebug("Cache: Event {EventId} has {Count} booked seats in cache", 
                eventId, bookedSeats.Count);

            return bookedSeats;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get booked seats for event {EventId} from cache", eventId);
            return new HashSet<string>(); // Fail gracefully - return empty set, caller will query DB
        }
    }

    public async Task AddBookedSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken cancellationToken)
    {
        if (seats == null || !seats.Any())
            return;

        try
        {
            var db = _redis.GetDatabase();
            var key = GetCacheKey(eventId);

            // Convert seat pairs to Redis values: "Row-Col"
            var redisValues = seats
                .Select(s => (RedisValue)$"{s.Row}-{s.Col}")
                .ToArray();

            // Redis SADD adds members to a set (idempotent - duplicates ignored)
            var addedCount = await db.SetAddAsync(key, redisValues);

            // Set expiration on first add
            await db.KeyExpireAsync(key, TimeSpan.FromHours(24));

            _logger.LogDebug("Cache: Added {Count} booked seats to event {EventId} (new members: {Added})", 
                seats.Count, eventId, addedCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add booked seats for event {EventId} to cache", eventId);
            // Don't throw - cache update failure shouldn't break booking flow
        }
    }

    /// <summary>
    /// Note: This method is currently unused in favor of GetBookedSeatKeysAsync() for bulk checking.
    /// Rationale: When booking multiple seats (e.g., 2-5 seats), it's more efficient to fetch the 
    /// entire booked set once (1 Redis call via SMEMBERS) and check in-memory, rather than making 
    /// N separate Redis calls (N × SISMEMBER). For typical event sizes (100 seats), fetching all 
    /// is faster than multiple network round-trips. Kept for API completeness and potential future 
    /// use cases (admin tools, single-seat availability checks, etc.).
    /// </summary>
    public async Task<bool> IsSeatBookedAsync(long eventId, string row, string col, CancellationToken cancellationToken)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetCacheKey(eventId);
            var seatKey = $"{row}-{col}";

            // Redis SISMEMBER checks if value is member of set - O(1) operation
            var isBooked = await db.SetContainsAsync(key, seatKey);

            _logger.LogDebug("Cache: Seat {SeatKey} in event {EventId} is {Status}", 
                seatKey, eventId, isBooked ? "BOOKED" : "NOT CACHED");

            return isBooked;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check if seat {Row}-{Col} is booked for event {EventId}", 
                row, col, eventId);
            return false; // Fail gracefully - return "not booked", let DB be the authority
        }
    }

    private static string GetCacheKey(long eventId) => $"{KeyPrefix}:{eventId}:{KeySuffix}";
}
