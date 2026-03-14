/**
 * Async Saga (Choreography) Load Test
 *
 * This test exercises the full "Reservation First" booking flow:
 *
 *   1. POST /api/Bookings
 *        → BookingsController atomically reserves the seat in Redis (Lua, TTL=120s).
 *        → If the seat is already held: instant 409 Conflict (no message is published).
 *        → If reserved: BookingInitiated is published to RabbitMQ, 202 Accepted returned.
 *           The user is told to "wait for payment confirmation" — but there is NO push
 *           notification. The client must poll to learn the final outcome (see teardown).
 *
 *   2. PaymentConsumer (SeatGrid.PaymentService)
 *        → Consumes BookingInitiated; simulates ~2s processing delay.
 *        → 85% success  → publishes PaymentSucceeded
 *        → 15% failure  → publishes PaymentFailed  ("Card Declined")
 *
 *   3. BookingFinalizerConsumer (SeatGrid.API)
 *        → PaymentSucceeded:
 *             a. INSERT booking to PostgreSQL (pessimistic lock, idempotent).
 *             b. AddBookedSeatsAsync → sets Redis field value to DateTime.MaxValue ticks
 *                (marks seat as permanently booked in stale-detection logic; key TTL: 24h).
 *             c. DB failure → ReleaseSeatsAsync (compensation, seat freed).
 *        → PaymentFailed:
 *             ReleaseSeatsAsync → removes the 120s reservation, seat immediately available.
 *
 *   Background: CacheReconciliationService (every 60s)
 *        → Scans for "ghost" reservations (reserved in Redis, Available in DB) and releases them.
 *
 * What the test can and cannot observe:
 *   - A 202 response means the RESERVATION succeeded, NOT that payment will succeed.
 *   - A 409 response means the seat is currently held by another user (or permanently booked).
 *   - The test has no way to track individual booking outcomes — the saga completes
 *     asynchronously after the HTTP response. Final state is only visible via teardown polling.
 */

import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';
import { randomIntBetween } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.1/index.js';

// Custom metrics — track saga entry-point outcomes only (not final booking outcomes).
// A 202 count does NOT equal confirmed bookings; ~15% will roll back via PaymentFailed.
const bookingAccepted = new Counter('booking_accepted_202');
const bookingConflict = new Counter('booking_conflict_409');
const bookingError = new Counter('booking_error_5xx');

// Configuration
const BASE_URL = 'http://localhost:5000';
const TOTAL_SEATS = 100; // 10x10
const TARGET_VUS = 2000;

export const options = {
  scenarios: {
    async_booking: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '10s', target: TARGET_VUS },
        { duration: '20s', target: TARGET_VUS },
        { duration: '10s', target: 0 },
      ],
      gracefulRampDown: '5s',
    },
  },
  thresholds: {
    'http_req_duration': ['p(95)<500'], // Should be very fast (Redis only)
  },
};

/**
 * setup() — runs once before the load test begins.
 * Creates a fresh 10×10 event (100 seats) so all seats start as Available.
 * Returns the eventId shared across all VUs via the `data` parameter.
 */
export function setup() {
  const payload = JSON.stringify({
    Name: `Async Saga Test ${new Date().toISOString()}`,
    Date: new Date().toISOString(),
    Rows: 10,
    Cols: 10
  });

  const params = { headers: { 'Content-Type': 'application/json' } };
  const res = http.post(`${BASE_URL}/api/Events`, payload, params);
  
  if (res.status !== 201) {
    throw new Error(`Failed to create event: ${res.body}`);
  }

  const eventId = res.json('id');
  console.log(`Created Event ID: ${eventId}`);
  return { eventId };
}

/**
 * default() — the main VU loop. Each iteration is one booking attempt (Saga step 1).
 *
 * Each VU picks a random seat and fires POST /api/Bookings:
 *   202 → Redis reservation succeeded; BookingInitiated published. Payment outcome unknown yet.
 *   409 → Seat is currently locked (reserved or permanently booked). Fast rejection from Redis.
 *
 * Both 202 and 409 are treated as "correct" behaviour — the check passes for both.
 * A 5xx would indicate a system error (e.g. Redis or RabbitMQ unavailable).
 *
 * Note: a single VU always uses the same userId (`user-<VU>`), so if it retries
 * the same seat it just hit, BookingFinalizerConsumer's idempotency check will
 * treat it as a no-op on the DB side.
 */
export default function (data) {
  const eventId = data.eventId;
  const row = randomIntBetween(1, 10).toString();
  const col = randomIntBetween(1, 10).toString();
  const userId = `user-${__VU}`;

  const payload = JSON.stringify({
    EventId: eventId,
    UserId: userId,
    Seats: [{ Row: row, Col: col }]
  });

  const params = { headers: { 'Content-Type': 'application/json' } };
  const res = http.post(`${BASE_URL}/api/Bookings`, payload, params);

  if (res.status === 202) {
    bookingAccepted.add(1);
  } else if (res.status === 409) {
    bookingConflict.add(1);
  } else if (res.status >= 500) {
    bookingError.add(1);
  }

  check(res, {
    'Status is 202 or 409': (r) => r.status === 202 || r.status === 409,
  });

  sleep(0.1);
}

/**
 * teardown() — polling substitute for customer notification.
 *
 * Because the system has no push notification (no SignalR, no order-status endpoint),
 * the only way to observe saga completion is to poll the seat-status endpoint after
 * enough time has passed for the async pipeline to drain:
 *
 *   POST /api/Bookings → 202                     (t=0)
 *   PaymentConsumer processes (~2s delay)         (t≈2s)
 *   BookingFinalizerConsumer writes to DB/Redis   (t≈2-3s)
 *
 * Waiting 10s is intentionally conservative to account for queue backlog under load.
 *
 * Expected final state:
 *   All 100 seats Booked — even with 15% payment failures, compensation releases seats
 *   fast enough for subsequent users to claim them, resulting in 100% occupancy.
 */
export function teardown(data) {
  const eventId = data.eventId;
  console.log('Waiting 10s for payments to process...');
  sleep(10);

  console.log('Validating final state...');
  const res = http.get(`${BASE_URL}/api/Events/${eventId}/seats`);
  
  if (res.status === 200) {
    const seats = res.json();
    const bookedCount = seats.filter(s => s.status === 'Booked').length;
    const availableCount = seats.filter(s => s.status === 'Available').length;
    
    console.log('='.repeat(80));
    console.log(`FINAL VALIDATION (Event ${eventId})`);
    console.log(`Total Seats: ${seats.length}`);
    console.log(`Booked:      ${bookedCount} (Expected ~85)`);
    console.log(`Available:   ${availableCount} (Expected ~15)`);
    console.log('='.repeat(80));
  } else {
    console.error(`Failed to fetch seats: ${res.status}`);
  }
}

/**
 * handleSummary() — prints saga-layer metrics alongside the standard k6 summary.
 *
 * Reminder: booking_accepted_202 ≠ confirmed bookings.
 * The final confirmed count is only visible in the teardown seat-status poll above.
 */
export function handleSummary(data) {
    const accepted = data.metrics.booking_accepted_202?.values.count || 0;
    const conflict = data.metrics.booking_conflict_409?.values.count || 0;
    const error = data.metrics.booking_error_5xx?.values.count || 0;
    const total = accepted + conflict + error;

    return {
        'stdout': textSummary(data, { indent: ' ', enableColors: true }) + '\n\n' +
        'ASYNC SAGA TEST RESULTS\n' +
        '-----------------------\n' +
        `Total Requests: ${total}\n` +
        `Accepted (202): ${accepted}\n` +
        `Conflict (409): ${conflict}\n` +
        `Errors (5xx):   ${error}\n`
    };
}
