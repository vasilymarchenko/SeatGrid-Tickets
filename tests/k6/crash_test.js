import http from 'k6/http';
import { check, sleep } from 'k6';
import { randomIntBetween } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

// Configuration
const BASE_URL = 'http://localhost:5025';
const TOTAL_SEATS = 100; // 10x10
const TARGET_VUS = 2000; // Simulate high concurrency

export const options = {
  scenarios: {
    crash_test: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '10s', target: TARGET_VUS }, // Ramp up fast
        { duration: '20s', target: TARGET_VUS }, // Hold the load
        { duration: '10s', target: 0 },          // Ramp down
      ],
      gracefulRampDown: '5s',
    },
  },
  thresholds: {
    // We expect many failures (conflicts), so we don't fail the test on 409s.
    // But we want to see if the system crashes (500s).
    'http_req_duration': ['p(95)<2000'], // Latency might spike
  },
};

export function setup() {
  // 1. Create a new Event for this test run
  const payload = JSON.stringify({
    Name: `Crash Test Event ${new Date().toISOString()}`,
    Date: new Date().toISOString(),
    Rows: 10,
    Cols: 10
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
    },
  };

  const res = http.post(`${BASE_URL}/api/Events`, payload, params);
  
  check(res, {
    'Event created successfully': (r) => r.status === 201,
  });

  if (res.status !== 201) {
    throw new Error(`Failed to create event: ${res.body}`);
  }

  const eventId = res.json('id');
  console.log(`Created Event ID: ${eventId} with ${TOTAL_SEATS} seats.`);
  return { eventId };
}

export default function (data) {
  const eventId = data.eventId;
  
  // 2. Pick a random seat to book
  // Rows 1-10, Cols 1-10
  const row = randomIntBetween(1, 10).toString();
  const col = randomIntBetween(1, 10).toString();
  const userId = `user-${__VU}`; // Virtual User ID

  const payload = JSON.stringify({
    EventId: eventId,
    UserId: userId,
    Seats: [
      { Row: row, Col: col }
    ]
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
    },
  };

  const res = http.post(`${BASE_URL}/api/Bookings`, payload, params);

  // 3. Check results
  check(res, {
    'Status is 200 (Booked) or 409 (Conflict)': (r) => r.status === 200 || r.status === 409,
    'System did not crash (No 500s)': (r) => r.status !== 500,
  });

  // Small sleep to pace the requests slightly, or remove for maximum chaos
  sleep(0.1);
}
