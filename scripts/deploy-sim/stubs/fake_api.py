#!/usr/bin/env python3
"""deploy-sim fake API: answers the health endpoints the deploy script polls.

--mode ready        GET /health/ready -> 200, /health/live -> 200
--mode never-ready  GET /health/ready -> 503 forever (startup that never completes)
Everything else -> 401 (like an unauthenticated API call).
"""
import argparse
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, required=True)
    parser.add_argument("--mode", choices=["ready", "never-ready"], default="ready")
    args = parser.parse_args()

    class Handler(BaseHTTPRequestHandler):
        def _send(self, code: int, body: dict) -> None:
            payload = json.dumps(body).encode()
            self.send_response(code)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(payload)))
            self.end_headers()
            self.wfile.write(payload)

        def do_GET(self) -> None:  # noqa: N802
            if self.path == "/health/live":
                self._send(200, {"status": "Healthy"})
            elif self.path == "/health/ready":
                if args.mode == "ready":
                    self._send(200, {"status": "Healthy"})
                else:
                    self._send(503, {"status": "Unhealthy", "checks": {"startup": "seeders still running (simulated)"}})
            else:
                self._send(401, {"title": "Unauthorized"})

        def log_message(self, fmt: str, *a) -> None:
            print("fake-api: " + fmt % a, flush=True)

    print(f"fake-api listening on 127.0.0.1:{args.port} mode={args.mode}", flush=True)
    ThreadingHTTPServer(("127.0.0.1", args.port), Handler).serve_forever()


if __name__ == "__main__":
    main()
