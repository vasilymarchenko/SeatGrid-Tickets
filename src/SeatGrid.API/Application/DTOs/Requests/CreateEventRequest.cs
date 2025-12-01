namespace SeatGrid.API.Application.DTOs.Requests;

public record CreateEventRequest(
    string Name,
    DateTime Date,
    int Rows,
    int Cols
);
