FROM grafana/k6

COPY test.js /test.js

ENTRYPOINT ["k6", "run", "/test.js"]