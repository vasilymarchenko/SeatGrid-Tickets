using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Interfaces;

namespace SeatGrid.API.Application.Services;

/// <summary>
/// Background service that reconciles the Redis seat cache with the Database.
/// Fixes "Ghost Seats" (seats marked as reserved in Redis but not booked in DB)
/// caused by application crashes or network failures during the booking flow.
/// </summary>
public class CacheReconciliationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CacheReconciliationService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);
    private readonly TimeSpan _staleThreshold = TimeSpan.FromSeconds(30);

    public CacheReconciliationService(
        IServiceProvider serviceProvider,
        ILogger<CacheReconciliationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cache Reconciliation Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during cache reconciliation.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ReconcileAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var bookedSeatsCache = scope.ServiceProvider.GetRequiredService<IBookedSeatsCache>();
        var eventRepository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
        
        // Note: In a real production system, we would need a way to discover active event IDs efficiently.
        // For this demo, we'll assume we can scan active events or just iterate known ones.
        // Since IBookedSeatsCache doesn't expose "GetAllEventIds", we might need to extend it or 
        // rely on the repository to give us active events.
        
        // For now, let's fetch active events from DB
        var activeEvents = await eventRepository.GetActiveEventsAsync(stoppingToken);

        foreach (var evt in activeEvents)
        {
            await ReconcileEventAsync(evt.Id, bookedSeatsCache, eventRepository, stoppingToken);
        }
    }

    private async Task ReconcileEventAsync(
        long eventId, 
        IBookedSeatsCache cache, 
        IEventRepository repo, 
        CancellationToken ct)
    {
        // 1. Get all "stale" reservations from Redis
        // Note: This returns ALL reservations older than the threshold, including valid ones.
        // We must verify them against the DB to distinguish "Valid" vs "Ghost".
        var staleKeys = await cache.GetStaleSeatKeysAsync(eventId, _staleThreshold, ct);
        
        if (!staleKeys.Any())
        {
            return;
        }

        _logger.LogInformation("Found {Count} stale keys for event {EventId}. Verifying against DB...", staleKeys.Count, eventId);

        // 2. Optimization: Fetch only "Available" seats from DB.
        // If a seat is in Redis (Booked) but in DB (Available), it is a Ghost Seat.
        // This is more efficient than fetching all seats if the event is large.
        var availableSeats = await repo.GetAvailableSeatsAsync(eventId, ct);
        
        // Create a HashSet for fast lookup of available seats
        var availableSeatKeys = availableSeats
            .Select(s => $"{s.Row}-{s.Col}")
            .ToHashSet();
        
        var ghostSeats = new List<(string Row, string Col)>();

        foreach (var key in staleKeys)
        {
            // If the Redis key corresponds to an Available seat in DB, it's a ghost.
            if (availableSeatKeys.Contains(key))
            {
                var parts = key.Split('-');
                if (parts.Length == 2)
                {
                    ghostSeats.Add((parts[0], parts[1]));
                }
            }
        }

        // 3. Release ghost seats
        if (ghostSeats.Any())
        {
            _logger.LogWarning("Releasing {Count} ghost seats for event {EventId}: {Seats}", 
                ghostSeats.Count, eventId, string.Join(", ", ghostSeats.Select(s => $"{s.Row}-{s.Col}")));
            
            await cache.ReleaseSeatsAsync(eventId, ghostSeats, ct);
        }
        else
        {
            _logger.LogDebug("All {Count} stale keys were valid bookings (not found in Available seats).", staleKeys.Count);
        }
    }
}
