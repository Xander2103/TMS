#!/usr/bin/env python3
"""deploy-sim stand-in for the public nginx (deploy/nginx/transportationservice.conf).

nginx runs as www-data, which is NOT the owner of dist/. The simulation cannot switch users,
so this server enforces the same outcome with the 'other' permission bits: every directory from
the root down must be o+x and the file o+r, otherwise 403 — exactly what nginx answered for the
0750/0640 assets. Locations follow the nginx site:
  /assets/  try_files $uri =404
  /sw.js    try_files $uri =404
  /         try_files $uri $uri/ /index.html   (SPA fallback)
If the file <state>/web-mode contains 'assets-fallback', /assets/ also falls back to index.html
(a misconfigured catch-all that serves HTML for a missing .js).
"""
import argparse, os, stat
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlsplit

TYPES = {'.html': 'text/html; charset=utf-8', '.js': 'application/javascript', '.css': 'text/css',
         '.svg': 'image/svg+xml', '.json': 'application/json', '.webmanifest': 'application/manifest+json'}

def readable_by_other(root, rel):
    """None if the file does not exist, False if 'other' cannot reach/read it, True otherwise."""
    path = root
    try:
        if not os.stat(path).st_mode & stat.S_IXOTH:
            return False
        parts = [p for p in rel.split('/') if p]
        for i, part in enumerate(parts):
            path = os.path.join(path, part)
            st = os.stat(path)
            last = i == len(parts) - 1
            if stat.S_ISDIR(st.st_mode):
                if not st.st_mode & stat.S_IXOTH:
                    return False
                if last:
                    return None  # directory, not a file
            elif last:
                return bool(st.st_mode & stat.S_IROTH)
            else:
                return None
    except FileNotFoundError:
        return None
    except NotADirectoryError:
        return None
    return None

class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass

    def send(self, code, body=b'', ctype='text/html; charset=utf-8'):
        self.send_response(code)
        self.send_header('Content-Type', ctype)
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        if self.command != 'HEAD':
            self.wfile.write(body)

    def serve_file(self, rel):
        state = readable_by_other(self.server.root, rel)
        if state is False:
            self.send(403, b'<html><body>403 Forbidden</body></html>')
            return True
        if state is None:
            return False
        with open(os.path.join(self.server.root, rel.lstrip('/')), 'rb') as f:
            body = f.read()
        self.send(200, body, TYPES.get(os.path.splitext(rel)[1], 'application/octet-stream'))
        return True

    def do_GET(self):
        path = unquote(urlsplit(self.path).path)
        if '..' in path.split('/') or any(p.startswith('.') for p in path.split('/') if p):
            self.send(403, b'forbidden'); return
        mode = ''
        try:
            with open(self.server.mode_file) as f:
                mode = f.read().strip()
        except OSError:
            pass
        exact_only = (path.startswith('/assets/') and mode != 'assets-fallback') or path == '/sw.js'
        if path.endswith('/'):
            path = path + 'index.html'
        if self.serve_file(path):
            return
        if exact_only:
            self.send(404, b'<html><body>404 Not Found</body></html>'); return
        if not self.serve_file('/index.html'):
            self.send(404, b'<html><body>404 Not Found</body></html>')

    do_HEAD = do_GET

if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--port', type=int, required=True)
    ap.add_argument('--root', required=True)
    ap.add_argument('--mode-file', required=True)
    a = ap.parse_args()
    srv = ThreadingHTTPServer(('127.0.0.1', a.port), Handler)
    srv.root, srv.mode_file = a.root, a.mode_file
    srv.serve_forever()
