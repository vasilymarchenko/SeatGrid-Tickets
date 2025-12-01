using Microsoft.AspNetCore.Mvc;
using SeatGrid.API.Application.DTOs.Requests;
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
        var response = await _eventService.CreateEventAsync(
            request.Name,
            request.Date,
            request.Rows,
            request.Cols,
            CancellationToken.None);

        return CreatedAtAction(nameof(GetSeats), new { id = response.Id }, response);
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
