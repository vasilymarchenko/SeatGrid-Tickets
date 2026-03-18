using MediatR;
using Microsoft.EntityFrameworkCore;
using SeatGrid.API.Domain.Entities;
using SeatGrid.API.Domain.Primitives;

namespace SeatGrid.API.Infrastructure.Persistence;

public class SeatGridDbContext : DbContext
{
    private readonly IMediator _mediator;

    // DDD — SaveChangesAsync override for domain event dispatch
    // IMediator is injected here so the DbContext can dispatch domain events
    // after the data is committed. This is an Infrastructure concern — the
    // domain knows nothing about MediatR; it only raises IDomainEvent instances
    // and stores them in memory on the aggregate root.
    public SeatGridDbContext(DbContextOptions<SeatGridDbContext> options, IMediator mediator)
        : base(options)
    {
        _mediator = mediator;
    }

    public DbSet<Event> Events { get; set; }
    public DbSet<Seat> Seats { get; set; }
    // Booking is the new DDD aggregate root. Its DbSet is required for EF to
    // track the aggregate and for repositories to query it.
    public DbSet<Booking> Bookings { get; set; }

    // DDD — Dispatch domain events after persistence
    // The override follows a strict order that guarantees consistency:
    //   1. Persist the aggregate state to the database (base call).
    //   2. Collect all domain events from every tracked aggregate root.
    //   3. Clear the events from the aggregates (prevent re-dispatch on a
    //      second SaveChangesAsync call within the same DbContext lifetime).
    //   4. Publish each event via MediatR.
    //
    // Why collect-then-clear BEFORE dispatching?
    // If a handler triggered by step 4 causes another SaveChangesAsync call,
    // clearing first ensures the original events are not published a second time.
    //
    // Stage 6 note: this method is the exact interception point for the Outbox
    // pattern. Instead of publishing via MediatR in step 4, the Outbox will
    // serialise the events into an OutboxMessages table inside the same DB
    // transaction as step 1. The collect-and-clear logic stays unchanged.
    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var aggregates = ChangeTracker
            .Entries<AggregateRoot>()    // find all tracked aggregate roots
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        var events = aggregates
            .SelectMany(a => a.DomainEvents)
            .ToList();

        foreach (var aggregate in aggregates)
            aggregate.ClearDomainEvents();

        var result = await base.SaveChangesAsync(ct);

        // TODO: the issue:
        // Even the fixed version above has a subtler problem — events are published even if SaveChangesAsync throws.
        // A domain event signals something that happened, but if the DB write failed, nothing actually happened.

        foreach (var evt in events)
            await _mediator.Publish(evt, ct);

        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // DDD — Apply all IEntityTypeConfiguration<T> classes from this assembly.
        // ApplyConfigurationsFromAssembly scans for every class implementing
        // IEntityTypeConfiguration<T> and calls Configure() on it. This keeps
        // OnModelCreating lean — no entity-mapping logic lives here any more.
        // Each aggregate/entity owns its own configuration file in Configurations/.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SeatGridDbContext).Assembly);
    }
}
