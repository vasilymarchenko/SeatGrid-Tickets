using Microsoft.EntityFrameworkCore;
using Npgsql;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Domain.Enums;
using SeatGrid.API.Infrastructure.Persistence;
using System.Data;

namespace SeatGrid.API.Application.Services;

public class BookingService : IBookingService
{
    private readonly SeatGridDbContext _context;

    public BookingService(SeatGridDbContext context)
    {
        _context = context;
    }

    public async Task<BookingResult> BookSeatsAsync(long eventId, string userId, List<(string Row, string Col)> seatPairs, CancellationToken cancellationToken)
    {
        if (seatPairs == null || !seatPairs.Any())
        {
            return new BookingResult(false, "No seats specified.");
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

                return new BookingResult(false, "One or more seats do not exist.", new { MissingSeats = missingSeats });
            }

            // Check availability
            var unavailableSeats = seats
                .Where(s => s.Status != SeatStatus.Available)
                .ToList();

            if (unavailableSeats.Any())
            {
                return new BookingResult(false, "One or more seats are already booked.", new
                {
                    UnavailableSeats = unavailableSeats.Select(s => new
                    {
                        s.Row,
                        s.Col,
                        s.Status,
                        BookedBy = s.CurrentHolderId
                    })
                });
            }

            // Book the seats: Update Status
            foreach (var seat in seats)
            {
                seat.Status = SeatStatus.Booked;
                seat.CurrentHolderId = userId;
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new BookingResult(true, "Booking successful", new { SeatCount = seats.Count });
        }
        catch (PostgresException ex) when (ex.SqlState == "55P03") // Lock not available
        {
            await transaction.RollbackAsync(cancellationToken);
            return new BookingResult(false, "Seats are currently locked by another transaction. Please try again.");
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
        // Note: valuesClause is built with SQL-escaped strings (single quotes doubled)
        // The eventId parameter is properly parameterized via {0}
#pragma warning disable EF1002 // Risk of vulnerability to SQL injection
        return await _context.Seats
            .FromSqlRaw($@"
                SELECT * FROM ""Seats""
                WHERE ""EventId"" = {{0}}
                  AND (""Row"", ""Col"") IN ({valuesClause})
                FOR UPDATE NOWAIT",
                    eventId)
            .ToListAsync(cancellationToken);
#pragma warning restore EF1002 // Risk of vulnerability to SQL injection
    }
}
