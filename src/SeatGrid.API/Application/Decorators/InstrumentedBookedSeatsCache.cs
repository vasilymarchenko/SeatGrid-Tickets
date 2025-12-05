using System.Diagnostics;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Application.Observability;

namespace SeatGrid.API.Application.Decorators;

/// <summary>
/// Decorator for IBookedSeatsCache that adds observability metrics.
/// Tracks cache effectiveness, operation durations, and conflict detection performance.
/// Uses the Decorator pattern to add cross-cutting concerns without modifying business logic.
/// </summary>
public class InstrumentedBookedSeatsCache : IBookedSeatsCache
{
    private readonly IBookedSeatsCache _inner;
    private readonly ILogger<InstrumentedBookedSeatsCache> _logger;
    private const string CacheType = "booked_seats";

    public InstrumentedBookedSeatsCache(
        IBookedSeatsCache inner,
        ILogger<InstrumentedBookedSeatsCache> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task<HashSet<string>> GetBookedSeatKeysAsync(long eventId, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.GetBookedSeatKeysAsync(eventId, cancellationToken);
            
            if (result.Any())
            {
                // Cache provided useful data - definite hit
                BookingMetrics.RecordCacheCheck(CacheType, "found");
                _logger.LogDebug("Cache HIT (found) for booked seats on event {EventId}, {Count} seats", 
                    eventId, result.Count);
            }
            else
            {
                // Empty response - caller may still need to query DB
                // Track separately so we can measure business effectiveness
                BookingMetrics.RecordCacheCheck(CacheType, "empty");
                _logger.LogDebug("Cache HIT (empty) for booked seats on event {EventId}", eventId);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            // Redis unavailable - true miss
            try
            {
                BookingMetrics.RecordCacheCheck(CacheType, "error");
            }
            catch
            {
                // Ignore metric recording failures
            }
            
            _logger.LogWarning(ex, "Cache MISS (error) for booked seats on event {EventId}, returning empty set", 
                eventId);
            
            return new HashSet<string>();
        }
        finally
        {
            sw.Stop();
            BookingMetrics.RecordCacheOperationDuration(sw.Elapsed.TotalMilliseconds, CacheType, "get_bulk");
        }
    }

    public async Task AddBookedSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.AddBookedSeatsAsync(eventId, seats, cancellationToken);
        }
        finally
        {
            sw.Stop();
            BookingMetrics.RecordCacheOperationDuration(sw.Elapsed.TotalMilliseconds, CacheType, "add");
        }
    }

    public async Task<bool> IsSeatBookedAsync(long eventId, string row, string col, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.IsSeatBookedAsync(eventId, row, col, cancellationToken);
            
            // Cache hit: Successfully checked Redis (regardless of result)
            BookingMetrics.RecordCacheHit(CacheType);
            
            if (result)
            {
                _logger.LogDebug("Seat {Row}-{Col} is booked in event {EventId}", row, col, eventId);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            // True cache miss: Redis unavailable
            try
            {
                BookingMetrics.RecordCacheCheck(CacheType, "error");
            }
            catch
            {
                // Ignore metric recording failures - don't let observability crash the app
            }
            
            _logger.LogWarning(ex, "Cache MISS (error) checking seat {Row}-{Col} for event {EventId}, assuming not booked", 
                row, col, eventId);
            
            // Graceful degradation: Assume not booked, let DB be the authority
            return false;
        }
        finally
        {
            sw.Stop();
            BookingMetrics.RecordCacheOperationDuration(sw.Elapsed.TotalMilliseconds, CacheType, "check_single");
        }
    }

    public async Task<bool> TryReserveSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.TryReserveSeatsAsync(eventId, seats, cancellationToken);
            
            if (result)
            {
                BookingMetrics.RecordCacheCheck(CacheType, "reserved");
            }
            else
            {
                BookingMetrics.RecordCacheCheck(CacheType, "conflict");
            }
            
            return result;
        }
        finally
        {
            sw.Stop();
            BookingMetrics.RecordCacheOperationDuration(sw.Elapsed.TotalMilliseconds, CacheType, "try_reserve");
        }
    }

    public async Task ReleaseSeatsAsync(long eventId, List<(string Row, string Col)> seats, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _inner.ReleaseSeatsAsync(eventId, seats, cancellationToken);
            BookingMetrics.RecordCacheCheck(CacheType, "released");
        }
        finally
        {
            sw.Stop();
            BookingMetrics.RecordCacheOperationDuration(sw.Elapsed.TotalMilliseconds, CacheType, "release");
        }
    }

    public async Task<List<string>> GetStaleSeatKeysAsync(long eventId, TimeSpan staleThreshold, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await _inner.GetStaleSeatKeysAsync(eventId, staleThreshold, cancellationToken);
            
            if (result.Any())
            {
                BookingMetrics.RecordCacheCheck(CacheType, "stale_found");
            }
            
            return result;
        }
        finally
        {
            sw.Stop();
            BookingMetrics.RecordCacheOperationDuration(sw.Elapsed.TotalMilliseconds, CacheType, "get_stale");
        }
    }
}
