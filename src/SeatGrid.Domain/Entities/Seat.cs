using SeatGrid.Domain.Enums;

namespace SeatGrid.Domain.Entities;

public class Seat
{
    public long Id { get; set; }
    public long EventId { get; set; }
    public string Row { get; set; } = string.Empty;
    public string Col { get; set; } = string.Empty;
    public SeatStatus Status { get; set; }
    public string? CurrentHolderId { get; set; } // UserId or ReservationId

    // Navigation property
    public Event? Event { get; set; }
}
