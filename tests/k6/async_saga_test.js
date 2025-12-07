import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';
import { randomIntBetween } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.1/index.js';

// Custom metrics
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
