using System.ComponentModel.DataAnnotations;
using SeatGrid.API.Domain.Enums;

namespace SeatGrid.API.Domain.Entities;

public class Seat
{
    public long Id { get; set; }
    public long EventId { get; set; }
    public string Row { get; set; } = string.Empty;
    public string Col { get; set; } = string.Empty;
    
    /// <summary>
    /// Seat booking status. Marked as ConcurrencyCheck for optimistic locking.
    /// When using optimistic concurrency, EF Core includes this value in the WHERE clause
    /// of UPDATE statements, ensuring the update only succeeds if the value hasn't changed.
    /// </summary>
    [ConcurrencyCheck]
    public SeatStatus Status { get; set; }
    
    /// <summary>
    /// Current holder (user/reservation) of the seat. Marked as ConcurrencyCheck for optimistic locking.
    /// Combined with Status, these two fields provide natural version tracking without requiring
    /// an explicit timestamp/rowversion column. Any change to either field indicates concurrent modification.
    /// </summary>
    [ConcurrencyCheck]
    public string? CurrentHolderId { get; set; } // UserId or ReservationId
}
