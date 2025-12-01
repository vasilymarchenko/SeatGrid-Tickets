using SeatGrid.API.Application.DTOs.Responses;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Domain.Enums;
using SeatGrid.API.Domain.Interfaces;

namespace SeatGrid.API.Application.Services;

public class EventService : IEventService
{
    private readonly IEventRepository _eventRepository;

    public EventService(IEventRepository eventRepository)
    {
        _eventRepository = eventRepository;
    }

    public async Task<EventResponse> CreateEventAsync(string name, DateTime date, int rows, int cols, CancellationToken cancellationToken)
    {
        var newEvent = new Event
        {
            Name = name,
            Date = date,
            Rows = rows,
            Cols = cols
        };

        await _eventRepository.AddEventAsync(newEvent, cancellationToken);
        await _eventRepository.SaveChangesAsync(cancellationToken); // Save to generate Event.Id
        
        // Generate seats - now newEvent.Id has the generated value
        var seats = new List<Seat>();
        for (int r = 1; r <= rows; r++)
        {
            for (int c = 1; c <= cols; c++)
            {
                seats.Add(new Seat
                {
                    EventId = newEvent.Id,
                    Row = r.ToString(),
                    Col = c.ToString(),
                    Status = SeatStatus.Available
                });
            }
        }

        await _eventRepository.AddSeatsAsync(seats, cancellationToken);
        await _eventRepository.SaveChangesAsync(cancellationToken);

        return new EventResponse(
            newEvent.Id,
            newEvent.Name,
            newEvent.Date,
            newEvent.Rows,
            newEvent.Cols,
            rows * cols
        );
    }

    public async Task<IEnumerable<SeatResponse>> GetEventSeatsAsync(long eventId, CancellationToken cancellationToken)
    {
        var seats = await _eventRepository.GetSeatsByEventIdAsync(eventId, cancellationToken);
        return seats.Select(s => new SeatResponse(
            s.Row,
            s.Col,
            s.Status.ToString()
        ));
    }
}
