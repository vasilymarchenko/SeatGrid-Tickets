using Microsoft.AspNetCore.Mvc;
using SeatGrid.API.Application.DTOs.Requests;
using SeatGrid.API.Application.DTOs.Responses;
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
            return BadRequest(new BookingErrorResponse(false, "No seats specified."));
        }

        var seatPairs = request.Seats.Select(s => (s.Row, s.Col)).ToList();

        var result = await _bookingService.BookSeatsAsync(
            request.EventId,
            request.UserId,
            seatPairs,
            CancellationToken.None);

        return result.Match<IActionResult>(
            onSuccess: success => Ok(new BookingResponse(true, "Booking successful", success.SeatCount)),
            onFailure: error =>
            {
                if (error.Message.Contains("locked") || error.Message.Contains("already booked"))
                {
                    return Conflict(new BookingErrorResponse(false, error.Message, error.Details));
                }
                return BadRequest(new BookingErrorResponse(false, error.Message, error.Details));
            }
        );
    }
}
