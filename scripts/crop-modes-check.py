"""Phase 7 end-to-end check: every spec §11 crop mode against a live bridge and a real device.

Decodes the bridge's PNG output in pure Python so the assertions are about actual pixels, not just
file sizes: "center-fit" must letterbox, the crop modes must not, and top/bottom must sample
opposite ends of the source.

Usage:  python scripts/crop-modes-check.py
"""

import json
import os
import struct
import sys
import urllib.error
import urllib.request
import zlib

HOST = "127.0.0.1"
PORT = 18765
API = f"http://{HOST}:{PORT}/api/v1"
TOKEN_PATH = os.path.join(os.environ["APPDATA"], "PSMobileWallpaper", "auth.token")
TEMP_WORKSPACE = os.path.join(os.environ["TEMP"], "PSMobileWallpaper")


# ------------------------------------------------------------------ REST

def api(method: str, path: str, token: str, body=None):
    data = None if body is None else json.dumps(body).encode("utf-8")
    request = urllib.request.Request(f"{API}{path}", data=data, method=method)
    request.add_header("Accept", "application/json")
    request.add_header("X-PSMW-Token", token)
    if data is not None:
        request.add_header("Content-Type", "application/json")

    with urllib.request.urlopen(request, timeout=180) as response:
        return json.loads(response.read().decode("utf-8"))


# ------------------------------------------------------- source generation

def write_gradient_png(path: str, width: int, height: int) -> None:
    """Vertically varying gradient: top rows are dark, bottom rows are bright.

    The vertical ramp is what makes top-crop and bottom-crop distinguishable. The source is
    deliberately TALLER than the phone screen: when a source is wider than the target the crop
    keeps the full height and trimming happens on the horizontal axis only, which makes the
    top/bottom/centre anchors mathematically identical.
    """
    rows = []
    for y in range(height):
        green = (y * 255) // max(1, height - 1)
        rows.append(b"\x00" + bytes([0x20, green, 0x80]) * width)
    raw = b"".join(rows)

    def chunk(tag: bytes, payload: bytes) -> bytes:
        return (
            struct.pack(">I", len(payload))
            + tag
            + payload
            + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)
        )

    png = (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 6))
        + chunk(b"IEND", b"")
    )
    with open(path, "wb") as handle:
        handle.write(png)


# ---------------------------------------------------------- PNG decoding

CHANNELS = {0: 1, 2: 3, 4: 2, 6: 4}


def decode_png(path: str):
    """Minimal PNG reader: returns (width, height, channels, bytes_of_pixels)."""
    with open(path, "rb") as handle:
        data = handle.read()

    if data[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("not a PNG")

    pos = 8
    width = height = depth = color_type = None
    idat = bytearray()

    while pos < len(data):
        (length,) = struct.unpack(">I", data[pos : pos + 4])
        tag = data[pos + 4 : pos + 8]
        payload = data[pos + 8 : pos + 8 + length]
        pos += 12 + length

        if tag == b"IHDR":
            width, height, depth, color_type, _, _, interlace = struct.unpack(">IIBBBBB", payload)
            if depth != 8 or interlace != 0:
                raise ValueError(f"unsupported PNG: depth={depth} interlace={interlace}")
        elif tag == b"IDAT":
            idat += payload
        elif tag == b"IEND":
            break

    channels = CHANNELS[color_type]
    stride = width * channels
    raw = zlib.decompress(bytes(idat))

    out = bytearray(height * stride)
    prior = bytearray(stride)

    for y in range(height):
        offset = y * (stride + 1)
        filter_type = raw[offset]
        line = bytearray(raw[offset + 1 : offset + 1 + stride])

        for i in range(stride):
            left = line[i - channels] if i >= channels else 0
            up = prior[i]
            up_left = prior[i - channels] if i >= channels else 0

            if filter_type == 1:
                line[i] = (line[i] + left) & 0xFF
            elif filter_type == 2:
                line[i] = (line[i] + up) & 0xFF
            elif filter_type == 3:
                line[i] = (line[i] + ((left + up) >> 1)) & 0xFF
            elif filter_type == 4:
                p = left + up - up_left
                pa, pb, pc = abs(p - left), abs(p - up), abs(p - up_left)
                predictor = left if (pa <= pb and pa <= pc) else (up if pb <= pc else up_left)
                line[i] = (line[i] + predictor) & 0xFF
            elif filter_type != 0:
                raise ValueError(f"unknown PNG filter {filter_type}")

        out[y * stride : (y + 1) * stride] = line
        prior = line

    return width, height, channels, bytes(out)


def pixel(image, x: int, y: int):
    width, height, channels, pixels = image
    offset = (y * width + x) * channels
    return tuple(pixels[offset : offset + channels])


def alpha_at(image, x: int, y: int) -> int:
    _, _, channels, _ = image
    return pixel(image, x, y)[3] if channels == 4 else 255


# ------------------------------------------------------------------- main

def main() -> int:
    with open(TOKEN_PATH, "r", encoding="utf-8") as handle:
        token = handle.read().strip()

    os.makedirs(TEMP_WORKSPACE, exist_ok=True)
    source = os.path.join(TEMP_WORKSPACE, "crop_modes_source.png")

    # Tall source (0.333) against a tall screen (0.455): the crop trims top/bottom, which is the
    # only configuration in which the top/bottom/centre anchors actually differ.
    write_gradient_png(source, 1000, 3000)

    devices = api("GET", "/devices", token)
    if not devices:
        print("no device attached; nothing to check")
        return 0

    device_id = devices[0]["id"]
    display = api("GET", f"/devices/{device_id}/display", token)
    target = (display["width"], display["height"])
    print(f"device {devices[0]['brand']} {devices[0]['model']}  screen {target[0]}x{target[1]}")
    print(f"source {source}  1000x3000\n")

    failures = []
    results = {}

    for mode in ["center-crop", "center-fit", "stretch", "top-crop", "bottom-crop"]:
        prepared = api("POST", "/wallpaper/prepare", token,
                       {"deviceId": device_id, "path": source, "mode": mode})

        if not prepared.get("success"):
            failures.append(f"{mode}: prepare failed - {prepared.get('message')}")
            continue

        produced = (prepared["width"], prepared["height"])
        if produced != target:
            failures.append(f"{mode}: produced {produced}, expected {target}")

        image = decode_png(prepared["imagePath"])
        if (image[0], image[1]) != target:
            failures.append(f"{mode}: decoded {image[0]}x{image[1]}, expected {target}")

        results[mode] = image
        print(f"{mode:12s} -> {produced[0]}x{produced[1]}  "
              f"top-green={pixel(image, target[0] // 2, 0)[1]:3d}  "
              f"bottom-green={pixel(image, target[0] // 2, target[1] - 1)[1]:3d}  "
              f"corner-alpha={alpha_at(image, 0, 0):3d}")

    # --- center-fit must letterbox; the crop modes must not. ---
    fit = results.get("center-fit")
    if fit:
        corner_is_bar = alpha_at(fit, 0, 0) == 0 or pixel(fit, 0, 0) == (0, 0, 0)
        centre_is_content = alpha_at(fit, target[0] // 2, target[1] // 2) != 0

        if not corner_is_bar:
            failures.append("center-fit did not letterbox: the corner is not a bar")
        if not centre_is_content:
            failures.append("center-fit: the centre pixel should be image content, not a bar")

    for mode in ["center-crop", "stretch", "top-crop", "bottom-crop"]:
        image = results.get(mode)
        if image and alpha_at(image, 0, 0) == 0:
            failures.append(f"{mode}: produced transparent pixels; it should fill the target")

    # --- The vertical anchor must move the sampled window along the gradient. ---
    # top-crop samples the darkest rows, centre-crop the middle, bottom-crop the brightest.
    first_row_green = {}
    for mode in ["top-crop", "center-crop", "bottom-crop"]:
        image = results.get(mode)
        if image:
            first_row_green[mode] = pixel(image, target[0] // 2, 0)[1]

    if len(first_row_green) == 3:
        top_green = first_row_green["top-crop"]
        centre_green = first_row_green["center-crop"]
        bottom_green = first_row_green["bottom-crop"]

        if not (top_green < centre_green < bottom_green):
            failures.append(
                "vertical anchors are not ordered as expected: "
                f"top={top_green}, centre={centre_green}, bottom={bottom_green} "
                "(expected top < centre < bottom)")

    # --- custom mode uses exactly the requested region. ---
    region = {"x": 200, "y": 100, "width": 800, "height": 1000}
    custom = api("POST", "/wallpaper/prepare", token,
                 {"deviceId": device_id, "path": source, "mode": "custom", "region": region})
    if not custom.get("success"):
        failures.append(f"custom: prepare failed - {custom.get('message')}")
    else:
        custom_image = decode_png(custom["imagePath"])
        if (custom_image[0], custom_image[1]) != target:
            failures.append(f"custom: produced {custom_image[0]}x{custom_image[1]}")
        print(f"{'custom':12s} -> {custom['width']}x{custom['height']}  "
              f"region {region['x']},{region['y']} {region['width']}x{region['height']}")

    # --- custom without a region must be rejected, not silently defaulted. ---
    try:
        api("POST", "/wallpaper/prepare", token,
            {"deviceId": device_id, "path": source, "mode": "custom"})
        failures.append("custom without a region was accepted; it should be rejected")
    except urllib.error.HTTPError as error:
        if error.code != 422:
            failures.append(f"custom without a region returned HTTP {error.code}, expected 422")
        else:
            print("custom(no region) -> rejected with HTTP 422 as expected")

    # --- an unknown mode must fall back to center-crop, not fail. ---
    fallback = api("POST", "/wallpaper/prepare", token,
                   {"deviceId": device_id, "path": source, "mode": "definitely-not-a-mode"})
    if not fallback.get("success"):
        failures.append(f"unknown mode did not fall back to center-crop: {fallback.get('message')}")
    else:
        print(f"{'unknown':12s} -> fell back to center-crop ({fallback['width']}x{fallback['height']})")

    print()
    if failures:
        print("FAILURES:")
        for failure in failures:
            print(f"  - {failure}")
        return 1

    print("ALL CROP MODE CHECKS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())
