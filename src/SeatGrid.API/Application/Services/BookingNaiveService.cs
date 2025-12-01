using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Application.Common;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Domain.Enums;
using SeatGrid.API.Infrastructure.Persistence;

namespace SeatGrid.API.Application.Services;

/// <summary>
/// Naive booking implementation using basic transaction isolation without explicit row-level locking.
/// Suitable for low-concurrency scenarios or as a baseline for comparison.
/// </summary>
public class BookingNaiveService : IBookingService
{
    private readonly SeatGridDbContext _context;

    public BookingNaiveService(SeatGridDbContext context)
    {
        _context = context;
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

        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // 1. Fetch requested seats
            // Note: Relies on EF Core's transaction isolation without explicit row-level locking
            // This may lead to race conditions under high concurrency
            
            var requestedRows = distinctSeatPairs.Select(s => s.Row).Distinct().ToList();
            var requestedCols = distinctSeatPairs.Select(s => s.Col).Distinct().ToList();

            var candidateSeats = await _context.Seats
                .Where(s => s.EventId == eventId && 
                            requestedRows.Contains(s.Row) && 
                            requestedCols.Contains(s.Col))
                .ToListAsync(cancellationToken);

            var seats = candidateSeats
                .Where(s => distinctSeatPairs.Any(rs => rs.Row == s.Row && rs.Col == s.Col))
                .ToList();

            // 2. Validate seat count
            if (seats.Count != distinctSeatPairs.Count)
            {
                var foundPairs = seats.Select(s => (s.Row, s.Col)).ToHashSet();
                var missingSeats = distinctSeatPairs.Where(p => !foundPairs.Contains(p)).ToList();

                return Result<BookingSuccess, BookingError>.Failure(
                    new BookingError("One or more seats do not exist.", new { MissingSeats = missingSeats }));
            }

            // 3. Check Availability (The critical check - race condition prone)
            var unavailableSeats = seats
                .Where(s => s.Status != SeatStatus.Available)
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

            // 4. Update Status
            foreach (var seat in seats)
            {
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
}
