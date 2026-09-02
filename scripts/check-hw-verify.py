#!/usr/bin/env python3
"""A line a hardware round is waiting on must survive the DEFAULT log level.

WHY THIS EXISTS
---------------
ModBuild 331 made the mod's log quiet, which the user had asked for in those words. It
did that by re-deciding what each severity MEANS: `VRLog.Info` and `VRLog.Warn` moved to
the DEBUG tier, and the shipped default level became INFO. Nothing was deleted and no
wording changed.

The first hardware test after that change — ModBuild 334, 2026-09-02, after eighteen
untested builds — produced a `Player.log` containing **fifteen** mod lines. Counted by
subsystem tag: Tutorial 0, Hands 0, FigureGrab 0, Cards 0, Board 0, Net 0, WorldUI 0.
Every question in the hardware-test backlog was answered by a line written with
`VRLog.Info`, so not one of them could be answered. Among the casualties was the desync
recorder shipped two days earlier, whose "watch armed" line is the only evidence that it
armed at all.

That is not a logging bug. It is an instrument that was silenced by an unrelated, correct
change, with nothing anywhere to notice. This script is the thing that notices.

THE RULE
--------
A call site marked `// HW-VERIFY` must log at a tier the default level prints. With the
331 mapping that is:

    VRLog.Error  -> Error tier    printed at default
    VRLog.Alert  -> Warning tier  printed at default
    VRLog.Note   -> Info tier     printed at default   <- the usual choice
    VRLog.Info   -> Debug tier    NOT printed at default
    VRLog.Warn   -> Debug tier    NOT printed at default
    VRLog.Debug  -> Debug tier    NOT printed at default

Mark a site when a hardware round's ANSWER depends on reading it. Do not mark per-frame
chatter: a marked line that fires every frame re-creates the flood 331 removed, so the
check also refuses a marked site inside an obvious per-frame method.

    check-hw-verify.py            verify every marked site
    check-hw-verify.py list       print the marked sites and their tiers
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"

MARKER = "HW-VERIFY"

# The marker must OPEN a comment line — `// HW-VERIFY: …` — and not merely appear in one.
# Prose that discusses the convention quotes its own name (the ModBuild 335 notes in
# NetProtocol.cs say "a `// HW-VERIFY` site must log at…"), and a substring match reads that as
# a marked site with no call after it. This is the same trap check-surface.py documents for
# config keys and log markers: the repository's comments quote the very strings being censused,
# so the pattern has to be anchored rather than searched.
MARK_RE = re.compile(r"^\s*//+\s*" + MARKER)

# VRLog method -> is it printed at the shipped default level?
TIERS = {
    "Error": True,
    "Alert": True,
    "Note": True,
    "Info": False,
    "Warn": False,
    "Debug": False,
}

# A marked line inside one of these methods would fire every frame — exactly the flood
# ModBuild 331 removed. The marker is for once-per-session verdicts.
PER_FRAME = re.compile(r"\b(?:void|private|internal|public|protected)[^\n(]*\b"
                       r"(Update|LateUpdate|FixedUpdate|OnGUI|OnRenderObject)\s*\(")

CALL = re.compile(r"VRLog\.(Error|Alert|Note|Info|Warn|Debug)\s*\(")


def sites() -> list[tuple[Path, int, str, str]]:
    """(file, 1-based line, VRLog method, the source line) for every marked call."""
    found: list[tuple[Path, int, str, str]] = []
    for path in sorted(SRC.rglob("*.cs")):
        text = path.read_text(encoding="utf-8")
        if MARKER not in text:
            continue
        lines = text.split("\n")
        for i, line in enumerate(lines):
            if not MARK_RE.match(line):
                continue
            # THE RULE IS "THE FIRST LINE THAT IS NOT A COMMENT", not "within N lines". The
            # window used to be four lines, which silently mis-reported a marker whose
            # explanation ran longer than its own two-line preamble — the checker then claimed
            # the site logged at no tier at all. A magic number here is the same class of
            # defect this script exists to catch, one directory over: an instrument answering a
            # question adjacent to the one asked. Blank lines and comments are skipped; the
            # first line with real code must carry the call.
            for j in range(i + 1, len(lines)):
                stripped = lines[j].strip()
                if not stripped or stripped.startswith("//"):
                    continue
                m = CALL.search(lines[j])
                if m:
                    found.append((path, j + 1, m.group(1), stripped))
                else:
                    found.append((path, i + 1, "<none>", lines[i].strip()))
                break
            else:
                found.append((path, i + 1, "<none>", lines[i].strip()))
    return found


def enclosing_method(path: Path, line_no: int) -> str | None:
    """The nearest method signature above `line_no`, if it is an obvious per-frame one."""
    lines = path.read_text(encoding="utf-8").split("\n")
    for i in range(min(line_no, len(lines)) - 1, -1, -1):
        m = PER_FRAME.search(lines[i])
        if m:
            return m.group(1)
        if re.match(r"\s*(?:internal|public|private|protected)[^\n=]*\)\s*$", lines[i]):
            return None  # some other method opened first
    return None


def main() -> int:
    found = sites()
    if not found:
        print(f"error: no {MARKER} sites found at all — the convention has been lost, and "
              f"with it the guarantee that a hardware round can read its own answer.",
              file=sys.stderr)
        return 1

    bad_tier = [(p, n, k, s) for p, n, k, s in found if not TIERS.get(k, False)]
    per_frame = [(p, n, k, enclosing_method(p, n)) for p, n, k, _ in found]
    per_frame = [(p, n, k, m) for p, n, k, m in per_frame if m]

    if bad_tier:
        print(f"error: these {MARKER} sites log at a tier the DEFAULT level does NOT print,",
              file=sys.stderr)
        print("       so the hardware round they exist for will come back with nothing:",
              file=sys.stderr)
        for p, n, kind, line in bad_tier:
            rel = p.relative_to(ROOT)
            print(f"         {rel}:{n}  VRLog.{kind}  ->  use VRLog.Note", file=sys.stderr)
            print(f"           {line[:110]}", file=sys.stderr)
        return 1

    if per_frame:
        print(f"error: these {MARKER} sites sit inside a per-frame method, which would rebuild "
              "the log flood ModBuild 331 removed:", file=sys.stderr)
        for p, n, kind, meth in per_frame:
            print(f"         {p.relative_to(ROOT)}:{n}  inside {meth}()", file=sys.stderr)
        print("       Gate it to fire once, or drop the marker.", file=sys.stderr)
        return 1

    by_file: dict[str, int] = {}
    for p, _, _, _ in found:
        by_file[str(p.relative_to(ROOT))] = by_file.get(str(p.relative_to(ROOT)), 0) + 1
    print(f"hw-verify: {len(found)} marked line(s) across {len(by_file)} file(s), all at a tier "
          "the default log level prints.")
    return 0


def listing() -> int:
    for p, n, kind, line in sites():
        print(f"{p.relative_to(ROOT)}:{n}  VRLog.{kind}")
        print(f"    {line[:120]}")
    return 0


if __name__ == "__main__":
    sys.exit(listing() if len(sys.argv) > 1 and sys.argv[1] == "list" else main())
