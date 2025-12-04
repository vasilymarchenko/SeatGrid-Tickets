using System.Diagnostics.Metrics;

namespace SeatGrid.API.Application.Observability;

/// <summary>
/// Custom metrics for booking and cache operations.
/// Provides observability into cache effectiveness and database query reduction.
/// </summary>
public static class BookingMetrics
{
    private static readonly Meter Meter = new("SeatGrid.API", "1.0.0");

    // Cache operation counters
    private static readonly Counter<long> CacheChecks = 
        Meter.CreateCounter<long>(
            "seatgrid.booking.cache_checks",
            unit: "checks",
            description: "Number of booking cache checks");

    // Database query counters
    private static readonly Counter<long> DatabaseQueries = 
        Meter.CreateCounter<long>(
            "seatgrid.booking.db_queries",
            unit: "queries",
            description: "Number of booking database queries");

    // Booking request duration histogram
    private static readonly Histogram<double> BookingDuration = 
        Meter.CreateHistogram<double>(
            "seatgrid.booking.duration",
            unit: "ms",
            description: "Booking request duration in milliseconds");

    // Cache operation duration histogram
    private static readonly Histogram<double> CacheOperationDuration = 
        Meter.CreateHistogram<double>(
            "seatgrid.cache.operation_duration",
            unit: "ms",
            description: "Cache operation duration in milliseconds");

    /// <summary>
    /// Records a cache hit event.
    /// </summary>
    /// <param name="cacheType">Type of cache (e.g., "availability", "booked_seats")</param>
    /// <param name="resultType">Type of result: "found" (useful data), "empty" (no data), "error" (cache failure)</param>
    public static void RecordCacheCheck(string cacheType, string resultType)
    {
        CacheChecks.Add(1,
            new KeyValuePair<string, object?>("cache_type", cacheType),
            new KeyValuePair<string, object?>("result", resultType));
    }

    /// <summary>
    /// Records a cache hit event (backward compatibility).
    /// </summary>
    /// <param name="cacheType">Type of cache (e.g., "availability", "booked_seats")</param>
    public static void RecordCacheHit(string cacheType)
    {
        RecordCacheCheck(cacheType, "hit");
    }

    /// <summary>
    /// Records a cache miss event (backward compatibility).
    /// </summary>
    /// <param name="cacheType">Type of cache (e.g., "availability", "booked_seats")</param>
    public static void RecordCacheMiss(string cacheType)
    {
        RecordCacheCheck(cacheType, "miss");
    }

    /// <summary>
    /// Records a database query event.
    /// </summary>
    /// <param name="queryType">Type of query (e.g., "availability", "seat_lookup")</param>
    public static void RecordDatabaseQuery(string queryType)
    {
        DatabaseQueries.Add(1,
            new KeyValuePair<string, object?>("query_type", queryType));
    }

    /// <summary>
    /// Records booking request duration.
    /// </summary>
    /// <param name="durationMs">Duration in milliseconds</param>
    /// <param name="outcome">Outcome of the booking (e.g., "success", "conflict_cache", "conflict_db")</param>
    public static void RecordBookingDuration(double durationMs, string outcome)
    {
        BookingDuration.Record(durationMs,
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    /// <summary>
    /// Records cache operation duration.
    /// </summary>
    /// <param name="durationMs">Duration in milliseconds</param>
    /// <param name="cacheType">Type of cache</param>
    /// <param name="operation">Operation name (e.g., "get", "set", "increment")</param>
    public static void RecordCacheOperationDuration(double durationMs, string cacheType, string operation)
    {
        CacheOperationDuration.Record(durationMs,
            new KeyValuePair<string, object?>("cache_type", cacheType),
            new KeyValuePair<string, object?>("operation", operation));
    }
}
