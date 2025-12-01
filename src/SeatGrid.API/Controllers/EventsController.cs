using Microsoft.AspNetCore.Mvc;
using SeatGrid.API.Application.Interfaces;

namespace SeatGrid.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly IEventService _eventService;

    public EventsController(IEventService eventService)
    {
        _eventService = eventService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent([FromBody] CreateEventRequest request)
    {
        var newEvent = await _eventService.CreateEventAsync(request.Name, request.Date, request.Rows, request.Cols, CancellationToken.None);
        
        // We don't have seat count in return value of CreateEventAsync easily unless we change it.
        // But for now, let's just return what we have.
        // Or calculate it: rows * cols.
        
        return CreatedAtAction(nameof(GetSeats), new { id = newEvent.Id }, new { newEvent.Id, newEvent.Name, SeatCount = request.Rows * request.Cols });
    }

    [HttpGet("{id}/seats")]
    public async Task<IActionResult> GetSeats(long id)
    {
        var seats = await _eventService.GetEventSeatsAsync(id, CancellationToken.None);

        if (!seats.Any())
            return NotFound("Event not found or no seats available.");

        return Ok(seats);
    }
}

public record CreateEventRequest(string Name, DateTime Date, int Rows, int Cols);
