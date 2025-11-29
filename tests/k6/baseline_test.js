import http from 'k6/http';
import { check, sleep } from 'k6';
import { randomIntBetween } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';

export const options = {
  stages: [
    { duration: '10s', target: 10 }, // Ramp up to 10 users
    { duration: '30s', target: 10 }, // Stay at 10 users
    { duration: '10s', target: 0 },  // Ramp down
  ],
};

const BASE_URL = 'http://localhost:5000';

export function setup() {
  // Create a new event for this test run
  const payload = JSON.stringify({
    name: `Load Test Event ${new Date().toISOString()}`,
    date: new Date().toISOString(),
    rows: 20,
    cols: 20, // 400 seats
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
    },
  };

  const res = http.post(`${BASE_URL}/api/events`, payload, params);
  
  check(res, {
    'event created': (r) => r.status === 201,
  });

  if (res.status !== 201) {
      throw new Error(`Failed to create event: ${res.body}`);
  }

  const eventId = res.json('id');
  console.log(`Created Event ID: ${eventId}`);
  return { eventId };
}

export default function (data) {
  const eventId = data.eventId;

  // 1. View Seat Map
  const seatsRes = http.get(`${BASE_URL}/api/events/${eventId}/seats`);
  
  check(seatsRes, {
    'seats retrieved': (r) => r.status === 200,
  });

  // 2. Try to book random seats
  const row = randomIntBetween(1, 20).toString();
  const col = randomIntBetween(1, 20).toString();
  const userId = `user-${__VU}-${__ITER}`;

  const bookingPayload = JSON.stringify({
    eventId: eventId,
    userId: userId,
    seats: [
      { row: row, col: col }
    ]
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
    },
  };

  const bookingRes = http.post(`${BASE_URL}/api/bookings`, bookingPayload, params);

  // We expect 200 OK or 409 Conflict (if taken)
  check(bookingRes, {
    'booking processed': (r) => r.status === 200 || r.status === 409,
  });

  sleep(1);
}
