using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Data;
using SeatGrid.Domain.Enums;

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
        // The "Write Bottleneck": Synchronous, transactional booking
        
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // 1. Fetch requested seats
            // Note: In a real high-concurrency scenario, we might want 'FOR UPDATE' locking here.
            // For now, we rely on EF Core's Optimistic Concurrency or standard transaction isolation.
            
            // Fix: EF Core cannot translate 'Contains' with complex objects (SeatPosition).
            // We fetch a superset (matching Rows AND Cols) and filter in memory.
            var requestedRows = request.Seats.Select(s => s.Row).Distinct().ToList();
            var requestedCols = request.Seats.Select(s => s.Col).Distinct().ToList();

            var candidateSeats = await _context.Seats
                .Where(s => s.EventId == request.EventId && 
                            requestedRows.Contains(s.Row) && 
                            requestedCols.Contains(s.Col))
                .ToListAsync();

            var seats = candidateSeats
                .Where(s => request.Seats.Any(rs => rs.Row == s.Row && rs.Col == s.Col))
                .ToList();

            // 2. Validation
            if (seats.Count != request.Seats.Count)
            {
                return BadRequest("One or more seats do not exist.");
            }

            // 3. Check Availability (The critical check)
            if (seats.Any(s => s.Status != SeatStatus.Available))
            {
                await transaction.RollbackAsync();
                return Conflict("One or more seats are already booked.");
            }

            // 4. Update Status
            foreach (var seat in seats)
            {
                seat.Status = SeatStatus.Booked;
                seat.CurrentHolderId = request.UserId;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { Message = "Booking successful", SeatCount = seats.Count });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

public record BookingRequest(long EventId, string UserId, List<SeatPosition> Seats);
public record SeatPosition(string Row, string Col);
