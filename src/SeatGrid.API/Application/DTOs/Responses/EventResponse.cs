namespace SeatGrid.API.Application.DTOs.Responses;

public record EventResponse(
    long Id,
    string Name,
    DateTime Date,
    int Rows,
    int Cols,
    int TotalSeats
);
