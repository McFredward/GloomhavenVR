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

WHAT CHANGED (Defaults refactor)
--------------------------------
Both sides now READ THE SAME `Defaults` entry — the frozen constants are `= Defaults.FanTiltFactor`
rather than a re-typed number — so the drift this file was written to catch can no longer be
expressed. The check stays anyway, and got stricter rather than weaker: it verifies that the
remote constant and the local Bind name the SAME entry, and that the entry is the one carrying
the `// => [Section] Key` annotation for the pair. Pointing at the wrong entry is as wrong as a
stale literal, and it is exactly the mistake a copy-paste makes. If anyone ever puts a literal
back on either side, this fails and says so.

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
    ("Net/RemoteBoardFurniture.cs", "BoardCapD", "BoardButtons", "Depth"),
    ("Net/RemoteBoardFurniture.cs", "PinCapW", "BoardDashboard", "PinWidth"),
    ("Net/RemoteBoardFurniture.cs", "DashCapH", "BoardDashboard", "Height"),
    ("Net/RemoteBoardFurniture.cs", "DashCapD", "BoardDashboard", "Depth"),
    ("Net/RemoteBoardFurniture.cs", "RestCapD", "RestButtons", "Depth"),
    ("Net/RemoteBoardFurniture.cs", "TransientCapR", "RoundButtons", "CapSize"),
    ("Net/RemoteBoardFurniture.cs", "TransientCapD", "RoundButtons", "Depth"),
    ("Net/RemoteHandFan.cs", "PalmOffset", "Cards", "FanPalmOffset"),
    ("Net/RemoteHandFan.cs", "Radius", "Cards", "FanEffectiveRadius"),
    ("Net/RemoteHandFan.cs", "ArcSweepDegrees", "Cards", "FanArcSweepDegrees"),
    ("Net/RemoteHandFan.cs", "PerCardStepDegrees", "Cards", "FanPerCardStepDegrees"),
    ("Net/RemoteHandFan.cs", "ArchFactor", "Cards", "FanFlatCurvatureFactor"),
    ("Net/RemoteHandFan.cs", "TiltFactor", "Cards", "FanTiltFactor"),
    ("Net/RemoteHandFan.cs", "OpenSeconds", "Cards", "FanOpenDuration"),
    ("Net/RemoteHandFan.cs", "OpenStagger", "Cards", "FanOpenStagger"),
]

DEFAULTS_DIR = SRC / "Defaults"

# One annotated Defaults line: `internal const float FanTiltFactor = 0.85f;  // => [Cards] FanTiltFactor`
ENTRY_RE = re.compile(
    r"internal\s+const\s+float\s+(?P<name>\w+)\s*=\s*(?P<init>-?[\d.]+)f\s*;\s*"
    r"//\s*=>\s*\[(?P<section>[^\]]+)\]\s+(?P<key>\S+)")


def defaults_table():
    """(section, key) -> (entry name, value) for every float default in src/.../Defaults/."""
    out = {}
    for path in sorted(DEFAULTS_DIR.glob("Defaults.*.cs")):
        for m in ENTRY_RE.finditer(path.read_text(encoding="utf-8")):
            out[(m.group("section"), m.group("key"))] = (m.group("name"), float(m.group("init")))
    return out


DEFAULTS = defaults_table()


def bind_reference(section, key):
    """The Defaults entry a Bind("Section", "Key", ...) names as its default, or None."""
    for path in SRC.rglob("*.cs"):
        if path.parent.name == "Defaults":
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        m = re.search(r'Bind\(\s*"' + re.escape(section) + r'"\s*,\s*"' + re.escape(key)
                      + r'"\s*,\s*([^,]+),', text)
        if not m:
            continue
        raw = m.group(1).strip()
        return raw[len("Defaults."):] if raw.startswith("Defaults.") else raw
    return None


def frozen(rel, const):
    """What the Remote renderer's frozen constant resolves to: a Defaults name, or a literal."""
    text = (SRC / rel).read_text(encoding="utf-8")
    m = re.search(r"const float " + const + r" *= *([^;]+);", text)
    if not m:
        return None
    raw = m.group(1).strip()
    if raw.startswith("Defaults."):
        return ("ref", raw[len("Defaults."):])
    try:
        return ("literal", float(raw.rstrip("f")))
    except ValueError:
        return ("expr", raw)


bad = []
for rel, const, section, key in PAIRS:
    want = DEFAULTS.get((section, key))
    have = frozen(rel, const)
    if want is None:
        bad.append(f"{rel}: no Defaults entry annotated `// => [{section}] {key}` — the pair "
                   f"names a config identity that no longer has a shipped default")
        continue
    name, value = want
    if have is None:
        bad.append(f"{rel}: constant {const} not found")
        continue
    kind, payload = have
    if kind == "ref":
        # The strong form: the constant IS the default, so it cannot drift. Verify it is the
        # RIGHT one — a reference to the wrong entry is exactly as wrong as a stale literal.
        if payload != name:
            bad.append(f"{rel}: {const} references Defaults.{payload}, but [{section}] {key} "
                       f"is Defaults.{name}")
    elif kind == "literal":
        if abs(payload - value) > 1e-6:
            bad.append(f"{rel}: {const} = {payload:g} but [{section}] {key} now defaults to "
                       f"{value:g} — a remote player would be drawn with the old value")
        else:
            bad.append(f"{rel}: {const} still copies [{section}] {key} as a LITERAL — point it at "
                       f"Defaults.{name} so it cannot drift")
    else:
        bad.append(f"{rel}: {const} = {payload} — cannot tell whether it still matches "
                   f"[{section}] {key}")

    # Belt and braces: the BIND must draw on the same entry. If a bind ever went back to a
    # literal, the Defaults line would stop being the thing the local player sees, and the
    # reference above would be checking against a value nobody uses.
    ref = bind_reference(section, key)
    if ref is None:
        bad.append(f"[{section}] {key}: no Bind found — the pair names an entry that no "
                   f"longer exists")
    elif ref != name:
        bad.append(f"[{section}] {key}: the bind default is `{ref}`, not `Defaults.{name}` — "
                   f"the local player no longer reads the entry the remote copy mirrors")

if bad:
    print("error: remote rendering constants drifted from the defaults they copy:", file=sys.stderr)
    for b in bad:
        print("  " + b, file=sys.stderr)
    sys.exit(1)

print(f"remote defaults: {len(PAIRS)} frozen constants resolve to the same Defaults entries as "
      f"the binds they mirror")
