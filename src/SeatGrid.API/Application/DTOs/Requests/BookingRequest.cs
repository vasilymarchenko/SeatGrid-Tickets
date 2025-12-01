namespace SeatGrid.API.Application.DTOs.Requests;

public record BookingRequest(
    long EventId,
    string UserId,
    List<SeatPosition> Seats
);

public record SeatPosition(
    string Row,
    string Col
);
