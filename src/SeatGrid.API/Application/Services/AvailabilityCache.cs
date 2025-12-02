using Microsoft.Extensions.Caching.Distributed;
using SeatGrid.API.Application.Interfaces;
using StackExchange.Redis;

namespace SeatGrid.API.Application.Services;

/// <summary>
/// Redis-based implementation of availability cache.
/// Uses Redis string operations for atomic available count tracking.
/// </summary>
public class AvailabilityCache : IAvailabilityCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AvailabilityCache> _logger;
    
    // Cache key pattern: "event:{eventId}:available"
    private const string KeyPrefix = "event";
    private const string KeySuffix = "available";

    public AvailabilityCache(
        IConnectionMultiplexer redis,
        ILogger<AvailabilityCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<int?> GetAvailableCountAsync(long eventId, CancellationToken cancellationToken)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetCacheKey(eventId);
            
            var value = await db.StringGetAsync(key);
            
            if (value.HasValue && value.TryParse(out int count))
            {
                _logger.LogDebug("Cache HIT: Event {EventId} has {Count} available seats", eventId, count);
                return count;
            }

            _logger.LogDebug("Cache MISS: Event {EventId} not in cache", eventId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get available count for event {EventId} from cache", eventId);
            return null; // Fail gracefully - caller will query DB
        }
    }

    public async Task SetAvailableCountAsync(long eventId, int count, CancellationToken cancellationToken)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetCacheKey(eventId);
            
            // Set with 24-hour expiration (reasonable for most events)
            // In production, this should be event end time + grace period
            await db.StringSetAsync(key, count, TimeSpan.FromHours(24));
            
            _logger.LogDebug("Cache SET: Event {EventId} available count = {Count}", eventId, count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set available count for event {EventId} in cache", eventId);
            // Don't throw - cache failure shouldn't break the application
        }
    }

    public async Task<bool> DecrementAvailableCountAsync(long eventId, int delta, CancellationToken cancellationToken)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = GetCacheKey(eventId);
            
            // Redis DECRBY is atomic - safe for concurrent operations
            var newValue = await db.StringDecrementAsync(key, delta);
            
            _logger.LogDebug("Cache DECR: Event {EventId} decremented by {Delta}, new value = {NewValue}", 
                eventId, delta, newValue);
            
            // If value went negative (shouldn't happen with proper logic), reset to 0
            if (newValue < 0)
            {
                await db.StringSetAsync(key, 0);
                _logger.LogWarning("Event {EventId} available count went negative ({NewValue}), reset to 0", 
                    eventId, newValue);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrement available count for event {EventId} in cache", eventId);
            return false;
        }
    }

    private static string GetCacheKey(long eventId) => $"{KeyPrefix}:{eventId}:{KeySuffix}";
}
