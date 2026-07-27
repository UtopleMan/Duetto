#!/usr/bin/env python3
"""Generates the Duetto app icon: two phase-shifted waves — amber over blue with
an ivory lens where they overlap — on a dark rounded tile.

This is the "9a / final mark" from the Duetto design spec ("Duetto File
Manager.dc.html", claude.ai/design project 9547189c-a040-4169-8fed-38dc0d79972e):
two voices near unison, the second lagging in both time and pitch.

Writes a 1024px PNG using only the stdlib (zlib), then macOS tooling
(sips + iconutil) turns it into Duetto.icns.
"""
import math
import struct
import sys
import zlib

SIZE = 1024
RADIUS = 229                       # tile corner radius (~0.2237 * SIZE, Apple squircle)

AMBER = (0xE8, 0xC6, 0x5E)
BLUE = (0x5B, 0x9C, 0xF0)
IVORY = (0xF7, 0xF5, 0xEF)
BG_IN = (0x32, 0x30, 0x2A)         # radial-gradient inner
BG_OUT = (0x21, 0x1F, 0x1A)        # radial-gradient outer
BG_CX, BG_CY, BG_R = 0.30 * SIZE, 0.20 * SIZE, 1.20 * SIZE

SS = 3                             # wave-edge supersampling factor
SS_SIZE = SIZE * SS

# The wave band, straight from the design SVG (viewBox 0 0 1024 1024). A single
# closed path swept by cubic beziers; drawn once as amber, once shifted as blue.
_WAVE = [
    ("M", (-180, 512)),
    ("C", (30, 292), (250, 292), (460, 512)),
    ("C", (670, 732), (890, 732), (1100, 512)),
    ("C", (1180, 430), (1260, 430), (1340, 512)),
    ("L", (1340, 712)),
    ("C", (1260, 630), (1180, 630), (1100, 712)),
    ("C", (890, 932), (670, 932), (460, 712)),
    ("C", (250, 492), (30, 492), (-180, 712)),
    ("Z",),
]
GROUP_DY = -175                    # <g transform="translate(0,-175)">
AMBER_SHIFT = (0, GROUP_DY)
BLUE_SHIFT = (120, 150 + GROUP_DY)


def _flatten(shift, steps=48):
    """Flatten the wave path into a polygon, applied with the given (dx, dy)."""
    dx, dy = shift
    pts = []
    cur = None
    for cmd in _WAVE:
        op = cmd[0]
        if op == "M":
            cur = cmd[1]
            pts.append((cur[0] + dx, cur[1] + dy))
        elif op == "L":
            cur = cmd[1]
            pts.append((cur[0] + dx, cur[1] + dy))
        elif op == "C":
            p0 = cur
            c1, c2, p3 = cmd[1], cmd[2], cmd[3]
            for i in range(1, steps + 1):
                t = i / steps
                mt = 1 - t
                x = (mt ** 3 * p0[0] + 3 * mt * mt * t * c1[0]
                     + 3 * mt * t * t * c2[0] + t ** 3 * p3[0])
                y = (mt ** 3 * p0[1] + 3 * mt * mt * t * c1[1]
                     + 3 * mt * t * t * c2[1] + t ** 3 * p3[1])
                pts.append((x + dx, y + dy))
            cur = p3
        elif op == "Z":
            pass
    return pts


def _fill_mask(pts):
    """Scanline-fill a polygon into a supersampled 0/1 mask (row-major bytes)."""
    edges = [(pts[i], pts[(i + 1) % len(pts)]) for i in range(len(pts))]
    mask = [bytearray(SS_SIZE) for _ in range(SS_SIZE)]
    for sy in range(SS_SIZE):
        yy = (sy + 0.5) / SS
        xs = []
        for (x0, y0), (x1, y1) in edges:
            if (y0 <= yy < y1) or (y1 <= yy < y0):
                xs.append(x0 + (yy - y0) / (y1 - y0) * (x1 - x0))
        xs.sort()
        row = mask[sy]
        for i in range(0, len(xs) - 1, 2):
            a = max(0, int(xs[i] * SS))
            b = min(SS_SIZE, int(xs[i + 1] * SS))
            for sx in range(a, b):
                row[sx] = 1
    return mask


def _tile_alpha(x, y):
    """1px-antialiased coverage of the rounded tile at pixel center (x, y)."""
    qx = abs(x - SIZE / 2) - (SIZE / 2 - RADIUS)
    qy = abs(y - SIZE / 2) - (SIZE / 2 - RADIUS)
    d = math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) - RADIUS
    return max(0.0, min(1.0, 0.5 - d))


def _bg(x, y):
    """Radial background gradient colour at pixel (x, y)."""
    t = min(1.0, math.hypot(x - BG_CX, y - BG_CY) / BG_R)
    return (BG_IN[0] + (BG_OUT[0] - BG_IN[0]) * t,
            BG_IN[1] + (BG_OUT[1] - BG_IN[1]) * t,
            BG_IN[2] + (BG_OUT[2] - BG_IN[2]) * t)


def make_pixels():
    amber = _fill_mask(_flatten(AMBER_SHIFT))
    blue = _fill_mask(_flatten(BLUE_SHIFT))
    inv = 1.0 / (SS * SS)
    rows = []
    for y in range(SIZE):
        row = bytearray()
        sy0 = y * SS
        for x in range(SIZE):
            ta = _tile_alpha(x + 0.5, y + 0.5)
            if ta <= 0:
                row += bytes((0, 0, 0, 0))
                continue
            br, bg_, bb = _bg(x + 0.5, y + 0.5)
            r = g = b = 0.0
            sx0 = x * SS
            for sy in range(sy0, sy0 + SS):
                arow = amber[sy]
                blrow = blue[sy]
                for sx in range(sx0, sx0 + SS):
                    a_in = arow[sx]
                    b_in = blrow[sx]
                    if a_in and b_in:
                        c = IVORY
                    elif b_in:
                        c = BLUE
                    elif a_in:
                        c = AMBER
                    else:
                        c = (br, bg_, bb)
                    r += c[0]
                    g += c[1]
                    b += c[2]
            row += bytes((int(r * inv + 0.5), int(g * inv + 0.5),
                          int(b * inv + 0.5), int(ta * 255 + 0.5)))
        rows.append(bytes(row))
    return rows


def write_png(path, rows):
    def chunk(tag, data):
        c = tag + data
        return struct.pack(">I", len(data)) + c + struct.pack(">I", zlib.crc32(c))

    raw = b"".join(b"\x00" + r for r in rows)
    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9))
           + chunk(b"IEND", b""))
    with open(path, "wb") as f:
        f.write(png)


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "icon-1024.png"
    write_png(out, make_pixels())
    print(f"wrote {out}")
