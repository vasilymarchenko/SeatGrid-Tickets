namespace SeatGrid.API.Application.DTOs.Responses;

public record SeatResponse(
    string Row,
    string Col,
    string Status
);
