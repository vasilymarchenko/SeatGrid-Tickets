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
    private readonly IAvailabilityCache _availabilityCache;

    public BookingsController(
        IBookingService bookingService,
        IAvailabilityCache availabilityCache)
    {
        _bookingService = bookingService;
        _availabilityCache = availabilityCache;
    }

    [HttpPost]
    public async Task<IActionResult> BookSeats([FromBody] BookingRequest request)
    {
        if (request.Seats == null || !request.Seats.Any())
        {
            return BadRequest(new BookingErrorResponse(false, "No seats specified."));
        }

        // Fast-path: Check available seat count from cache
        var availableCount = await _availabilityCache.GetAvailableCountAsync(
            request.EventId, 
            CancellationToken.None);

        if (availableCount.HasValue)
        {
            // Event sold out - instant rejection
            if (availableCount == 0)
            {
                return Conflict(new BookingErrorResponse(false, 
                    "Event is sold out. No seats available."));
            }

            // Not enough seats available
            if (availableCount < request.Seats.Count)
            {
                return Conflict(new BookingErrorResponse(false, 
                    $"Only {availableCount} seats available, you requested {request.Seats.Count}."));
            }
        }

        // Proceed to booking service
        var seatPairs = request.Seats.Select(s => (s.Row, s.Col)).ToList();

        var result = await _bookingService.BookSeatsAsync(
            request.EventId,
            request.UserId,
            seatPairs,
            CancellationToken.None);

        // Update cache on successful booking
        if (result.IsSuccess)
        {
            var success = result.GetSuccessOrThrow();
            await _availabilityCache.DecrementAvailableCountAsync(
                request.EventId, 
                success.SeatCount, 
                CancellationToken.None);
        }

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
