using MediatR;

namespace SeatGrid.API.Domain.Events;

// DDD — Domain Event marker interface
// A domain event records something that happened inside the domain — a fact,
// stated in the past tense: "booking was confirmed", "booking was cancelled".
//
// Why an interface and not a base class?
// Keeps the type hierarchy flat. Events are plain data records; they do not
// share behaviour, only the contract that they are domain events.
//
// Why extend INotification?
// INotification is MediatR's dispatch contract. Extending it here means domain
// events can be published via the mediator without the domain layer knowing anything
// about how dispatch works. The domain only sees IDomainEvent — the fact that
// INotification is in the hierarchy is an infrastructure concern, not a domain one.
// The Domain layer imports MediatR.Contracts (a small, stable package with only
// interfaces) — there is no dependency on the MediatR runtime or any dispatcher impl.
//
// Stage 6 note: when the Outbox pattern is introduced, the dispatch destination
// changes (write to OutboxMessages table instead of publish directly) but this
// interface stays exactly as-is. The domain does not change.
public interface IDomainEvent : INotification { }
