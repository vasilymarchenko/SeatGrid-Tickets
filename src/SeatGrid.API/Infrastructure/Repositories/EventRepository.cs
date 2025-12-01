using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Domain.Interfaces;
using SeatGrid.API.Infrastructure.Persistence;

namespace SeatGrid.API.Infrastructure.Repositories;

public class EventRepository : IEventRepository
{
    private readonly SeatGridDbContext _context;

    public EventRepository(SeatGridDbContext context)
    {
        _context = context;
    }

    public async Task AddEventAsync(Event evt, CancellationToken cancellationToken)
    {
        await _context.Events.AddAsync(evt, cancellationToken);
    }

    public async Task AddSeatsAsync(IEnumerable<Seat> seats, CancellationToken cancellationToken)
    {
        await _context.Seats.AddRangeAsync(seats, cancellationToken);
    }

    public async Task<List<Seat>> GetSeatsByEventIdAsync(long eventId, CancellationToken cancellationToken)
    {
        return await _context.Seats
            .Where(s => s.EventId == eventId)
            .OrderBy(s => s.Row)
            .ThenBy(s => s.Col)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
