using SeatGrid.API.Domain.Events;

namespace SeatGrid.API.Domain.Primitives;

// DDD — Aggregate Root base class (non-generic)
// Holds the domain event collection. The DbContext queries this type so that all
// concrete aggregates are discovered regardless of their TId.
//
// Why split into non-generic base + generic subclass?
// ChangeTracker.Entries<T>() uses `is T` checks internally. C# generic classes
// are invariant: AggregateRoot<BookingId> is NOT assignment-compatible with
// AggregateRoot<object>. Querying Entries<AggregateRoot<object>>() silently
// finds zero entries. The non-generic base resolves this — every concrete
// aggregate satisfies it regardless of its TId.
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = new();

    // Read-only view: the Application layer can collect events but cannot add them.
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    // Protected: only the aggregate's own methods may raise events.
    protected void AddDomainEvent(IDomainEvent evt) => _domainEvents.Add(evt);

    // Called by the DbContext override after dispatch to prevent re-publishing
    // the same events on a second SaveChangesAsync call.
    public void ClearDomainEvents() => _domainEvents.Clear();
}

// DDD — Typed Aggregate Root
// Adds the strongly-typed Id property. Concrete aggregates inherit this and
// get both the Id and the domain event list via the non-generic base.
public abstract class AggregateRoot<TId> : AggregateRoot where TId : notnull
{
    public TId Id { get; protected set; } = default!;
}
