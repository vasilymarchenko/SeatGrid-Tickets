using SeatGrid.API.Application.Interfaces;
using StackExchange.Redis;

namespace SeatGrid.API.Application.Services;

/// <summary>
/// Redis-based implementation of booked seats cache using Redis HASH data structure.
/// Supports atomic "Check-and-Set" operations via Lua scripting to prevent race conditions.
/// </summary>
public class BookedSeatsCache : IBookedSeatsCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<BookedSeatsCache> _logger;

    // Cache key pattern: "event:{eventId}:seats"
    private const string KeyPrefix = "event";
    private const string KeySuffix = "seats";

    // Lua script for atomic reservation
    // KEYS[1] = key
    // ARGV[1] = timestamp
    // ARGV[2...] = seat keys (Row-Col)
    private const string ReserveScript = @"
        local key = KEYS[1]
        local timestamp = ARGV[1]
        local seats = {}

        -- Parse arguments (starting from 2) into a table
        for i = 2, #ARGV do
            table.insert(seats, ARGV[i])
        end

        -- 1. Check if ANY requested seat is already taken
        for _, seat in ipairs(seats) do
            if redis.call('HEXISTS', key, seat) == 1 then
                return 0 -- Fail: At least one seat is taken
            end
        end

        -- 2. If all free, reserve ALL of them
        for _, seat in ipairs(seats) do
            redis.call('HSET', key, seat, timestamp)
        end

        -- Set expiration if key didn't exist (24h)
        if redis.call('TTL', key) == -1 then
            redis.call('EXPIRE', key, 86400)
        end

        return 1 -- Success
    ";

    public BookedSeatsCache(
        IConnectionMultiplexer redis,
        ILogger<BookedSeatsCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<HashSet<string>> GetBookedSeatKeysAsync(long eventId, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var key = GetCacheKey(eventId);

        // HKEYS returns all field names in the hash
        var members = await db.HashKeysAsync(key);

        var bookedSeats = members
            .Select(m => m.ToString())
            .ToHashSet();

        _logger.LogDebug("Cache: Event {EventId} has {Count} booked seats in cache", 
            eventId, bookedSeats.Count);

        return bookedSeats;
    }

    public async Task AddBookedSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken cancellationToken)
    {
        if (seats == null || !seats.Any())
            return;

        try
        {
            var db = _redis.GetDatabase();
            var key = GetCacheKey(eventId);
            var timestamp = DateTime.UtcNow.Ticks;

            // Convert to HashEntries
            var entries = seats
                .Select(s => new HashEntry($"{s.Row}-{s.Col}", timestamp))
                .ToArray();

            await db.HashSetAsync(key, entries);
            await db.KeyExpireAsync(key, TimeSpan.FromHours(24));

            _logger.LogDebug("Cache: Added {Count} booked seats to event {EventId}", seats.Count, eventId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add booked seats for event {EventId} to cache", eventId);
        }
    }

    public async Task<bool> IsSeatBookedAsync(long eventId, string row, string col, CancellationToken cancellationToken)
    {
        var db = _redis.GetDatabase();
        var key = GetCacheKey(eventId);
        var seatKey = $"{row}-{col}";

        return await db.HashExistsAsync(key, seatKey);
    }

    public async Task<bool> TryReserveSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken cancellationToken)
    {
        if (seats == null || !seats.Any()) return false;

        try
        {
            var db = _redis.GetDatabase();
            var key = GetCacheKey(eventId);
            var timestamp = DateTime.UtcNow.Ticks;

            // Prepare arguments: [timestamp, seat1, seat2, ...]
            var args = new List<RedisValue> { timestamp };
            args.AddRange(seats.Select(s => (RedisValue)$"{s.Row}-{s.Col}"));

            var result = await db.ScriptEvaluateAsync(
                ReserveScript,
                new RedisKey[] { key },
                args.ToArray()
            );

            bool success = (int)result == 1;
            
            if (success)
            {
                _logger.LogDebug("Cache: Reserved {Count} seats for event {EventId}", seats.Count, eventId);
            }
            else
            {
                _logger.LogDebug("Cache: Failed to reserve seats for event {EventId} (conflict)", eventId);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis script error during reservation for event {EventId}", eventId);
            return false; // Fail safe
        }
    }

    public async Task ReleaseSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken cancellationToken)
    {
        if (seats == null || !seats.Any()) return;

        try
        {
            var db = _redis.GetDatabase();
            var key = GetCacheKey(eventId);

            var fields = seats
                .Select(s => (RedisValue)$"{s.Row}-{s.Col}")
                .ToArray();

            await db.HashDeleteAsync(key, fields);
            
            _logger.LogDebug("Cache: Released {Count} seats for event {EventId}", seats.Count, eventId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release seats for event {EventId}", eventId);
        }
    }

    public async Task<List<string>> GetStaleSeatKeysAsync(long eventId, TimeSpan staleThreshold, CancellationToken cancellationToken)
    {
        var staleKeys = new List<string>();
        try
        {
            var db = _redis.GetDatabase();
            var key = GetCacheKey(eventId);
            
            // Get all fields and values
            var allEntries = await db.HashGetAllAsync(key);
            var nowTicks = DateTime.UtcNow.Ticks;
            var thresholdTicks = staleThreshold.Ticks;

            foreach (var entry in allEntries)
            {
                if (entry.Value.TryParse(out long timestamp))
                {
                    // Check if the reservation is older than the threshold
                    if (nowTicks - timestamp > thresholdTicks)
                    {
                        staleKeys.Add(entry.Name.ToString());
                    }
                }
            }

            if (staleKeys.Any())
            {
                _logger.LogDebug("Cache: Found {Count} stale seats for event {EventId}", staleKeys.Count, eventId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for stale seats for event {EventId}", eventId);
        }

        return staleKeys;
    }

    private static string GetCacheKey(long eventId) => $"{KeyPrefix}:{eventId}:{KeySuffix}";
}
