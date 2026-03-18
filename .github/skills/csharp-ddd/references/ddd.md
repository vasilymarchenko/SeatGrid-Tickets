# DDD Reference — C# / .NET

## Table of Contents
1. [Aggregate design](#1-aggregate-design)
2. [Value objects](#2-value-objects)
3. [Domain events](#3-domain-events)
4. [Repositories](#4-repositories)
5. [Domain services](#5-domain-services)
6. [Application services & CQRS](#6-application-services--cqrs)
7. [Bounded contexts & anti-corruption layer](#7-bounded-contexts--anti-corruption-layer)
8. [Testing domain logic](#8-testing-domain-logic)

---

## 1. Aggregate design

An **aggregate** is a cluster of domain objects that are always kept consistent together.
One object is the **aggregate root** — the only entry point for external callers.

### Rules
- Load the full aggregate via its repository; never partially hydrate it.
- Only the root exposes mutating methods. Child entities are mutated through root methods.
- External aggregates reference this aggregate by **ID only**.
- Keep aggregates small. If a child entity is referenced individually by external code, it is probably its own aggregate root.

### Example

```csharp
// © Sitecore Corporation A/S. All rights reserved.
using Ardalis.GuardClauses;

namespace Orders.Domain;

public sealed class Order : AggregateRoot<OrderId>
{
    private readonly List<OrderLine> _lines = [];

    private Order() { } // EF / serialization

    private Order(OrderId id, CustomerId customerId, Address shippingAddress)
    {
        Id = Guard.Against.Null(id);
        CustomerId = Guard.Against.Null(customerId);
        ShippingAddress = Guard.Against.Null(shippingAddress);
        Status = OrderStatus.Draft;
    }

    public static Order Create(CustomerId customerId, Address shippingAddress)
    {
        var order = new Order(OrderId.New(), customerId, shippingAddress);
        order.AddDomainEvent(new OrderCreated(order.Id, customerId));
        return order;
    }

    public CustomerId CustomerId { get; private set; } = null!;
    public Address ShippingAddress { get; private set; } = null!;
    public OrderStatus Status { get; private set; }
    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    public void AddLine(ProductId productId, Quantity quantity, Money unitPrice)
    {
        Guard.Against.Null(productId);
        EnsureStatus(OrderStatus.Draft);

        var existing = _lines.FirstOrDefault(l => l.ProductId == productId);
        if (existing is not null)
            existing.IncreaseQuantity(quantity);
        else
            _lines.Add(OrderLine.Create(productId, quantity, unitPrice));
    }

    public void Submit()
    {
        EnsureStatus(OrderStatus.Draft);
        if (_lines.Count == 0) throw new DomainException("Order must have at least one line.");
        Status = OrderStatus.Submitted;
        AddDomainEvent(new OrderSubmitted(Id));
    }

    private void EnsureStatus(OrderStatus expected)
    {
        if (Status != expected)
            throw new DomainException($"Operation requires status {expected}, but current status is {Status}.");
    }
}
```

### AggregateRoot base class (minimal)

```csharp
// © Sitecore Corporation A/S. All rights reserved.
namespace SharedKernel;

public abstract class AggregateRoot<TId> where TId : notnull
{
    private readonly List<IDomainEvent> _events = [];

    public TId Id { get; protected set; } = default!;
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _events.AsReadOnly();

    protected void AddDomainEvent(IDomainEvent @event) => _events.Add(@event);
    public void ClearDomainEvents() => _events.Clear();
}
```

---

## 2. Value objects

Value objects have **no identity** — equality is determined by their values.
They are **immutable**. Use C# `record` types.

### Rules
- No setters; all state set in the constructor or primary constructor.
- Validate in the constructor; throw `DomainException` for invalid values.
- Place primitive-obsession guard at the boundary — wrap primitives as soon as they enter the domain.

### Example

```csharp
// © Sitecore Corporation A/S. All rights reserved.
namespace Orders.Domain;

public sealed record Money(decimal Amount, Currency Currency)
{
    public static readonly Money Zero = new(0, Currency.Default);

    public Money(decimal amount, Currency currency) : this(amount, currency)
    {
        if (amount < 0) throw new DomainException("Amount cannot be negative.");
    }

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return this with { Amount = Amount + other.Amount };
    }

    public Money Multiply(int factor) => this with { Amount = Amount * factor };

    private void EnsureSameCurrency(Money other)
    {
        if (Currency != other.Currency)
            throw new DomainException($"Cannot combine {Currency} and {other.Currency}.");
    }
}
```

### Strongly-typed IDs

Prefer strongly-typed IDs over raw `Guid` to prevent mixing IDs across aggregates:

```csharp
// © Sitecore Corporation A/S. All rights reserved.
namespace Orders.Domain;

public sealed record OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
```

---

## 3. Domain events

Domain events record **something that happened** in the domain.

### Rules
- Named in the past tense: `OrderPlaced`, `PaymentFailed`.
- Raised **inside** the aggregate (via `AddDomainEvent`), **not** in the Application layer.
- Dispatched **after** the aggregate is persisted — typically in the Application layer or via an outbox.
- Immutable records.

### Example

```csharp
// © Sitecore Corporation A/S. All rights reserved.
namespace Orders.Domain.Events;

public sealed record OrderSubmitted(OrderId OrderId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
```

### Dispatching in the Application layer

```csharp
// © Sitecore Corporation A/S. All rights reserved.
namespace Orders.Application.Commands;

public sealed class SubmitOrderHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IDomainEventDispatcher dispatcher) : ICommandHandler<SubmitOrderCommand>
{
    public async Task HandleAsync(SubmitOrderCommand command, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(command.OrderId, ct)
            ?? throw new NotFoundException(nameof(Order), command.OrderId);

        order.Submit(); // raises OrderSubmitted domain event

        await unitOfWork.CommitAsync(ct); // persist first
        await dispatcher.DispatchAsync(order.DomainEvents, ct); // then dispatch
        order.ClearDomainEvents();
    }
}
```

---

## 4. Repositories

Repositories abstract aggregate **persistence**.

### Rules
- Interface lives in **Domain**; implementation lives in **Infrastructure**.
- One repository per aggregate root.
- Returns and accepts **full aggregates** only — no partial projections.
- Never expose `IQueryable` from a repository; that leaks persistence concerns into the domain.
- For read-side projections (CQRS), use a separate **query service** in the Application or Infrastructure layer.

### Example

```csharp
// © Sitecore Corporation A/S. All rights reserved.
// Domain layer
namespace Orders.Domain;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct = default);
    Task AddAsync(Order order, CancellationToken ct = default);
    void Update(Order order);
}
```

```csharp
// © Sitecore Corporation A/S. All rights reserved.
// Infrastructure layer
namespace Orders.Infrastructure.Persistence;

internal sealed class OrderRepository(OrderDbContext db) : IOrderRepository
{
    public Task<Order?> GetByIdAsync(OrderId id, CancellationToken ct) =>
        db.Orders
          .Include(o => o.Lines)
          .FirstOrDefaultAsync(o => o.Id == id, ct);

    public Task AddAsync(Order order, CancellationToken ct) =>
        db.Orders.AddAsync(order, ct).AsTask();

    public void Update(Order order) =>
        db.Orders.Update(order);
}
```

---

## 5. Domain services

Use a domain service only when logic **genuinely spans multiple aggregates** and belongs conceptually in the domain (not in any one aggregate).

### When NOT to use a domain service
- Logic fits entirely in one aggregate → put it in the aggregate.
- Logic is orchestration (load, call, persist) → put it in the Application layer.
- Logic is infrastructure (email, HTTP) → put it in Infrastructure behind an interface.

### Example

```csharp
// © Sitecore Corporation A/S. All rights reserved.
namespace Orders.Domain.Services;

/// <summary>
/// Determines whether a customer is eligible to place an order,
/// combining rules from Customer and CreditAccount aggregates.
/// </summary>
public sealed class OrderEligibilityService(ICreditAccountRepository creditAccounts)
{
    public async Task<bool> IsEligibleAsync(Customer customer, Money orderTotal, CancellationToken ct)
    {
        if (!customer.IsActive) return false;
        var credit = await creditAccounts.GetByCustomerAsync(customer.Id, ct);
        return credit?.HasSufficientFunds(orderTotal) ?? false;
    }
}
```

---

## 6. Application services & CQRS

The Application layer **orchestrates** domain objects to fulfil use cases.
It contains **no business logic** — all rules live in the domain.

### Command / Query pattern

```
Commands  → mutate state  → return void (or a created ID)
Queries   → read state    → return a DTO / read model
```

Keep commands and queries in separate handlers. Never mix reads into command handlers.

### Command handler skeleton

```csharp
// © Sitecore Corporation A/S. All rights reserved.
namespace Orders.Application.Commands;

public sealed record PlaceOrderCommand(CustomerId CustomerId, Address ShippingAddress);

public sealed class PlaceOrderHandler(
    IOrderRepository orders,
    IUnitOfWork unitOfWork,
    IDomainEventDispatcher dispatcher) : ICommandHandler<PlaceOrderCommand, OrderId>
{
    public async Task<OrderId> HandleAsync(PlaceOrderCommand cmd, CancellationToken ct)
    {
        var order = Order.Create(cmd.CustomerId, cmd.ShippingAddress);
        await orders.AddAsync(order, ct);
        await unitOfWork.CommitAsync(ct);
        await dispatcher.DispatchAsync(order.DomainEvents, ct);
        order.ClearDomainEvents();
        return order.Id;
    }
}
```

### Query handler skeleton

```csharp
// © Sitecore Corporation A/S. All rights reserved.
namespace Orders.Application.Queries;

public sealed record OrderSummaryQuery(OrderId OrderId);
public sealed record OrderSummaryDto(OrderId Id, OrderStatus Status, int LineCount, decimal Total);

public sealed class OrderSummaryHandler(IOrderReadService readService)
    : IQueryHandler<OrderSummaryQuery, OrderSummaryDto?>
{
    public Task<OrderSummaryDto?> HandleAsync(OrderSummaryQuery query, CancellationToken ct) =>
        readService.GetSummaryAsync(query.OrderId, ct);
}
```

---

## 7. Bounded contexts & anti-corruption layer

A **bounded context** is an explicit boundary within which a domain model applies.
Different contexts may use the same word differently — the ubiquitous language is scoped to its context.

### Integration between contexts
- Prefer **domain events / messages** over direct calls.
- When calling a foreign context directly, wrap the call in an **Anti-Corruption Layer (ACL)** that translates between models.
- Never let a foreign context's types bleed into your domain model.

### ACL example

```csharp
// © Sitecore Corporation A/S. All rights reserved.
namespace Orders.Infrastructure.ExternalServices;

/// <summary>Anti-corruption layer for the Pricing bounded context.</summary>
internal sealed class PricingServiceAdapter(IPricingApiClient client) : IPricingService
{
    // IPricingService is defined in Orders.Domain
    public async Task<Money> GetPriceAsync(ProductId productId, CancellationToken ct)
    {
        var response = await client.FetchPriceAsync(productId.Value, ct); // foreign DTO
        return new Money(response.Price, Currency.FromCode(response.CurrencyCode)); // translate
    }
}
```

---

## 8. Testing domain logic

Domain logic must be unit-testable without infrastructure.

### Principles
- Aggregates and value objects are plain C# — test them directly, no mocks needed.
- Application handlers: mock repositories and unit-of-work; verify domain events dispatched.
- Never test EF mappings in domain tests; test them in integration tests.

### Example

```csharp
namespace Orders.Domain.Tests;

public class OrderTests
{
    [Fact]
    public void Submit_WhenDraft_RaisesOrderSubmittedEvent()
    {
        var order = Order.Create(CustomerId.New(), SampleAddress());
        order.AddLine(ProductId.New(), new Quantity(2), new Money(10, Currency.Usd));

        order.Submit();

        order.DomainEvents.Should().ContainSingle(e => e is OrderSubmitted);
        order.Status.Should().Be(OrderStatus.Submitted);
    }

    [Fact]
    public void Submit_WithNoLines_ThrowsDomainException()
    {
        var order = Order.Create(CustomerId.New(), SampleAddress());

        var act = () => order.Submit();

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AddLine_WhenSubmitted_ThrowsDomainException()
    {
        var order = SubmittedOrder();

        var act = () => order.AddLine(ProductId.New(), new Quantity(1), new Money(5, Currency.Usd));

        act.Should().Throw<DomainException>();
    }

    private static Order SubmittedOrder()
    {
        var o = Order.Create(CustomerId.New(), SampleAddress());
        o.AddLine(ProductId.New(), new Quantity(1), new Money(1, Currency.Usd));
        o.Submit();
        return o;
    }

    private static Address SampleAddress() =>
        new("123 Main St", "Springfield", "IL", "62701", "US");
}
```
