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

// Configuration
const BASE_URL = 'http://localhost:5000';
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

  // Track booking response metrics
  if (res.status === 200) {
    bookingSuccess.add(1);
  } else if (res.status === 409) {
    bookingConflict.add(1);
  } else if (res.status === 400) {
    bookingBadRequest.add(1);
  } else if (res.status >= 500) {
    bookingServerError.add(1);
  } else {
    bookingOther.add(1);
  }

  // 3. Check results
  check(res, {
    'Status is 200 (Booked) or 409 (Conflict)': (r) => r.status === 200 || r.status === 409,
    'System did not crash (No 500s)': (r) => r.status !== 500,
  });

  // Small sleep to pace the requests slightly, or remove for maximum chaos
  sleep(0.1);
}

export function handleSummary(data) {
  const bookingSuccessCount = data.metrics.booking_success_200?.values.count || 0;
  const bookingConflictCount = data.metrics.booking_conflict_409?.values.count || 0;
  const bookingBadRequestCount = data.metrics.booking_bad_request_400?.values.count || 0;
  const bookingServerErrorCount = data.metrics.booking_server_error_5xx?.values.count || 0;
  const bookingOtherCount = data.metrics.booking_other_status?.values.count || 0;
  
  const totalRequests = bookingSuccessCount + bookingConflictCount + 
    bookingBadRequestCount + bookingServerErrorCount + bookingOtherCount;
  
  const successRate = totalRequests > 0 ? ((bookingSuccessCount/totalRequests)*100).toFixed(2) : 0;
  const conflictRate = totalRequests > 0 ? ((bookingConflictCount/totalRequests)*100).toFixed(2) : 0;
  
  return {
    'stdout': textSummary(data, { indent: ' ', enableColors: true }) + '\n\n' +
      '='.repeat(80) + '\n' +
      'CRASH TEST - RESPONSE STATISTICS\n' +
      '='.repeat(80) + '\n\n' +
      'Booking Requests Under High Load:\n' +
      `  ✓ 200 OK (Success):        ${bookingSuccessCount.toString().padEnd(10)} (${successRate}%)\n` +
      `  ⚠ 409 Conflict:            ${bookingConflictCount.toString().padEnd(10)} (${conflictRate}%)\n` +
      `  ✗ 400 Bad Request:         ${bookingBadRequestCount.toString().padEnd(10)} (${totalRequests > 0 ? ((bookingBadRequestCount/totalRequests)*100).toFixed(2) : 0}%)\n` +
      `  ✗ 5xx Server Error:        ${bookingServerErrorCount.toString().padEnd(10)} (${totalRequests > 0 ? ((bookingServerErrorCount/totalRequests)*100).toFixed(2) : 0}%)\n` +
      `  ? Other Status Codes:      ${bookingOtherCount.toString().padEnd(10)} (${totalRequests > 0 ? ((bookingOtherCount/totalRequests)*100).toFixed(2) : 0}%)\n` +
      `  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n` +
      `  Total Requests:            ${totalRequests}\n\n` +
      'Performance Metrics:\n' +
      `  Contention Level:          ${TOTAL_SEATS} seats, ${TARGET_VUS} concurrent VUs\n` +
      `  Successful Bookings:       ${bookingSuccessCount}/${TOTAL_SEATS} seats (${TOTAL_SEATS > 0 ? ((bookingSuccessCount/TOTAL_SEATS)*100).toFixed(2) : 0}% capacity)\n\n` +
      '='.repeat(80) + '\n',
  };
}
