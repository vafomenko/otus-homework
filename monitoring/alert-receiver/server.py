"""Local Grafana webhook sink: real firing/resolved notifications go to pod logs."""
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, HTTPServer
import json


class Receiver(BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200 if self.path == "/health" else 404)
        self.end_headers()

    def do_POST(self):
        if self.path != "/alerts":
            self.send_error(404)
            return
        length = int(self.headers.get("Content-Length", "0"))
        if length > 1_000_000:
            self.send_error(413)
            return
        try:
            payload = json.loads(self.rfile.read(length))
        except (ValueError, UnicodeDecodeError):
            self.send_error(400)
            return
        print(json.dumps({"received_at": datetime.now(timezone.utc).isoformat(),
                          "notification": payload}, ensure_ascii=False), flush=True)
        self.send_response(200)
        self.end_headers()
        self.wfile.write(b"ok")


HTTPServer(("0.0.0.0", 8080), Receiver).serve_forever()
