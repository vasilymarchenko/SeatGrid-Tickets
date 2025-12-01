using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Domain.Enums;

namespace SeatGrid.API.Application.Services;

public class EventService : IEventService
{
    private readonly IEventRepository _eventRepository;

    public EventService(IEventRepository eventRepository)
    {
        _eventRepository = eventRepository;
    }

    public async Task<Event> CreateEventAsync(string name, DateTime date, int rows, int cols, CancellationToken cancellationToken)
    {
        var newEvent = new Event
        {
            Name = name,
            Date = date,
            Rows = rows,
            Cols = cols
        };

        await _eventRepository.AddEventAsync(newEvent, cancellationToken);
        
        // Generate seats
        var seats = new List<Seat>();
        for (int r = 1; r <= rows; r++)
        {
            for (int c = 1; c <= cols; c++)
            {
                seats.Add(new Seat
                {
                    Row = r.ToString(),
                    Col = c.ToString(),
                    Status = SeatStatus.Available,
                    Event = newEvent
                });
            }
        }

        await _eventRepository.AddSeatsAsync(seats, cancellationToken);
        await _eventRepository.SaveChangesAsync(cancellationToken);

        return newEvent;
    }

    public async Task<IEnumerable<object>> GetEventSeatsAsync(long eventId, CancellationToken cancellationToken)
    {
        var seats = await _eventRepository.GetSeatsByEventIdAsync(eventId, cancellationToken);
        return seats.Select(s => new 
        {
            s.Row,
            s.Col,
            Status = s.Status.ToString()
        });
    }
}
