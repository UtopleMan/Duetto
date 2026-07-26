#!/usr/bin/env python3
"""Generates the Duet app icon: blue rounded square with two white panes.

Writes a base 1024px PNG using only the stdlib (zlib), then macOS tooling
(sips + iconutil) turns it into Duet.icns.
"""
import math
import struct
import sys
import zlib

SIZE = 1024
BLUE = (0x2F, 0x6F, 0xD0)
WHITE = (0xFA, 0xF9, 0xF7)


def rounded_rect_alpha(x, y, x0, y0, x1, y1, r):
    cx = min(max(x, x0 + r), x1 - r)
    cy = min(max(y, y0 + r), y1 - r)
    d = math.hypot(x - cx, y - cy)
    if x0 + r <= x <= x1 - r or y0 + r <= y <= y1 - r:
        inside = x0 <= x <= x1 and y0 <= y <= y1
        return 1.0 if inside else 0.0
    return max(0.0, min(1.0, r - d + 0.5))


def make_pixels():
    margin = SIZE * 0.08
    radius = SIZE * 0.22
    pane_m = SIZE * 0.22
    gap = SIZE * 0.035
    pane_r = SIZE * 0.045
    rows = []
    for y in range(SIZE):
        row = bytearray()
        for x in range(SIZE):
            a = rounded_rect_alpha(x, y, margin, margin, SIZE - margin, SIZE - margin, radius)
            if a <= 0:
                row += bytes((0, 0, 0, 0))
                continue
            mid = SIZE / 2
            left = rounded_rect_alpha(x, y, pane_m, pane_m, mid - gap / 2, SIZE - pane_m, pane_r)
            right = rounded_rect_alpha(x, y, mid + gap / 2, pane_m, SIZE - pane_m, SIZE - pane_m, pane_r)
            w = max(left, right)
            r = int(BLUE[0] * (1 - w) + WHITE[0] * w)
            g = int(BLUE[1] * (1 - w) + WHITE[1] * w)
            b = int(BLUE[2] * (1 - w) + WHITE[2] * w)
            row += bytes((r, g, b, int(a * 255)))
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
