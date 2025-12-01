using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Application.Interfaces;
using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Infrastructure.Persistence;

namespace SeatGrid.API.Infrastructure.Repositories;

public class SeatRepository : ISeatRepository
{
    private readonly SeatGridDbContext _context;

    public SeatRepository(SeatGridDbContext context)
    {
        _context = context;
    }

    public async Task<List<Seat>> GetSeatsForBookingAsync(long eventId, List<(string Row, string Col)> seatPairs, CancellationToken cancellationToken)
    {
        // Build VALUES clause for tuple IN
        var valuesClause = string.Join(", ",
            seatPairs.Select(p => $"('{p.Row.Replace("'", "''")}', '{p.Col.Replace("'", "''")}')"));

        // Fetch and lock seats in one query
        return await _context.Seats
            .FromSqlRaw($@"
                SELECT * FROM ""Seats""
                WHERE ""EventId"" = {{0}}
                  AND (""Row"", ""Col"") IN ({valuesClause})
                FOR UPDATE NOWAIT",
                    eventId)
            .ToListAsync(cancellationToken);
    }
}
