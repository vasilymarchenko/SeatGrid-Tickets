namespace SeatGrid.API.Domain.Exceptions;

// DDD — Domain Exception
// In DDD the domain layer must never depend on infrastructure or application concerns.
// Using a dedicated DomainException keeps violation-of-invariant signals inside the
// domain layer: aggregates and value objects throw it, and the Application layer
// catches it to translate it into an appropriate HTTP response or error DTO.
// Using the base Exception class directly would blur that boundary.
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
