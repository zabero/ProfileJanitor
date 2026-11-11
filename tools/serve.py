"""Short-lived file exchange with a test VM: GET serves files from a directory, PUT stores PNG screenshots in its 'in' subfolder.
It listens on one address only, answers one client only, and shuts itself down after the given time.

Usage: python -I tools/serve.py <directory> <seconds> <bind-ip> <allowed-client-ip>
  on the VM:  curl.exe -s -o ProfileJanitor.exe http://<bind-ip>:8765/ProfileJanitor.exe     (download)
              curl.exe -s -T shot.png http://<bind-ip>:8765/shot.png                         (upload -> <directory>/in/shot.png)
"""
import os, sys, threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer

PORT = 8765
root = os.path.abspath(sys.argv[1])
seconds = int(sys.argv[2])
BIND = sys.argv[3]      # this computer's address on the test network (never 0.0.0.0)
ALLOWED = sys.argv[4]   # the test VM - nobody else gets an answer
os.makedirs(os.path.join(root, "in"), exist_ok=True)


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *a, **k):
        super().__init__(*a, directory=root, **k)

    def parse_request(self):
        if self.client_address[0] != ALLOWED:
            self.send_error(403)
            return False
        return super().parse_request()

    def list_directory(self, path):
        self.send_error(404)  # no directory listings

    def do_PUT(self):
        name = os.path.basename(self.path)  # file name only: uploads cannot leave the 'in' folder
        size = int(self.headers.get("Content-Length", 0))
        if not name.lower().endswith(".png") or size > 20 * 1024 * 1024:  # screenshots only
            self.send_error(400)
            return
        with open(os.path.join(root, "in", name), "wb") as f:
            f.write(self.rfile.read(size))
        self.send_response(201)
        self.end_headers()


server = ThreadingHTTPServer((BIND, PORT), Handler)
threading.Timer(seconds, server.shutdown).start()
server.serve_forever()
