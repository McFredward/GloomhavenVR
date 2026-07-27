#!/usr/bin/env python3
"""Frame-order lock — make an accidental per-frame reorder loud at commit time.

WHY THIS EXISTS
---------------
`refactor-guard.sh` proves *blast radius*, not correctness. A reorder of five
statements inside `BoardDriver.Update` is an ordinary diff inside a type the
refactor legitimately touched — which is the guard's **pass** condition for a
Tier-1 motion. So per-frame ordering is the one class of regression in this mod
that can ship completely silently (CHARTER §3b.2).

The orderings are not stylistic. Each adjacency is an arbitration decision won on
hardware: RayGrab ticks after RayUgui so a UI click on a window beats dragging its
bar, and before Grabber so a laser-carry suppresses the proximity grab that frame;
FigureGrab's Ghosts and Glide run *before* the config gate so a glide already in
flight still lands when the feature is toggled off mid-air; MixedReality is last in
the rig's tail because it reads what every earlier step wrote.

THE MECHANISM: TWO LEVERS THAT MUST AGREE
-----------------------------------------
Each ordered block carries a marker comment naming its steps, and the same bracket
list is checked into `.planning/refactor/FRAME-ORDER.lock`. This script enforces
two independent things:

  1. the marker's tokens occur, in that order, in the source that follows it
     — catches "I tidied the Update method";
  2. the marker's bracket list is byte-identical to the lock entry
     — catches "I tidied the Update method *and* the comment".

Defeating the check therefore requires editing the source, the comment beside it,
and a file in another directory, all in agreement. That is no longer an accident.

Both are pure text. Neither reaches the compiler, so the guard diff is empty.

WHAT THIS DELIBERATELY DOES NOT DO
----------------------------------
It adds no `[DefaultExecutionOrder]`. Two cross-`MonoBehaviour` orderings *look*
load-bearing (`BoardDriver.SyncRayMask` → `VRHand`'s ray mask;
`FigureGrabDriver.TickLaserGrab` → `RayInteractor.HasFreshUiHit`) and are
deliberately not relied upon — the mask is sticky across frames and the freshness
window is two frames precisely so producer and consumer may sit in either phase.
Freezing an order the code does not depend on is a Tier-3 behaviour change wearing
a tidy-up costume, and would let a later edit quietly start depending on it.
See `.planning/refactor/REVIEW-Hands-Board-Core.md` §P2. Those two sites carry a
comment saying so, and no marker.

Six further orderings (§P2 sites 8–13) are single-method-local and already state
their reason at the code; a marker would add nothing a reader would miss. They are
not locked, on purpose.

USAGE
-----
    scripts/check-frame-order.py           exit 1 on any disagreement
    scripts/check-frame-order.py --list    print what is locked today
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
LOCK = ROOT / ".planning" / "refactor" / "FRAME-ORDER.lock"

MARKER_RE = re.compile(
    r'^\s*//\s*FRAME-ORDER\s+(?P<id>[^\s\[]+)'
    r'(?:\s+(?P<mode>LateUpdate-required))?'
    r'\s*(?P<brackets>\[[^\]]*\])\s*$')

LOCK_RE = re.compile(
    r'^(?P<id>\S+)\s*\|\s*(?P<file>\S+)\s*\|\s*(?P<mode>\S+)\s*\|\s*(?P<brackets>\[.*\])\s*$')

IDENT = re.compile(r'\w')


@dataclass
class Marker:
    id: str
    mode: str
    brackets: str
    file: Path
    line: int          # 1-based line of the marker itself


def strip_comments(text: str) -> str:
    """Blank comments, preserving offsets — a marker must not match itself, and the
    prose around these blocks names the very symbols we are looking for."""
    out = list(text)
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '/' and i + 1 < n and text[i + 1] == '/':
            while i < n and text[i] != '\n':
                out[i] = ' '
                i += 1
        elif c == '/' and i + 1 < n and text[i + 1] == '*':
            while i < n and not (text[i] == '*' and i + 1 < n and text[i + 1] == '/'):
                if text[i] != '\n':
                    out[i] = ' '
                i += 1
            for _ in range(2):
                if i < n:
                    out[i] = ' '
                    i += 1
        elif c == '"':
            i += 1
            while i < n and text[i] != '"':
                i += 2 if text[i] == '\\' else 1
            i += 1
        else:
            i += 1
    return ''.join(out)


def tokens_of(brackets: str) -> list[str]:
    inner = brackets.strip()[1:-1]
    return [t.strip() for t in inner.split(',') if t.strip()]


def match_token(line: str, token: str) -> bool:
    """Substring match with identifier boundaries, so `Ray` does not match `RayUgui`."""
    probe = token.split('GATE:', 1)[-1]
    start = 0
    while True:
        i = line.find(probe, start)
        if i < 0:
            return False
        before_ok = i == 0 or not IDENT.match(line[i - 1])
        j = i + len(probe)
        after_ok = j >= len(line) or not IDENT.match(line[j])
        if before_ok and after_ok:
            return True
        start = i + 1


def find_markers() -> list[Marker]:
    found = []
    for path in sorted(SRC.rglob('*.cs')):
        if 'obj' in path.parts or 'bin' in path.parts:
            continue
        for n, line in enumerate(path.read_text(encoding='utf-8').splitlines(), 1):
            m = MARKER_RE.match(line)
            if m:
                found.append(Marker(m['id'], m['mode'] or 'ordered',
                                    m['brackets'], path, n))
    return found


def read_lock() -> dict[str, tuple[str, str, str]]:
    entries = {}
    if not LOCK.exists():
        return entries
    for n, line in enumerate(LOCK.read_text(encoding='utf-8').splitlines(), 1):
        s = line.strip()
        if not s or s.startswith('#'):
            continue
        m = LOCK_RE.match(s)
        if not m:
            raise SystemExit(f"error: {LOCK.name}:{n}: malformed lock line: {line!r}")
        entries[m['id']] = (m['file'], m['mode'], m['brackets'])
    return entries


def check_ordered(mk: Marker, errors: list[str]) -> None:
    """The listed tokens must be the first ones mentioned after the marker, in order."""
    toks = tokens_of(mk.brackets)
    body = strip_comments(mk.file.read_text(encoding='utf-8')).splitlines()
    seen: list[tuple[str, int]] = []
    for n in range(mk.line, len(body)):          # mk.line is 1-based -> starts AFTER marker
        for t in toks:
            if match_token(body[n], t):
                seen.append((t, n + 1))
        if len(seen) >= len(toks):
            break
    got = [t for t, _ in seen[:len(toks)]]
    if got != toks:
        errors.append(
            f"{mk.file.relative_to(ROOT)}:{mk.line}: FRAME-ORDER {mk.id} — the source no\n"
            f"  longer runs these steps in the marked order.\n"
            f"    marker says : {toks}\n"
            f"    source does : {got or '(none of the steps found)'}\n"
            f"  Each adjacency here is an arbitration decision won on hardware, not a\n"
            f"  style choice — reordering it is Tier 3. If the change is deliberate,\n"
            f"  update the marker AND {LOCK.relative_to(ROOT)} in the same commit.")


def check_late_update(mk: Marker, errors: list[str]) -> None:
    """Every listed token must sit inside a LateUpdate body in the marker's file."""
    text = strip_comments(mk.file.read_text(encoding='utf-8'))
    bodies = []
    for m in re.finditer(r'\bLateUpdate\s*\(\s*\)\s*\{', text):
        depth, i = 0, m.end() - 1
        while i < len(text):
            if text[i] == '{':
                depth += 1
            elif text[i] == '}':
                depth -= 1
                if depth == 0:
                    bodies.append(text[m.end():i])
                    break
            i += 1
    joined = '\n'.join(bodies)
    missing = [t for t in tokens_of(mk.brackets)
               if not any(match_token(l, t) for l in joined.splitlines())]
    if missing:
        errors.append(
            f"{mk.file.relative_to(ROOT)}:{mk.line}: FRAME-ORDER {mk.id} — "
            f"{missing} no longer run inside a LateUpdate body.\n"
            f"  These must land AFTER the game's Update writes (the Animator, the\n"
            f"  generator's renderer lists, the final head pose). Merging them into\n"
            f"  Update 'for symmetry' is the change this marker exists to stop.")


def main(argv: list[str]) -> int:
    markers = find_markers()
    lock = read_lock()

    if '--list' in argv:
        for mk in markers:
            print(f"{mk.id:38} {mk.mode:19} {mk.brackets}")
        return 0

    errors: list[str] = []
    by_id: dict[str, list[Marker]] = {}
    for mk in markers:
        by_id.setdefault(mk.id, []).append(mk)

    for mid, mks in sorted(by_id.items()):
        if len(mks) > 1:
            errors.append(f"FRAME-ORDER id {mid!r} is used by {len(mks)} markers — ids must be unique.")
            continue
        mk = mks[0]
        if mid not in lock:
            errors.append(
                f"{mk.file.relative_to(ROOT)}:{mk.line}: FRAME-ORDER {mid} has no entry in\n"
                f"  {LOCK.relative_to(ROOT)}. A marker with no lock entry is one lever, not two —\n"
                f"  it can be edited away with the code it guards. Add the entry.")
            continue
        want_file, want_mode, want_brackets = lock[mid]
        rel = str(mk.file.relative_to(ROOT))
        if rel != want_file:
            errors.append(
                f"FRAME-ORDER {mid}: marker moved to {rel}, lock says {want_file}.")
        if mk.mode != want_mode:
            errors.append(
                f"FRAME-ORDER {mid}: marker mode {mk.mode!r}, lock says {want_mode!r}.")
        if mk.brackets != want_brackets:
            errors.append(
                f"{rel}:{mk.line}: FRAME-ORDER {mid} — the marker and the lock disagree.\n"
                f"    marker : {mk.brackets}\n"
                f"    lock   : {want_brackets}\n"
                f"  Two levers must be pulled in agreement to change a frame order. This is\n"
                f"  the second one. If the reorder is deliberate and Tier 3, update the lock.")
            continue
        (check_late_update if mk.mode == 'LateUpdate-required' else check_ordered)(mk, errors)

    for mid in sorted(lock):
        if mid not in by_id:
            errors.append(
                f"{LOCK.relative_to(ROOT)}: {mid} is locked but its marker comment is gone\n"
                f"  from the source. Deleting the marker is how the guard gets defeated —\n"
                f"  restore it, or remove the lock entry deliberately.")

    for e in errors:
        print(f"error: {e}", file=sys.stderr)
    if errors:
        return 1
    print(f"frame order: {len(markers)} locked orderings verified against source and lock")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
