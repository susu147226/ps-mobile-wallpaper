"""Phase 5 end-to-end check: WebSocket event stream + wallpaper prepare against a live bridge.

Uses only the standard library so it runs on a bare Windows host. The WebSocket client is
deliberately minimal — it performs the RFC 6455 handshake and decodes unmasked text frames,
which is all the bridge ever sends.
"""

import base64
import json
import os
import socket
import struct
import sys
import urllib.error
import urllib.request
import zlib

HOST = "127.0.0.1"
PORT = 18765
API = f"http://{HOST}:{PORT}/api/v1"
TOKEN_PATH = os.path.join(
    os.environ["APPDATA"], "PSMobileWallpaper", "auth.token"
)


def load_token() -> str:
    with open(TOKEN_PATH, "r", encoding="utf-8") as handle:
        return handle.read().strip()


# ---------------------------------------------------------------- REST helpers

def api(method: str, path: str, token: str, body=None):
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(f"{API}{path}", data=data, method=method)
    request.add_header("Accept", "application/json")
    request.add_header("X-PSMW-Token", token)
    if data is not None:
        request.add_header("Content-Type", "application/json")

    with urllib.request.urlopen(request, timeout=180) as response:
        return json.loads(response.read().decode("utf-8"))


# --------------------------------------------------------------- PNG creation

def write_png(path: str, width: int, height: int) -> None:
    """Writes a small RGB gradient PNG using only zlib."""
    rows = []
    for y in range(height):
        green = (y * 255) // max(1, height - 1)
        rows.append(b"\x00" + bytes([0x40, green, 0xC0]) * width)
    raw = b"".join(rows)

    def chunk(tag: bytes, payload: bytes) -> bytes:
        return (
            struct.pack(">I", len(payload))
            + tag
            + payload
            + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)
        )

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)
    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", header)
        + chunk(b"IDAT", zlib.compress(raw, 6))
        + chunk(b"IEND", b"")
    )
    with open(path, "wb") as handle:
        handle.write(png)


# ------------------------------------------------------------ WebSocket client

class MiniWebSocket:
    def __init__(self, path: str):
        self.sock = socket.create_connection((HOST, PORT), timeout=30)
        key = base64.b64encode(os.urandom(16)).decode()
        handshake = (
            f"GET {path} HTTP/1.1\r\n"
            f"Host: {HOST}:{PORT}\r\n"
            "Upgrade: websocket\r\n"
            "Connection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\n"
            "Sec-WebSocket-Version: 13\r\n"
            "Origin: http://127.0.0.1\r\n\r\n"
        )
        self.sock.sendall(handshake.encode())
        self.status = self._read_status()

    def _read_status(self) -> int:
        buffer = b""
        while b"\r\n\r\n" not in buffer:
            chunk = self.sock.recv(4096)
            if not chunk:
                break
            buffer += chunk
        self._pending = buffer.split(b"\r\n\r\n", 1)[1] if b"\r\n\r\n" in buffer else b""
        return int(buffer.split(b" ", 2)[1])

    def recv_text(self, timeout: float = 15.0):
        self.sock.settimeout(timeout)
        frame = self._read_frame()
        if frame is None:
            return None
        fin_opcode, payload = frame
        return payload.decode("utf-8")

    def _read_exact(self, count: int) -> bytes:
        data = self._pending[:count]
        self._pending = self._pending[count:]
        while len(data) < count:
            chunk = self.sock.recv(count - len(data))
            if not chunk:
                raise ConnectionError("socket closed")
            data += chunk
        return data

    def _read_frame(self):
        try:
            header = self._read_exact(2)
        except (ConnectionError, socket.timeout):
            return None

        opcode = header[0] & 0x0F
        length = header[1] & 0x7F
        if length == 126:
            length = struct.unpack(">H", self._read_exact(2))[0]
        elif length == 127:
            length = struct.unpack(">Q", self._read_exact(8))[0]

        masked = bool(header[1] & 0x80)
        mask = self._read_exact(4) if masked else None
        payload = self._read_exact(length)

        if mask:
            payload = bytes(b ^ mask[i % 4] for i, b in enumerate(payload))

        if opcode == 0x8:  # close
            return None

        return opcode, payload

    def close(self):
        try:
            self.sock.close()
        except OSError:
            pass


def main() -> int:
    token = load_token()
    failures = []

    # --- 1. WebSocket must reject an unauthenticated handshake (spec §23) ---
    anon = MiniWebSocket("/ws")
    print(f"[1] WS handshake without token -> HTTP {anon.status} (expect 401)")
    if anon.status != 401:
        failures.append("WebSocket accepted a connection without a token")
    anon.close()

    # --- 2. WebSocket must accept the authenticated handshake ---
    ws = MiniWebSocket(f"/ws?token={token}")
    print(f"[2] WS handshake with token    -> HTTP {ws.status} (expect 101)")
    if ws.status != 101:
        failures.append(f"WebSocket handshake failed with HTTP {ws.status}")

    # --- 3. Build a source image and ask the bridge to crop it (spec §10) ---
    source = os.path.join(os.environ["TEMP"], "psmw_e2e_source.png")
    write_png(source, 1600, 1200)
    print(f"[3] source image               -> {source} (1600x1200)")

    devices = api("GET", "/devices", token)
    if not devices:
        print("    no device attached; stopping here")
        ws.close()
        return 0

    device = devices[0]
    device_id = device["id"]
    display = api("GET", f"/devices/{device_id}/display", token)
    print(f"[4] device                     -> {device['brand']} {device['model']} "
          f"({display['width']}x{display['height']})")

    prepared = api("POST", "/wallpaper/prepare", token,
                   {"deviceId": device_id, "path": source})
    print(f"[5] prepare                    -> {prepared['width']}x{prepared['height']} "
          f"success={prepared['success']}")
    if (prepared["width"], prepared["height"]) != (display["width"], display["height"]):
        failures.append("Prepared size does not match the device screen size")

    # --- 4. The prepare call must have published a WebSocket event (spec §22) ---
    event = ws.recv_text(timeout=15)
    if event is None:
        failures.append("No WebSocket event arrived after /wallpaper/prepare")
    else:
        payload = json.loads(event)
        print(f"[6] WS event                   -> {json.dumps(payload, ensure_ascii=False)}")
        if set(payload.keys()) != {"event", "data"}:
            failures.append(f"Event envelope has unexpected keys: {list(payload.keys())}")

    ws.close()

    print()
    if failures:
        print("FAILURES:")
        for failure in failures:
            print(f"  - {failure}")
        return 1

    print("ALL CHECKS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
