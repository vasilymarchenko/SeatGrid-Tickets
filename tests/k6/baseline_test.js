import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';
import { randomIntBetween } from 'https://jslib.k6.io/k6-utils/1.2.0/index.js';
import { textSummary } from 'https://jslib.k6.io/k6-summary/0.0.1/index.js';

// Custom metrics for tracking response types
const bookingSuccess = new Counter('booking_success_200');
const bookingConflict = new Counter('booking_conflict_409');
const bookingBadRequest = new Counter('booking_bad_request_400');
const bookingServerError = new Counter('booking_server_error_5xx');
const bookingOther = new Counter('booking_other_status');
const seatsRetrieved = new Counter('seats_retrieved_200');
const seatsError = new Counter('seats_error');

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
    Name: `Load Test Event ${new Date().toISOString()}`,
    Date: new Date().toISOString(),
    Rows: 20,
    Cols: 20, // 400 seats
  });

  const params = {
    headers: {
      'Content-Type': 'application/json',
    },
  };

  const res = http.post(`${BASE_URL}/api/Events`, payload, params);
  
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
  const seatsRes = http.get(`${BASE_URL}/api/Events/${eventId}/seats`);
  
  check(seatsRes, {
    'seats retrieved': (r) => r.status === 200,
  });

  // Track seat retrieval metrics
  if (seatsRes.status === 200) {
    seatsRetrieved.add(1);
  } else {
    seatsError.add(1);
  }

  // 2. Try to book random seats
  const row = randomIntBetween(1, 20).toString();
  const col = randomIntBetween(1, 20).toString();
  const userId = `user-${__VU}-${__ITER}`;

  const bookingPayload = JSON.stringify({
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

  const bookingRes = http.post(`${BASE_URL}/api/Bookings`, bookingPayload, params);

  // Track booking response metrics
  if (bookingRes.status === 200) {
    bookingSuccess.add(1);
  } else if (bookingRes.status === 409) {
    bookingConflict.add(1);
  } else if (bookingRes.status === 400) {
    bookingBadRequest.add(1);
  } else if (bookingRes.status >= 500) {
    bookingServerError.add(1);
  } else {
    bookingOther.add(1);
  }

  // We expect 200 OK or 409 Conflict (if taken)
  check(bookingRes, {
    'booking processed': (r) => r.status === 200 || r.status === 409,
  });

  sleep(1);
}

export function handleSummary(data) {
  const bookingSuccessCount = data.metrics.booking_success_200?.values.count || 0;
  const bookingConflictCount = data.metrics.booking_conflict_409?.values.count || 0;
  const bookingBadRequestCount = data.metrics.booking_bad_request_400?.values.count || 0;
  const bookingServerErrorCount = data.metrics.booking_server_error_5xx?.values.count || 0;
  const bookingOtherCount = data.metrics.booking_other_status?.values.count || 0;
  const seatsRetrievedCount = data.metrics.seats_retrieved_200?.values.count || 0;
  const seatsErrorCount = data.metrics.seats_error?.values.count || 0;
  
  const totalBookingRequests = bookingSuccessCount + bookingConflictCount + 
    bookingBadRequestCount + bookingServerErrorCount + bookingOtherCount;
  
  return {
    'stdout': textSummary(data, { indent: ' ', enableColors: true }) + '\n\n' +
      '='.repeat(80) + '\n' +
      'RESPONSE STATISTICS\n' +
      '='.repeat(80) + '\n\n' +
      'Seat Retrieval Requests:\n' +
      `  ✓ 200 OK (Success):        ${seatsRetrievedCount}\n` +
      `  ✗ Errors:                  ${seatsErrorCount}\n\n` +
      'Booking Requests:\n' +
      `  ✓ 200 OK (Success):        ${bookingSuccessCount} (${totalBookingRequests > 0 ? ((bookingSuccessCount/totalBookingRequests)*100).toFixed(2) : 0}%)\n` +
      `  ⚠ 409 Conflict:            ${bookingConflictCount} (${totalBookingRequests > 0 ? ((bookingConflictCount/totalBookingRequests)*100).toFixed(2) : 0}%)\n` +
      `  ✗ 400 Bad Request:         ${bookingBadRequestCount} (${totalBookingRequests > 0 ? ((bookingBadRequestCount/totalBookingRequests)*100).toFixed(2) : 0}%)\n` +
      `  ✗ 5xx Server Error:        ${bookingServerErrorCount} (${totalBookingRequests > 0 ? ((bookingServerErrorCount/totalBookingRequests)*100).toFixed(2) : 0}%)\n` +
      `  ? Other Status Codes:      ${bookingOtherCount} (${totalBookingRequests > 0 ? ((bookingOtherCount/totalBookingRequests)*100).toFixed(2) : 0}%)\n` +
      `  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n` +
      `  Total Booking Requests:    ${totalBookingRequests}\n\n` +
      '='.repeat(80) + '\n',
  };
}
