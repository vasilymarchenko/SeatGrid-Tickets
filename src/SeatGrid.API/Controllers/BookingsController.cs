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
    private readonly IBookedSeatsCache _bookedSeatsCache;

    public BookingsController(
        IBookingService bookingService,
        IBookedSeatsCache bookedSeatsCache)
    {
        _bookingService = bookingService;
        _bookedSeatsCache = bookedSeatsCache;
    }

    [HttpPost]
    public async Task<IActionResult> BookSeats([FromBody] BookingRequest request)
    {
        if (request.Seats == null || !request.Seats.Any())
        {
            return BadRequest(new BookingErrorResponse(false, "No seats specified."));
        }

        var seatPairs = request.Seats.Select(s => (s.Row, s.Col)).ToList();

        // 1. Redis Gatekeeper: Try to reserve specific seats atomically
        // This prevents 99.9% of conflicts from hitting the DB
        var reserved = await _bookedSeatsCache.TryReserveSeatsAsync(
            request.EventId, 
            seatPairs, 
            CancellationToken.None);

        if (!reserved)
        {
            return Conflict(new BookingErrorResponse(false, 
                "One or more seats are already reserved (cache hit)."));
        }

        try
        {
            // 2. DB Transaction (Optimistic Lock)
            var result = await _bookingService.BookSeatsAsync(
                request.EventId,
                request.UserId,
                seatPairs,
                CancellationToken.None);

            if (result.IsSuccess)
            {
                // Success! 
                // Note: We don't need to update BookedSeatsCache because we already reserved them in Step 1
                var success = result.GetSuccessOrThrow();
                
                return Ok(new BookingResponse(true, "Booking successful", success.SeatCount));
            }
            else
            {
                // 3. Compensation: Booking failed (e.g. DB constraint, validation error)
                // We must release the seats in Redis so others can try
                await _bookedSeatsCache.ReleaseSeatsAsync(
                    request.EventId, 
                    seatPairs, 
                    CancellationToken.None);

                var error = result.GetErrorOrThrow();

                if (error.Message.Contains("locked") || error.Message.Contains("already booked"))
                {
                    return Conflict(new BookingErrorResponse(false, error.Message, error.Details));
                }
                return BadRequest(new BookingErrorResponse(false, error.Message, error.Details));
            }
        }
        catch (Exception)
        {
            // 4. Compensation on Crash: Ensure we don't leave ghost seats if the app crashes
            await _bookedSeatsCache.ReleaseSeatsAsync(
                request.EventId, 
                seatPairs, 
                CancellationToken.None);
            throw;
        }
    }
}
