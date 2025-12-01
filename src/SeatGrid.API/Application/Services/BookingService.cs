using Npgsql;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Enums;
using System.Data;

namespace SeatGrid.API.Application.Services;

public class BookingService : IBookingService
{
    private readonly ISeatRepository _seatRepository;
    private readonly IUnitOfWork _unitOfWork;

    public BookingService(ISeatRepository seatRepository, IUnitOfWork unitOfWork)
    {
        _seatRepository = seatRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<BookingResult> BookSeatsAsync(long eventId, string userId, List<(string Row, string Col)> seatPairs, CancellationToken cancellationToken)
    {
        if (seatPairs == null || !seatPairs.Any())
        {
            return new BookingResult(false, "No seats specified.");
        }

        var distinctSeatPairs = seatPairs.Distinct().ToList();

        await _unitOfWork.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            var seats = await _seatRepository.GetSeatsForBookingAsync(eventId, distinctSeatPairs, cancellationToken);

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

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync();

            return new BookingResult(true, "Booking successful", new { SeatCount = seats.Count });
        }
        catch (PostgresException ex) when (ex.SqlState == "55P03") // Lock not available
        {
            await _unitOfWork.RollbackTransactionAsync();
            return new BookingResult(false, "Seats are currently locked by another transaction. Please try again.");
        }
        catch (Exception)
        {
            await _unitOfWork.RollbackTransactionAsync();
            throw;
        }
    }
}
