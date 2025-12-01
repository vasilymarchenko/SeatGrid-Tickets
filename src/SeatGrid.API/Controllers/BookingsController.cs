using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Data;
using SeatGrid.Domain.Enums;
using System.Data;

namespace SeatGrid.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly SeatGridDbContext _context;

    public BookingsController(SeatGridDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> BookSeats([FromBody] BookingRequest request)
    {
        // Validate input
        if (request.Seats == null || !request.Seats.Any())
        {
            return BadRequest("No seats specified.");
        }

        using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        try
        {
            // Build seat pairs
            var seatPairs = request.Seats
                .Select(s => (Row: s.Row, Col: s.Col))
                .Distinct()
                .ToList();

            // Build VALUES clause for tuple IN
            var valuesClause = string.Join(", ",
                seatPairs.Select(p => $"('{p.Row.Replace("'", "''")}', '{p.Col.Replace("'", "''")}')"));

            // Fetch and lock seats in one query
            var seats = await _context.Seats
                .FromSqlRaw($@"
                    SELECT * FROM ""Seats""
                    WHERE ""EventId"" = {{0}}
                      AND (""Row"", ""Col"") IN ({valuesClause})
                    FOR UPDATE NOWAIT",
                        request.EventId)
                .ToListAsync();

            // Validate seat count
            if (seats.Count != seatPairs.Count)
            {
                var foundPairs = seats.Select(s => (s.Row, s.Col)).ToHashSet();
                var missingSeats = seatPairs.Where(p => !foundPairs.Contains(p)).ToList();

                return BadRequest(new
                {
                    Message = "One or more seats do not exist.",
                    MissingSeats = missingSeats
                });
            }

            // Check availability
            var unavailableSeats = seats
                .Where(s => s.Status != SeatStatus.Available)
                .ToList();

            if (unavailableSeats.Any())
            {
                return Conflict(new
                {
                    Message = "One or more seats are already booked.",
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
                seat.CurrentHolderId = request.UserId;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { Message = "Booking successful", SeatCount = seats.Count });
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "55P03") // Lock not available
        {
            await transaction.RollbackAsync();
            return Conflict(new { Message = "Seats are currently locked by another transaction. Please try again." });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            // TODO: Log exception
            return StatusCode(500, "An error occurred while processing your booking.");
        }
    }
}

public record BookingRequest(long EventId, string UserId, List<SeatPosition> Seats);
public record SeatPosition(string Row, string Col);
