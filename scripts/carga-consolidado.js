// Teste de carga do requisito: 50 req/s no consolidado, com ate 5% de falhas.
// Precisa do k6 (https://k6.io). Uso:
//   k6 run -e TOKEN=<jwt> scripts/carga-consolidado.js
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  scenarios: {
    pico: {
      executor: 'constant-arrival-rate',
      rate: 50,
      timeUnit: '1s',
      duration: '60s',
      preAllocatedVUs: 50,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.05'],
    http_req_duration: ['p(95)<200'],
  },
};

const hoje = new Date().toISOString().slice(0, 10);

export default function () {
  const res = http.get(`http://localhost:5102/api/consolidado/${hoje}`, {
    headers: { Authorization: `Bearer ${__ENV.TOKEN}` },
  });
  check(res, { 'status 200': (r) => r.status === 200 });
}
