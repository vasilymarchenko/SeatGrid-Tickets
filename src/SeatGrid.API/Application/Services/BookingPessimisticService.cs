using Microsoft.EntityFrameworkCore;
using Npgsql;
using SeatGrid.API.Application.Common;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Domain.Enums;
using SeatGrid.API.Infrastructure.Persistence;
using System.Data;

namespace SeatGrid.API.Application.Services;

/// <summary>
/// Pessimistic locking booking implementation using PostgreSQL FOR UPDATE NOWAIT.
/// Provides strong consistency guarantees under high concurrency by explicitly locking rows.
/// </summary>
public class BookingPessimisticService : IBookingService
{
    private readonly SeatGridDbContext _context;

    public BookingPessimisticService(SeatGridDbContext context)
    {
        _context = context;
    }

    public async Task<Result<BookingSuccess, BookingError>> BookSeatsAsync(long eventId, string userId, List<(string Row, string Col)> seatPairs, CancellationToken cancellationToken)
    {
        if (seatPairs == null || !seatPairs.Any())
        {
            return Result<BookingSuccess, BookingError>.Failure(
                new BookingError("No seats specified."));
        }

        var distinctSeatPairs = seatPairs.Distinct().ToList();

        using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            // Fetch and lock seats with PostgreSQL pessimistic locking
            var seats = await GetSeatsForBookingAsync(eventId, distinctSeatPairs, cancellationToken);

            // Validate seat count
            if (seats.Count != distinctSeatPairs.Count)
            {
                var foundPairs = seats.Select(s => (s.Row, s.Col)).ToHashSet();
                var missingSeats = distinctSeatPairs.Where(p => !foundPairs.Contains(p)).ToList();

                return Result<BookingSuccess, BookingError>.Failure(
                    new BookingError("One or more seats do not exist.", new { MissingSeats = missingSeats }));
            }

            // Check availability (Idempotency: Allow if already booked by same user)
            var unavailableSeats = seats
                .Where(s => s.Status != SeatStatus.Available && s.CurrentHolderId != userId)
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

            // Book the seats: Update Status
            foreach (var seat in seats)
            {
                // Idempotency check: If already booked by us, skip update
                if (seat.Status == SeatStatus.Booked && seat.CurrentHolderId == userId)
                {
                    continue;
                }

                seat.Status = SeatStatus.Booked;
                seat.CurrentHolderId = userId;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Result<BookingSuccess, BookingError>.Success(
                new BookingSuccess(seats.Count));
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<List<Seat>> GetSeatsForBookingAsync(long eventId, List<(string Row, string Col)> seatPairs, CancellationToken cancellationToken)
    {
        // Build VALUES clause for tuple IN with proper escaping
        var valuesClause = string.Join(", ",
            seatPairs.Select(p => $"('{p.Row.Replace("'", "''")}', '{p.Col.Replace("'", "''")}')"));

        // Fetch and lock seats in one query using PostgreSQL row-level locking
        // Removed NOWAIT: In a background Saga consumer, we prefer waiting for the lock
        // rather than failing immediately. This ensures higher success rates for paid orders.
        // Note: valuesClause is built with SQL-escaped strings (single quotes doubled)
        // The eventId parameter is properly parameterized via {0}
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection
        return await _context.Seats
            .FromSqlRaw($@"
                SELECT * FROM ""Seats""
                WHERE ""EventId"" = {{0}}
                  AND (""Row"", ""Col"") IN ({valuesClause})
                FOR UPDATE",
                    eventId)
            .ToListAsync(cancellationToken);
#pragma warning restore EF1002 // Risk of vulnerability to SQL injection
    }
}
