using System.Diagnostics;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Application.Observability;

namespace SeatGrid.API.Application.Decorators;

/// <summary>
/// Decorator for IAvailabilityCache that adds observability metrics.
/// Tracks cache hit/miss ratios, operation durations, and database query avoidance.
/// Uses the Decorator pattern to add cross-cutting concerns without modifying business logic.
/// </summary>
public class InstrumentedAvailabilityCache : IAvailabilityCache
{
    private readonly IAvailabilityCache _inner;
    private readonly ILogger<InstrumentedAvailabilityCache> _logger;
    private const string CacheType = "availability";

    public InstrumentedAvailabilityCache(
        IAvailabilityCache inner,
        ILogger<InstrumentedAvailabilityCache> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<int?> GetAvailableCountAsync(long eventId, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.GetAvailableCountAsync(eventId, cancellationToken);
            
            // Record cache hit or miss
            if (result.HasValue)
            {
                BookingMetrics.RecordCacheHit(CacheType);
                _logger.LogDebug("Cache HIT for availability check on event {EventId}, count: {Count}", 
                    eventId, result.Value);
            }
            else
            {
                BookingMetrics.RecordCacheMiss(CacheType);
                _logger.LogDebug("Cache MISS for availability check on event {EventId}, key not found", eventId);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            // Redis error: Record as miss and return null for graceful degradation
            try
            {
                BookingMetrics.RecordCacheCheck(CacheType, "error");
            }
            catch
            {
                // Ignore metric recording failures - don't let observability crash the app
            }
            
            _logger.LogWarning(ex, "Cache MISS (error) for availability on event {EventId}, returning null", eventId);
            
            // Return null to trigger database fallback in caller
            return null;
        }
        finally
        {
            sw.Stop();
            BookingMetrics.RecordCacheOperationDuration(sw.Elapsed.TotalMilliseconds, CacheType, "get");
        }
    }

    public async Task SetAvailableCountAsync(long eventId, int count, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.SetAvailableCountAsync(eventId, count, cancellationToken);
        }
        finally
        {
            sw.Stop();
            BookingMetrics.RecordCacheOperationDuration(sw.Elapsed.TotalMilliseconds, CacheType, "set");
        }
    }

    public async Task<bool> DecrementAvailableCountAsync(long eventId, int delta, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await _inner.DecrementAvailableCountAsync(eventId, delta, cancellationToken);
        }
        finally
        {
            sw.Stop();
            BookingMetrics.RecordCacheOperationDuration(sw.Elapsed.TotalMilliseconds, CacheType, "decrement");
        }
    }
}
