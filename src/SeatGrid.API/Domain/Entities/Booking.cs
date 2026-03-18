using SeatGrid.API.Domain.Events;
using SeatGrid.API.Domain.Exceptions;
using SeatGrid.API.Domain.Primitives;
using SeatGrid.API.Domain.ValueObjects;

namespace SeatGrid.API.Domain.Entities;

// DDD — Aggregate Root
// Booking is the aggregate root for the booking boundary. An aggregate root is
// the single entry point through which all state changes are made. External code
// can hold a reference to a Booking and call its methods, but it can never reach
// inside and mutate a BookedSeat or change Status directly.
//
// Why is Booking the root and not Seat?
// A seat's availability is only meaningful in the context of a booking attempt.
// The invariant "no double-booking" spans multiple seats and requires knowing what
// is already booked — that logic belongs here, not scattered in a service.
//
// Anemic model vs rich model:
// In the previous design, Booking did not exist as an entity at all — services
// queried Seat rows and mutated them directly. That is the "anemic domain model"
// anti-pattern: the data lives in one place, the behaviour in another. Here, the
// Booking aggregate owns both the data and the rules that govern it.
//
// Constructor visibility:
// Both constructors are private. The only way to obtain a valid Booking is via
// the static Create() factory. This makes it impossible to construct a Booking
// in an invalid state (e.g. missing EventId, null UserId, wrong initial status).
public sealed class Booking : AggregateRoot<BookingId>
{
    // DDD — Aggregate-internal collection
    // The backing field is private so the aggregate fully controls it.
    // External code receives only an IReadOnlyList via the Seats property —
    // it can iterate but never Add, Remove, or mutate the collection directly.
    private readonly List<BookedSeat> _seats = new();

    // Required by EF Core to materialise this entity from the database.
    private Booking() { }

    private Booking(BookingId id, Guid orderId, long eventId, string userId)
    {
        Id      = id;
        OrderId = orderId;
        EventId = eventId;
        UserId  = userId;
        // DDD — Initialise to a known valid state.
        // A newly created Booking is always Pending. Nothing external can set
        // it to Confirmed or Cancelled at construction time.
        Status  = BookingStatus.Pending;
    }

    // DDD — Static factory method (Named Constructor pattern)
    // Centralises object construction. Keeps 'new Booking(...)' private and
    // exposes one well-named entry point that communicates intent.
    public static Booking Create(Guid orderId, long eventId, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return new Booking(BookingId.New(), orderId, eventId, userId);
    }

    // No public setters — all properties are read-only from outside.
    // Id is inherited from AggregateRoot<BookingId>.
    public Guid          OrderId { get; private set; }
    public long          EventId { get; private set; }
    public string        UserId  { get; private set; } = null!;
    public BookingStatus Status  { get; private set; } = null!;

    // Exposes the internal collection as a read-only view.
    // Callers can read seat data but cannot modify the list.
    public IReadOnlyList<BookedSeat> Seats => _seats.AsReadOnly();

    // DDD — Invariant enforcement
    // The "no double-booking" rule lives here, not in a service or handler.
    // After this refactor it is structurally impossible to add the same seat
    // twice to a Booking from outside this class — the aggregate rejects it.
    // This is what DDD means by "protecting invariants inside the aggregate".
    public void AddSeat(SeatLocation location)
    {
        if (_seats.Any(s => s.Location == location))
            throw new DomainException($"Seat {location} is already booked in this booking.");

        _seats.Add(new BookedSeat(location));
    }

    // DDD — State transition methods with domain event raising
    // The aggregate changes its own state and immediately records what happened
    // as a domain event. The event is not dispatched here — it is collected in
    // memory and dispatched by the Infrastructure layer after the data is persisted.
    // This keeps cause (state change) and effect (notification) co-located inside
    // the aggregate, yet decoupled from transport concerns.
    public void Confirm()
    {
        if (Status != BookingStatus.Pending)
            throw new DomainException("Only a Pending booking can be confirmed.");

        Status = BookingStatus.Confirmed;
        // DDD — Domain Event: raised inside the aggregate after the state change.
        // Carries OrderId, EventId, and seat list so handlers need not query the DB.
        AddDomainEvent(new BookingConfirmed(
            OrderId,
            EventId,
            UserId,
            _seats.Select(s => s.Location).ToList(),
            DateTimeOffset.UtcNow));
    }

    public void Cancel()
    {
        if (Status == BookingStatus.Cancelled)
            throw new DomainException("Booking is already cancelled.");

        Status = BookingStatus.Cancelled;
        AddDomainEvent(new BookingCancelled(
            OrderId,
            EventId,
            _seats.Select(s => s.Location).ToList(),
            DateTimeOffset.UtcNow));
    }
}
