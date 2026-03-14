using MassTransit;
using Microsoft.AspNetCore.Mvc;
using SeatGrid.API.Application.DTOs.Requests;
using SeatGrid.API.Application.DTOs.Responses;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.Contracts;

namespace SeatGrid.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BookingsController : ControllerBase
{
    private readonly IBookedSeatsCache _bookedSeatsCache;
    private readonly IPublishEndpoint _publishEndpoint;

    public BookingsController(
        IBookedSeatsCache bookedSeatsCache,
        IPublishEndpoint publishEndpoint)
    {
        _bookedSeatsCache = bookedSeatsCache;
        _publishEndpoint = publishEndpoint;
    }

    [HttpPost]
    public async Task<IActionResult> BookSeats([FromBody] BookingRequest request)
    {
        if (request.Seats == null || !request.Seats.Any())
        {
            return BadRequest(new BookingErrorResponse(false, "No seats specified."));
        }

        var seatPairs = request.Seats.Select(s => (s.Row, s.Col)).ToList();
        var orderId = Guid.NewGuid();

        // 1. Redis Gatekeeper: Reserve seats with TTL (120s)
        // This is the "Reservation" phase.
        var reserved = await _bookedSeatsCache.TryReserveSeatsAsync(
            request.EventId, 
            seatPairs, 
            TimeSpan.FromSeconds(120),
            CancellationToken.None);

        if (!reserved)
        {
            return Conflict(new BookingErrorResponse(false, 
                "One or more seats are already reserved."));
        }

        try
        {
            // 2. Publish Event to Bus (Async Payment)
            await _publishEndpoint.Publish(new BookingInitiated
            {
                OrderId = orderId,
                EventId = request.EventId,
                Seats = request.Seats.Select(s => new SeatLocation(int.Parse(s.Row), int.Parse(s.Col))).ToList(),
                UserId = request.UserId,
                CreatedAt = DateTime.UtcNow
            });

            return Accepted(new BookingResponse(true, "Booking initiated. Please wait for payment confirmation.", request.Seats.Count));
        }
        catch (Exception)
        {
            // Compensation: If publishing fails, release the lock
            await _bookedSeatsCache.ReleaseSeatsAsync(
                request.EventId, 
                seatPairs, 
                CancellationToken.None);
            throw;
        }
    }
}
