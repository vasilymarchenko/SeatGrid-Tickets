# Project Requirements: SeatGrid Ticketing Platform

## 1. Executive Summary
**SeatGrid** is a high-performance ticketing platform designed to handle "flash sales" for major entertainment events. Imagine a Taylor Swift concert or the World Cup final going on sale: millions of fans arrive at the exact same second to buy a limited number of seats.

Our core business problem is **Concurrency at Scale**. We need to sell tickets fairly, quickly, and without ever selling the same seat to two different people (double-booking), even when 100,000 people are clicking "Buy" simultaneously.

---

## 2. Domain Concepts (The "Language" of the System)

Before we talk about features, here are the key terms we use:

*   **Event**: A specific show at a specific time (e.g., "Metallica, Wembley Stadium, June 15th").
*   **Venue**: The physical location. For this project, we simplify a Venue into a **Grid** of seats (Rows × Columns).
*   **Seat**: A specific spot (e.g., Row 5, Seat 12). A seat has a **Status**:
    *   `Available`: Ready to be bought.
    *   `Reserved`: A user has added it to their cart. It is "held" for them for a short time (e.g., 10 minutes). No one else can buy it.
    *   `Booked`: Successfully paid for and owned by a user.
*   **Booking/Order**: The record of a user purchasing specific seats.
*   **Manifest**: The complete list of all seats and their current status for an event.

---

## 3. Functional Requirements (What the system must do)

### A. The Manager Experience (Back Office)
1.  **Create Event**:
    *   I need to define an event name, date, and venue size (e.g., 100 rows x 100 columns = 10,000 seats).
    *   *Simplification*: Assume all seats cost the same for now.

### B. The User Experience (The Fan)
1.  **View Event Availability**:
    *   I want to see a list of events.
    *   When I click an event, I need to see the **Seat Map** showing which seats are taken and which are free.
    *   *Critical*: This data must be reasonably fresh. I shouldn't see a seat as "Available" if it was sold an hour ago.
2.  **Book a Seat**:
    *   I select specific seats (e.g., Row 10, Seats 1-2).
    *   I click "Buy".
    *   **The System Must**: Instantly check if they are still free. If yes, **Reserve** them for me so no one else can grab them while I pay.
3.  **Payment (Mocked)**:
    *   After reserving, the system processes my payment.
    *   If payment succeeds -> Seats become `Booked`. I get a confirmation.
    *   If payment fails (or I take too long) -> Seats become `Available` again for others.

---

## 4. Non-Functional Requirements (The "Hard" Part)

This is where the "High Load" course concepts apply.

### 1. Scalability (The "Flash Sale" Requirement)
*   **Traffic Pattern**: The system will be idle most of the time. But at **10:00 AM** on Friday, traffic will spike from 0 to **100,000 Requests Per Second (RPS)** in seconds.
*   **Goal**: The system must not crash. It can slow down slightly, but it must keep processing orders.

### 2. Consistency (The "No Double-Booking" Rule)
*   **Strict Requirement**: Under no circumstances can two users hold a ticket for the same seat.
*   **Trade-off**: It is better to tell a user "Sorry, sold out" (even if there might be 1 seat left that we aren't sure about) than to take their money and have no seat for them.

### 3. Latency (Speed)
*   **Read Operations (Viewing Map)**: Must be extremely fast (< 50ms). Users refresh the page constantly.
*   **Write Operations (Booking)**: Can be slower (< 500ms), but the user needs immediate feedback that their request is "in progress".

### 4. Observability
*   We need to know *immediately* if the database is locking up or if the payment service is failing. We cannot fly blind during a sale.

---

## 5. Implementation Constraints (For your Learning Project)

To keep this manageable as a one-person project:
*   **No User Accounts**: Just pass a random `UserId` string in your API headers.
*   **No Frontend UI**: Use API tools (Postman/Swagger) or a simple CLI script to act as the user.
*   **Mocked External Services**: Do not integrate real Stripe/PayPal. Create a "PaymentService" that just sleeps for 200ms and returns "Success" 90% of the time.
