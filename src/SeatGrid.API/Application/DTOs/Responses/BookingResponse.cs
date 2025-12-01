namespace SeatGrid.API.Application.DTOs.Responses;

public record BookingResponse(
    bool Success,
    string Message,
    int? SeatCount = null
);

public record BookingErrorResponse(
    bool Success,
    string Message,
    object? ErrorDetails = null
);
