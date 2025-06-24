import http from 'k6/http';
import { check } from 'k6';

export const options = {
  vus: 50,
  duration: '30s',
};

export default function () {
  // Generar un voter_id random para simular diferentes usuarios
  const voter_id = Math.floor(Math.random() * 1000000);

  const headers = {
    'Content-Type': 'application/x-www-form-urlencoded',
    'Cookie': `voter_id=${voter_id}`,
  };

  const payload = `vote=a`; 

  const url = 'http://test.antonellabrochini.com/vote/'; 

  const res = http.post(url, payload, { headers });

  check(res, {
    'status 200': (r) => r.status === 200,
  });
}
