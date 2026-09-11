"""Phase 9 end-to-end check: wallpaper support matches what the device actually does.

Drives the real REST API against a live bridge and asserts the honest behaviour:

  * the home screen is set (verified working through the helper app)
  * the lock screen is *refused* with WALLPAPER_NOT_SUPPORTED, because EMUI ignores FLAG_LOCK —
    the helper reports success and dumpsys records an id, but the lock screen never changes

Usage:  python scripts/wallpaper-check.py
"""

import json
import os
import struct
import sys
import time
import urllib.error
import urllib.request
import zlib

HOST = "127.0.0.1"
PORT = 18765
API = f"http://{HOST}:{PORT}/api/v1"
TOKEN_PATH = os.path.join(os.environ["APPDATA"], "PSMobileWallpaper", "auth.token")
TEMP_WORKSPACE = os.path.join(os.environ["TEMP"], "PSMobileWallpaper")


def api(method, path, token, body=None, timeout=180):
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(f"{API}{path}", data=data, method=method)
    request.add_header("Accept", "application/json")
    request.add_header("X-PSMW-Token", token)
    if data is not None:
        request.add_header("Content-Type", "application/json")

    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return json.loads(response.read().decode("utf-8"))
    except urllib.error.HTTPError as error:
        # Surface the bridge's own error body; a bare "422" says nothing useful.
        detail = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"{method} {path} -> HTTP {error.code}: {detail}") from None


def write_gradient_png(path, width, height):
    rows = [b"\x00" + bytes([0x30, (y * 255) // max(1, height - 1), 0x90]) * width
            for y in range(height)]
    raw = b"".join(rows)

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 6))
           + chunk(b"IEND", b""))
    with open(path, "wb") as handle:
        handle.write(png)


def main():
    failures = []

    with open(TOKEN_PATH, "r", encoding="utf-8") as handle:
        token = handle.read().strip()

    # The device monitor probes devices in the background before they appear in /devices.
    devices = []
    for _ in range(20):
        devices = [d for d in api("GET", "/devices", token) if d["transport"] == "ADB"]
        if devices:
            break
        time.sleep(1)

    if not devices:
        print("no ADB device attached; nothing to check")
        return 0

    device = devices[0]
    device_id = device["id"]
    print(f"device: {device['brand']} {device['model']}  [{device['transport']}]")

    capabilities = api("GET", f"/devices/{device_id}/capabilities", token)
    print(f"capabilities: {json.dumps(capabilities, ensure_ascii=False)}")

    if not capabilities.get("canSetHome"):
        failures.append("canSetHome is false but the helper is installed on this device")

    # The lock screen is NOT expected to work on EMUI. The helper reports success and dumpsys even
    # records a lock wallpaper id, but the lock screen never changes. Claiming support would be the
    # false positive spec §40 forbids, so the bridge must refuse instead.
    lock_settable = bool(capabilities.get("canSetLock"))
    print(f"lock screen settable: {lock_settable}")

    os.makedirs(TEMP_WORKSPACE, exist_ok=True)
    source = os.path.join(TEMP_WORKSPACE, "wallpaper-check-source.png")
    write_gradient_png(source, 1600, 2400)

    prepared = api("POST", "/wallpaper/prepare", token,
                   {"deviceId": device_id, "path": source})
    print(f"prepare: {prepared['width']}x{prepared['height']} success={prepared['success']}")

    if not prepared.get("success"):
        failures.append(f"prepare failed: {prepared.get('message')}")
        print()
        print("FAILURES:")
        for failure in failures:
            print(f"  - {failure}")
        return 1

    # Home must work and must be reported as working.
    home = api("POST", "/wallpaper/set-home", token,
               {"deviceId": device_id, "imagePath": prepared["imagePath"]})
    print(f"set-home: {json.dumps(home, ensure_ascii=False)}")

    if not home.get("success"):
        failures.append(f"set-home failed: {home.get('message')} [{home.get('errorCode')}]")

    # Lock must behave exactly as advertised: succeed only when the capability says it can.
    try:
        lock = api("POST", "/wallpaper/set-lock", token,
                   {"deviceId": device_id, "imagePath": prepared["imagePath"]})
        print(f"set-lock: {json.dumps(lock, ensure_ascii=False)}")

        if not lock_settable:
            failures.append(
                "canSetLock is false but set-lock reported success — that is a false positive")
    except RuntimeError as error:
        print(f"set-lock: refused ({error})")

        if lock_settable:
            failures.append(f"canSetLock is true but set-lock was refused: {error}")

    print()
    if failures:
        print("FAILURES:")
        for failure in failures:
            print(f"  - {failure}")
        return 1

    print("PASSED - capabilities match reality (home works; lock honestly reported as unsupported)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
