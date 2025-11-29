using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Data;
using SeatGrid.Domain.Entities;
using SeatGrid.Domain.Enums;

namespace SeatGrid.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly SeatGridDbContext _context;

    public EventsController(SeatGridDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent([FromBody] CreateEventRequest request)
    {
        // 1. Create the Event
        var newEvent = new Event
        {
            Name = request.Name,
            Date = request.Date,
            Rows = request.Rows,
            Cols = request.Cols
        };

        _context.Events.Add(newEvent);
        
        // 2. Synchronously generate seats (The "Naive" approach)
        // This will be slow for large venues, simulating a heavy write operation.
        var seats = new List<Seat>();
        for (int r = 1; r <= request.Rows; r++)
        {
            for (int c = 1; c <= request.Cols; c++)
            {
                seats.Add(new Seat
                {
                    Row = r.ToString(),
                    Col = c.ToString(),
                    Status = SeatStatus.Available,
                    Event = newEvent
                });
            }
        }

        _context.Seats.AddRange(seats);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSeats), new { id = newEvent.Id }, new { newEvent.Id, newEvent.Name, SeatCount = seats.Count });
    }

    [HttpGet("{id}/seats")]
    public async Task<IActionResult> GetSeats(long id)
    {
        // The "Read Bottleneck": Fetching all seats without pagination or caching
        var seats = await _context.Seats
            .Where(s => s.EventId == id)
            .OrderBy(s => s.Row)
            .ThenBy(s => s.Col)
            .Select(s => new 
            {
                s.Row,
                s.Col,
                Status = s.Status.ToString()
            })
            .ToListAsync();

        if (!seats.Any())
            return NotFound("Event not found or no seats available.");

        return Ok(seats);
    }
}

public record CreateEventRequest(string Name, DateTime Date, int Rows, int Cols);
