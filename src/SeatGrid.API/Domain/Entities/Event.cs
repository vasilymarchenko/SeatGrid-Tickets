namespace SeatGrid.API.Domain.Entities;

public class Event
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public int Rows { get; set; }
    public int Cols { get; set; }
}
