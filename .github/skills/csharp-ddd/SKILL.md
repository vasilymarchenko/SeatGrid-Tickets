---
name: csharp-ddd
description: >
  Use alongside the csharp skill for any task involving domain modelling in C#.
  Trigger on: aggregate, entity, value object, domain event, repository, domain service,
  bounded context, ubiquitous language, CQRS, clean architecture, anemic model,
  invariant, or any request to design or refactor the domain layer.
  Always pair with the csharp skill — this one adds DDD rules on top.
---

# C# — Domain-Driven Design

This skill extends the `csharp` skill with DDD rules. Apply both together.

**No Sitecore copyright header** on any file in this project.

For patterns, examples, and layer rules — read `references/ddd.md`.

## Checklist — before delivering any domain change

- [ ] Aggregate enforces all its invariants; invalid state cannot be constructed
- [ ] No public setters on entities or aggregates
- [ ] Value objects are immutable `record` types with value-equality
- [ ] Domain events are raised inside the aggregate, not in the Application layer
- [ ] Repository interface lives in Domain; implementation in Infrastructure
- [ ] Application service contains zero business logic
- [ ] No cross-aggregate direct object references — IDs only
- [ ] Ubiquitous language used consistently: type names match domain terms
