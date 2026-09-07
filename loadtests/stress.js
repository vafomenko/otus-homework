import http from 'k6/http';
import { check, sleep } from 'k6';
import exec from 'k6/execution';
import { Counter, Rate } from 'k6/metrics';

const base = __ENV.BASE_URL || 'http://host.docker.internal:8085';
const runId = __ENV.RUN_ID || String(Date.now());
const unexpected = new Rate('unexpected_responses');
const injected = new Counter('injected_500');
const smoke = __ENV.SMOKE === 'true';

export const options = {
  scenarios: {
    crud: smoke
      ? { executor: 'constant-vus', vus: 2, duration: '30s' }
      : { executor: 'ramping-vus', startVUs: 2, gracefulRampDown: '10s',
          stages: [
            { duration: '1m', target: 10 },
            { duration: '2m', target: 30 },
            { duration: '2m', target: 50 },
            { duration: '2m', target: 50 },
            { duration: '2m', target: 20 },
            { duration: '1m', target: 0 },
          ] },
  },
  thresholds: {
    checks: ['rate>0.99'],
    unexpected_responses: ['rate<0.01'],
    'http_req_duration{phase:normal}': ['p(95)<1000'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(95)', 'p(99)', 'max'],
};

function params(name, phase, extras = {}) {
  return { headers: { Host: 'arch.homework', 'Content-Type': 'application/json', ...extras },
           tags: { name, phase }, timeout: '10s' };
}

function verify(response, expected, operation) {
  const ok = response.status === expected;
  unexpected.add(!ok);
  check(response, { [operation]: () => ok });
  return ok;
}

export default function () {
  const seconds = (Date.now() - exec.scenario.startTime) / 1000;
  // 0–5 min: normal/stress; 5–7 min: controlled faults; 7–10 min: recovery.
  const faults = !smoke && __ENV.FAULTS !== 'false' && seconds >= 300 && seconds < 420;
  const phase = faults ? 'faults' : 'normal';
  const name = `k6-${runId}-${__VU}-${__ITER}`;
  const body = { username: name, firstName: 'Load', lastName: 'Test',
                 email: `${name}@example.test`, phone: '+71002003040' };
  const created = http.post(`${base}/user`, JSON.stringify(body), params('POST /user', phase));
  if (!verify(created, 201, 'POST creates user')) { sleep(0.5); return; }
  const id = created.json('id');
  try {
    const delay = faults ? { 'X-Demo-Delay-Ms': '700' } : {};
    verify(http.get(`${base}/user/${id}`, params('GET /user/{id}', phase, delay)), 200, 'GET reads user');

    const error = faults && Math.random() < 0.25;
    const listed = http.get(`${base}/user`, params('GET /user', phase, error ? { 'X-Demo-Error': '500' } : {}));
    verify(listed, error ? 500 : 200, 'GET list returns expected status');
    if (error && listed.status === 500) injected.add(1);

    verify(http.put(`${base}/user/${id}`, JSON.stringify({ ...body, firstName: 'Updated' }),
      params('PUT /user/{id}', phase)), 200, 'PUT updates user');
  } finally {
    verify(http.del(`${base}/user/${id}`, null, params('DELETE /user/{id}', phase)), 204, 'DELETE cleans up user');
  }
  sleep(0.3);
}

export function handleSummary(data) {
  const result = __ENV.K6_SUMMARY_PATH || '/results/k6-summary.json';
  return {
    [result]: JSON.stringify(data, null, 2),
    stdout: `\nRequests: ${data.metrics.http_reqs.values.count}\n` +
      `Unexpected response rate: ${data.metrics.unexpected_responses.values.rate}\n` +
      `Injected HTTP 500: ${data.metrics.injected_500?.values.count || 0}\n` +
      `p95: ${data.metrics.http_req_duration.values['p(95)']} ms\n`,
  };
}
