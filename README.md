# SeatGrid - High-Load Ticketing System

**SeatGrid** is a learning project designed to simulate a high-concurrency ticketing platform (similar to Ticketmaster). The goal is to build a system capable of handling "flash sales" where thousands of users compete for limited inventory simultaneously, focusing on distributed systems challenges like concurrency, consistency, and high availability.

## Project Overview

*   **Core Challenge**: Prevent double-booking while handling traffic spikes (e.g., 100k RPS).
*   **Architecture**: Evolves from a naive monolith to a distributed microservices architecture.
*   **Tech Stack**: .NET 8/9, PostgreSQL, Redis, RabbitMQ/Kafka, Kubernetes, OpenTelemetry.

## Roadmap

This project follows a phased implementation plan:

1.  **Phase 1: The Naive Monolith** - A baseline implementation to establish functionality.
2.  **Phase 2: Observability & The "Crash"** - Stress testing with k6 to identify bottlenecks.
3.  **Phase 3: Read Optimization** - Implementing caching (Redis) and efficient data formats.
4.  **Phase 4: Write Optimization** - Handling the "Thundering Herd" with message queues.
5.  **Phase 5: Distributed Transactions** - Implementing Sagas for data consistency.
6.  **Phase 6: Sharding & HA** - Database scaling strategies.
7.  **Phase 7: Analytics** - Big data ingestion with ClickHouse.

## Getting Started

*Instructions for running the project locally will be added here.*

## Documentation

*   [Project Requirements](Docs/project-requirements.md)
*   [Learning Plan](Docs/learning-project-plan.md)
