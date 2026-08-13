#!/usr/bin/env python3
"""GloomhavenVR — Yale Bright Star Catalogue -> compact star table.

User finding, ModBuild 132: "Der Sternenhimmel sollte auch ein animierte
'echter' Sternenhimmel sein, recharchier da was du findest statt einfach nur
ein Bild."

So the night sky stops being one photograph: on top of the photographic Milky
Way (Poly Haven 'Rogland Clear Night', CC0) the dome now carries the REAL
naked-eye sky — every star of the Yale Bright Star Catalogue down to V=6.5,
with its true right ascension, declination, visual magnitude and B-V colour.
BuildEnvironments.cs turns this table into a point-sprite mesh that the
EnvStarPoints shader rotates about the celestial pole, twinkles per star by
airmass, and rises/sets correctly — all from _Time, no scripts (bundle rule).

SOURCE (public domain)
  Hoffleit D. & Warren Jr. W.H., 1991, "Bright Star Catalogue, 5th Revised
  Edition (Preliminary)", Astronomical Data Center, NSSDC/ADC.
  http://tdc-www.harvard.edu/catalogs/bsc5.dat.gz   (Harvard/NASA mirror)
  Mirror with TLS: https://cdsarc.cds.unistra.fr/ftp/V/50/catalog.gz
  A NASA/NSSDC product: US Government work, not subject to copyright; star
  positions and magnitudes are uncopyrightable facts regardless.
  NOTE: tdc-www.harvard.edu serves a certificate for another host — use http://
  for that mirror (the payload is a fixed-format ASCII table, and the parse
  below rejects anything malformed).

RECORD FORMAT (V/50 ReadMe, 1-based inclusive byte columns, 197-byte records)
  1-4     HR number (stable per-star id -> twinkle phase)
  76-83   RA  J2000: RAh(2) RAm(2) RAs(4.1)
  84-90   Dec J2000: sign(1) DEd(2) DEm(2) DEs(2)
  103-107 Vmag
  110-114 B-V
  14 of the 9110 records are novae/removed objects with blank fields — skipped.

Run:  python3 star_catalogue.py            # -> bsc5_stars.csv next to this file
The output is committed so the Unity build never needs the network. It lives in
Assets/Editor/ (editor-only, never part of the player build or the bundle).
"""
import gzip
import io
import os
import sys
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "bsc5_stars.csv")
URLS = [
    "http://tdc-www.harvard.edu/catalogs/bsc5.dat.gz",
    "https://cdsarc.cds.unistra.fr/ftp/V/50/catalog.gz",
]
MAG_LIMIT = 6.5          # naked-eye limit; the catalogue is complete to ~6.5


def fetch():
    cache = os.path.join(HERE, "..", "..", "..", ".bsc5.dat")
    cache = os.environ.get("BSC5_CACHE", os.path.normpath(cache))
    if os.path.exists(cache):
        with open(cache, "rb") as f:
            return f.read().decode("latin-1")
    last = None
    for url in URLS:
        try:
            req = urllib.request.Request(url, headers={"User-Agent": "GloomhavenVR-env-build/1.0"})
            raw = urllib.request.urlopen(req, timeout=60).read()
            if url.endswith(".gz"):
                raw = gzip.decompress(raw)
            with open(cache, "wb") as f:
                f.write(raw)
            print("fetched %s (%d bytes)" % (url, len(raw)))
            return raw.decode("latin-1")
        except Exception as e:                                   # noqa: BLE001
            print("  %s failed: %s" % (url, e))
            last = e
    raise SystemExit("could not fetch BSC5: %s" % last)


def main():
    text = fetch()
    rows = []
    for line in io.StringIO(text):
        if len(line) < 115:
            continue
        try:
            rah, ram, ras = float(line[75:77]), float(line[77:79]), float(line[79:83])
            sign = -1.0 if line[83] == "-" else 1.0
            ded, dem, des = float(line[84:86]), float(line[86:88]), float(line[88:90])
            vmag = float(line[102:107])
        except ValueError:
            continue                                             # blank = removed object
        try:
            bv = float(line[109:114])
        except ValueError:
            bv = 0.0                                             # unknown colour -> white-ish A0
        if vmag > MAG_LIMIT:
            continue
        hr = int(line[0:4])
        ra = (rah + ram / 60.0 + ras / 3600.0) * 15.0            # hours -> degrees
        dec = sign * (ded + dem / 60.0 + des / 3600.0)
        rows.append((hr, ra, dec, vmag, bv))

    rows.sort(key=lambda r: r[0])
    with open(OUT, "w") as f:
        f.write("# Yale Bright Star Catalogue 5th rev. (Hoffleit & Warren 1991, NSSDC/ADC,\n")
        f.write("# public domain) filtered to V<=%.1f. hr,ra_deg,dec_deg,vmag,bv\n" % MAG_LIMIT)
        for hr, ra, dec, vmag, bv in rows:
            f.write("%d,%.5f,%.5f,%.2f,%.2f\n" % (hr, ra, dec, vmag, bv))
    print("wrote %s: %d stars, %.0f kB" % (OUT, len(rows), os.path.getsize(OUT) / 1024))
    hist = {}
    for _, _, _, v, _ in rows:
        hist[int(v // 1)] = hist.get(int(v // 1), 0) + 1
    print("magnitude histogram:", dict(sorted(hist.items())))


if __name__ == "__main__":
    sys.exit(main())
