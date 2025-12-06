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
