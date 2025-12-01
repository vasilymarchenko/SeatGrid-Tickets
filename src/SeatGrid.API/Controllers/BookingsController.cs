using Microsoft.AspNetCore.Mvc;
using SeatGrid.API.Application.Interfaces;

namespace SeatGrid.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpPost]
    public async Task<IActionResult> BookSeats([FromBody] BookingRequest request)
    {
        if (request.Seats == null || !request.Seats.Any())
        {
            return BadRequest("No seats specified.");
        }

        var seatPairs = request.Seats.Select(s => (s.Row, s.Col)).ToList();

        try
        {
            var result = await _bookingService.BookSeatsAsync(request.EventId, request.UserId, seatPairs, CancellationToken.None);

            if (result.Success)
            {
                return Ok(new { result.Message, Data = result.Data });
            }
            else
            {
                if (result.Message.Contains("locked") || result.Message.Contains("already booked"))
                {
                    return Conflict(new { result.Message, result.Data });
                }
                return BadRequest(new { result.Message, result.Data });
            }
        }
        catch (Exception)
        {
            return StatusCode(500, "An error occurred while processing your booking.");
        }
    }
}

public record BookingRequest(long EventId, string UserId, List<SeatPosition> Seats);
public record SeatPosition(string Row, string Col);
