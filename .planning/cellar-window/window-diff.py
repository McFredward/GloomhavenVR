#!/usr/bin/env python3
"""Amplify the difference between two cellar-window preview frames.

The window view is deliberately "sehr dunkel", so the honest way to look at it
is not to stare at a black rectangle but to subtract the two frames the harness
already writes at ONE camera pose and stretch what is left.

    python3 .planning/cellar-window/window-diff.py A.png B.png OUT.png [gain]
"""
import struct
import sys
import zlib


def read_png(path):
    d = open(path, 'rb').read()
    assert d[:8] == b'\x89PNG\r\n\x1a\n', path
    i = 8
    w = h = None
    idat = b''
    while i < len(d):
        ln = struct.unpack('>I', d[i:i + 4])[0]
        typ = d[i + 4:i + 8]
        body = d[i + 8:i + 8 + ln]
        if typ == b'IHDR':
            w, h, bd, ct = struct.unpack('>IIBB', body[:10])
            assert bd == 8 and ct in (2, 6), (bd, ct)
            nch = 3 if ct == 2 else 4
        elif typ == b'IDAT':
            idat += body
        i += 12 + ln
    raw = zlib.decompress(idat)
    stride = w * nch
    out = bytearray(h * stride)
    prev = bytearray(stride)
    pos = 0
    for y in range(h):
        f = raw[pos]
        pos += 1
        line = bytearray(raw[pos:pos + stride])
        pos += stride
        if f == 1:
            for x in range(nch, stride):
                line[x] = (line[x] + line[x - nch]) & 255
        elif f == 2:
            for x in range(stride):
                line[x] = (line[x] + prev[x]) & 255
        elif f == 3:
            for x in range(stride):
                a = line[x - nch] if x >= nch else 0
                line[x] = (line[x] + ((a + prev[x]) >> 1)) & 255
        elif f == 4:
            for x in range(stride):
                a = line[x - nch] if x >= nch else 0
                b = prev[x]
                c = prev[x - nch] if x >= nch else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[x] = (line[x] + pr) & 255
        out[y * stride:(y + 1) * stride] = line
        prev = line
    return w, h, nch, out


def write_png(path, w, h, rgb):
    raw = b''.join(b'\x00' + bytes(rgb[y * w * 3:(y + 1) * w * 3]) for y in range(h))
    def chunk(t, b):
        c = t + b
        return struct.pack('>I', len(b)) + c + struct.pack('>I', zlib.crc32(c) & 0xffffffff)
    open(path, 'wb').write(
        b'\x89PNG\r\n\x1a\n'
        + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
        + chunk(b'IDAT', zlib.compress(raw, 6))
        + chunk(b'IEND', b''))


def main():
    a, b, out = sys.argv[1], sys.argv[2], sys.argv[3]
    gain = float(sys.argv[4]) if len(sys.argv) > 4 else 8.0
    wa, ha, na, da = read_png(a)
    wb, hb, nb, db = read_png(b)
    assert (wa, ha) == (wb, hb), 'frames differ in size'
    res = bytearray(wa * ha * 3)
    nz = 0
    biggest = 0
    for y in range(ha):
        for x in range(wa):
            ia, ib = (y * wa + x) * na, (y * wa + x) * nb
            for c in range(3):
                dv = abs(da[ia + c] - db[ib + c])
                if dv > biggest:
                    biggest = dv
                v = int(min(255, dv * gain))
                res[(y * wa + x) * 3 + c] = v
                if dv:
                    nz += 1
    write_png(out, wa, ha, res)
    print('%s vs %s -> %s   gain %.1fx, %d subpixels differ, worst delta %d/255'
          % (a.split('/')[-1], b.split('/')[-1], out.split('/')[-1], gain, nz, biggest))


main()
