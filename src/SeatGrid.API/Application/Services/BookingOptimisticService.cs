using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Application.Common;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Domain.Enums;
using SeatGrid.API.Infrastructure.Persistence;

namespace SeatGrid.API.Application.Services;

/// <summary>
/// Optimistic locking booking implementation using EF Core's ConcurrencyCheck.
/// Leverages Status and CurrentHolderId as natural version indicators - no explicit timestamp needed.
/// Provides better throughput under low-to-medium contention by avoiding row locks.
/// </summary>
public class BookingOptimisticService : IBookingService
{
    private readonly SeatGridDbContext _context;
    private readonly IBookedSeatsCache _bookedSeatsCache;
    private readonly ILogger<BookingOptimisticService> _logger;

    public BookingOptimisticService(
        SeatGridDbContext context,
        IBookedSeatsCache bookedSeatsCache,
        ILogger<BookingOptimisticService> logger)
    {
        _context = context;
        _bookedSeatsCache = bookedSeatsCache;
        _logger = logger;
    }

    public async Task<Result<BookingSuccess, BookingError>> BookSeatsAsync(
        long eventId, 
        string userId, 
        List<(string Row, string Col)> seatPairs, 
        CancellationToken cancellationToken)
    {
        if (seatPairs == null || !seatPairs.Any())
        {
            return Result<BookingSuccess, BookingError>.Failure(
                new BookingError("No seats specified."));
        }

        var distinctSeatPairs = seatPairs.Distinct().ToList();

        try
        {
            // FAST-PATH: Check booked seats cache before hitting database
            var bookedSeatsInCache = await _bookedSeatsCache.GetBookedSeatKeysAsync(eventId, cancellationToken);
            
            if (bookedSeatsInCache.Any())
            {
                var alreadyBooked = distinctSeatPairs
                    .Where(p => bookedSeatsInCache.Contains($"{p.Row}-{p.Col}"))
                    .ToList();

                if (alreadyBooked.Any())
                {
                    _logger.LogDebug("Fast-path rejection: {Count} seats already booked (cache hit)", alreadyBooked.Count);
                    
                    return Result<BookingSuccess, BookingError>.Failure(
                        new BookingError("One or more seats are already booked (cached).", new
                        {
                            AlreadyBooked = alreadyBooked.Select(s => new { s.Row, s.Col })
                        }));
                }
            }

            // Read seats WITHOUT locking - optimistic approach assumes low contention
            var seats = await GetSeatsAsync(eventId, distinctSeatPairs, cancellationToken);

            // Validate seat count
            if (seats.Count != distinctSeatPairs.Count)
            {
                var foundPairs = seats.Select(s => (s.Row, s.Col)).ToHashSet();
                var missingSeats = distinctSeatPairs.Where(p => !foundPairs.Contains(p)).ToList();

                return Result<BookingSuccess, BookingError>.Failure(
                    new BookingError("One or more seats do not exist.", new { MissingSeats = missingSeats }));
            }

            // Check availability in-memory (snapshot at read time)
            var unavailableSeats = seats
                .Where(s => s.Status != SeatStatus.Available || s.CurrentHolderId != null)
                .ToList();

            if (unavailableSeats.Any())
            {
                return Result<BookingSuccess, BookingError>.Failure(
                    new BookingError("One or more seats are already booked.", new
                    {
                        UnavailableSeats = unavailableSeats.Select(s => new
                        {
                            s.Row,
                            s.Col,
                            s.Status,
                            BookedBy = s.CurrentHolderId
                        })
                    }));
            }

            // Optimistically update seats
            // EF Core will generate: UPDATE Seats SET Status = @p0, CurrentHolderId = @p1 
            // WHERE Id = @p2 AND Status = @p3 AND CurrentHolderId = @p4
            // Thanks to [ConcurrencyCheck] attributes on Status and CurrentHolderId
            foreach (var seat in seats)
            {
                seat.Status = SeatStatus.Booked;
                seat.CurrentHolderId = userId;
            }

            // Attempt to save - will throw DbUpdateConcurrencyException if any seat was modified
            var affectedRows = await _context.SaveChangesAsync(cancellationToken);

            // Verify all seats were updated (defensive check)
            if (affectedRows != seats.Count)
            {
                // This shouldn't happen if ConcurrencyCheck is working correctly,
                // but provides extra safety
                return Result<BookingSuccess, BookingError>.Failure(
                    new BookingError("Booking conflict detected. Some seats may have been modified by another transaction."));
            }

            // Update cache with newly booked seats (best-effort, fire-and-forget style)
            await _bookedSeatsCache.AddBookedSeatsAsync(eventId, distinctSeatPairs, cancellationToken);
            
            _logger.LogDebug("Successfully booked {Count} seats, cache updated", seats.Count);

            return Result<BookingSuccess, BookingError>.Success(
                new BookingSuccess(seats.Count));
        }
        catch (DbUpdateConcurrencyException)
        {
            // Concurrency conflict: Someone else modified Status or CurrentHolderId between our read and update
            // This is the expected optimistic locking behavior under contention
            return Result<BookingSuccess, BookingError>.Failure(
                new BookingError("Booking conflict: One or more seats were modified by another user. Please try again."));
        }
    }

    private async Task<List<Seat>> GetSeatsAsync(long eventId, List<(string Row, string Col)> seatPairs, CancellationToken cancellationToken)
    {
        // Build VALUES clause for tuple IN with proper escaping
        var valuesClause = string.Join(", ",
            seatPairs.Select(p => $"('{p.Row.Replace("'", "''")}', '{p.Col.Replace("'", "''")}')"));

        // Fetch seats using PostgreSQL tuple IN syntax (without locking for optimistic approach)
        // Note: valuesClause is built with SQL-escaped strings (single quotes doubled)
        // The eventId parameter is properly parameterized via {0}
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection
        return await _context.Seats
            .FromSqlRaw($@"
                SELECT * FROM ""Seats""
                WHERE ""EventId"" = {{0}}
                  AND (""Row"", ""Col"") IN ({valuesClause})",
                    eventId)
            .ToListAsync(cancellationToken);
#pragma warning restore EF1002 // Risk of vulnerability to SQL injection
    }
}
