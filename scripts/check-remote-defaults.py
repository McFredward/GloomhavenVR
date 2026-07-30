#!/usr/bin/env python3
"""
Remote-vs-local rendering lint: every FROZEN constant in Net/Remote*.cs must still equal the
config default it copies.

WHY THIS EXISTS
---------------
A remote player's board, fan and buttons are drawn by the Net/Remote* renderers, which cannot
read the SENDER's config — nothing on the wire carries it. They therefore hold frozen copies of
the shipped DEFAULTS, so that two players who have not tuned anything (the overwhelmingly common
case) see the same thing.

That works exactly until someone changes a default. On 2026-07-30 the whole shipped config was
re-based on a tuned setup, and six of these constants silently stopped matching: the remote hand
fan kept the old 0.1792 m radius and 91-degree sweep while the local one moved to 0.2192 / 103,
and three button caps kept their old sizes. Nothing failed, nothing logged; the two players simply
saw different boards.

This is not a test of behaviour — it is the one check that makes a default change tell you it has
a second home. Adding a pair here costs one line; forgetting costs a desync nobody can see from
inside their own headset.

WHAT IT DOES NOT COVER
----------------------
A player who TUNES one of these values still desyncs, because the value is not transmitted. That
is a protocol question, not a lint question: see NetProtocol's remaining-extension-slot note.
"""
import re
import sys
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "src" / "GloomhavenVR"

# remote file : constant : the config Bind whose default it copies (section, key)
PAIRS = [
    ("Net/RemoteBoardFurniture.cs", "BoardCapW", "BoardButtons", "Width"),
    ("Net/RemoteBoardFurniture.cs", "BoardCapH", "BoardButtons", "Height"),
    ("Net/RemoteBoardFurniture.cs", "PinCapW", "BoardDashboard", "PinWidth"),
    ("Net/RemoteBoardFurniture.cs", "DashCapH", "BoardDashboard", "Height"),
    ("Net/RemoteBoardFurniture.cs", "TransientCapR", "RoundButtons", "CapSize"),
    ("Net/RemoteHandFan.cs", "PalmOffset", "Cards", "FanPalmOffset"),
    ("Net/RemoteHandFan.cs", "Radius", "Cards", "FanEffectiveRadius"),
    ("Net/RemoteHandFan.cs", "ArcSweepDegrees", "Cards", "FanArcSweepDegrees"),
    ("Net/RemoteHandFan.cs", "PerCardStepDegrees", "Cards", "FanPerCardStepDegrees"),
    ("Net/RemoteHandFan.cs", "ArchFactor", "Cards", "FanFlatCurvatureFactor"),
    ("Net/RemoteHandFan.cs", "TiltFactor", "Cards", "FanTiltFactor"),
    ("Net/RemoteHandFan.cs", "OpenSeconds", "Cards", "FanOpenDuration"),
    ("Net/RemoteHandFan.cs", "OpenStagger", "Cards", "FanOpenStagger"),
]

# Where a Bind default may be written as a named constant instead of a literal.
CONST_SOURCES = ["WorldUI/ButtonTuning.cs", "Cards/CardsConfig.cs"]


def named_constants():
    out = {}
    for rel in CONST_SOURCES:
        text = (SRC / rel).read_text(encoding="utf-8")
        for name, value in re.findall(r"const float (\w+) *= *(-?[\d.]+)f", text):
            out.setdefault(name, float(value))
    return out


CONSTS = named_constants()


def bind_default(section, key):
    """The default argument of Bind("Section", "Key", <default>, ...), as a float."""
    for path in SRC.rglob("*.cs"):
        text = path.read_text(encoding="utf-8", errors="replace")
        m = re.search(r'Bind\(\s*"' + re.escape(section) + r'"\s*,\s*"' + re.escape(key)
                      + r'"\s*,\s*([^,]+),', text)
        if not m:
            continue
        raw = m.group(1).strip().rstrip("f")
        try:
            return float(raw)
        except ValueError:
            return CONSTS.get(raw)
    return None


def frozen(rel, const):
    text = (SRC / rel).read_text(encoding="utf-8")
    m = re.search(r"const float " + const + r" *= *(-?[\d.]+)f", text)
    return float(m.group(1)) if m else None


bad = []
for rel, const, section, key in PAIRS:
    have = frozen(rel, const)
    want = bind_default(section, key)
    if have is None:
        bad.append(f"{rel}: constant {const} not found")
    elif want is None:
        bad.append(f"{rel}: [{section}] {key} not found — the pair names a Bind that no longer exists")
    elif abs(have - want) > 1e-6:
        bad.append(f"{rel}: {const} = {have:g} but [{section}] {key} now defaults to {want:g} — "
                   f"a remote player would be drawn with the old value")

if bad:
    print("error: remote rendering constants drifted from the defaults they copy:", file=sys.stderr)
    for b in bad:
        print("  " + b, file=sys.stderr)
    sys.exit(1)

print(f"remote defaults: {len(PAIRS)} frozen constants match the config defaults they copy")
