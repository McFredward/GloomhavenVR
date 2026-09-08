#!/usr/bin/env python3
"""AT RUNTIME, WHOSE COPY DOES THE MIRROR READ? — the question no other gate asks.

WHY THIS EXISTS
---------------
Review R2 of 2026-09-07 is a 1:1 audit, and its headline is that **all three existing
guards were green and most of its confirmed findings are invisible to all three, by
construction**:

    check-remote-defaults.py   compares a FROZEN CONSTANT against the Defaults entry it copies
    check-wire-coverage.py     asks "is this dial ON THE WIRE"
    check-mirrors.sh           compares declared constant groups and shared expressions

R2, verbatim: *"None of them asks the question that actually decides 1:1: at runtime,
does the mirror read the OWNER's copy of this value or the VIEWER's? A live
`ConfigEntry.Value` read on the mirror side is not a frozen constant, so
`check-remote-defaults.py` has no shape in which to express it."*

And R2 falsifies `check-remote-defaults.py`'s own closing claim ("What is genuinely no
longer covered: **nothing**") twice from inside that script's own body — its list is 95
HAND-MAINTAINED pairs with no sweep behind it, so anything not on the list is not covered
at all. **That existing gate is not weakened here and not edited here.** This is a
sibling that asks the other question. The integrator has the finding.

R2's recommendation, verbatim: *"The missing guard … is a lint that flags any live
`ConfigEntry<T>.Value` read reachable from `Net/Remote/` and requires the call site to
name whose dial it means."* This is that lint.

THE DEFECTS IT WOULD HAVE CAUGHT, each with the file and the build it shipped in
--------------------------------------------------------------------------------
  * **R2 F2** — `Net/Remote/RemoteUsableFrame.cs:122` reads
    `CardsConfig.ItemCueBeatSeconds.Value`, the VIEWER's dial, to beat a peer's item cue.
    Its sibling on the same board (`RemoteControlBoard.cs:3443`) reads the OWNER's
    `tuning.ItemCueBeatSeconds` off wire id 161. The standing ruling
    (`NetProtocol.cs:647`) is *"ItemCueBeatSeconds follows the OWNER on both surfaces"*.
    One board, two clocks.
  * **R2 F3** — `Net/Remote/RemoteMapRoom.cs:1265` sizes a peer's map placard with
    `WorldUI.WorldUIConfig.CanvasScaleMm.Value`, the VIEWER's dial, under a comment
    saying *"THE FACTOR IS THE OWNER'S DIAL … not this viewer's"* — true of one of its
    three factors. The sibling mirror `RemoteBoardTooltip.cs:179` reads the owner's
    `tuning.CanvasScaleMm` off wire id 143.
  * **R2 F8** — `WorldUIConfig.SharedWindowArcRadiusMeters.Value` seats a SHARED window,
    which by `WorldUI/Modal/SharedWindowSizeLaw.cs:55-62` has no owner at all and must be
    frozen to the shipped const. Three such dials, none with a `Tune*` id.
  * **R2 F12** — `Net/Remote/RemoteBoardContent.cs:110` gates every peer board's whole
    structural pass on `Core.PerfConfig.RemoteContentSeconds`, which is
    `Mathf.Clamp(RemoteContentInterval.Value, …)` — a live viewer dial reached through a
    PROPERTY, with no `.Value` at the call site at all. A `.Value`-only lint misses it,
    which is why this one follows one level of indirection.

None of the four is a frozen constant, so none has a shape in `check-remote-defaults.py`.

WHAT IT DOES
------------
1. Sweeps all of `src/GloomhavenVR` for `static ConfigEntry<T> Name` declarations — the
   dials themselves, 300-odd of them.
2. Sweeps the same tree for a **one-level wrapper**: a static member whose body reads one
   of those entries' `.Value` (`PerfConfig.RemoteContentSeconds`,
   `NetModule.NameTagsWanted`, …). One level, not a full closure — see FALSE POSITIVES.
3. Reads every `.cs` under `src/GloomhavenVR/Net/Remote` and reports each read of an
   entry or a wrapper.
4. Requires every such read to carry a recorded verdict in
   `.planning/refactor/MIRROR-DIALS.allow`.

A read with no verdict FAILS. **A verdict with no read also FAILS** — a stale entry is
how an allowance outlives the thing it allowed, and this file is meant to shrink.

THE VERDICT VOCABULARY, and every entry must argue in its own line:

    owner        the value reaches the mirror from the OWNER (wire field / tuning record)
                 and this read is only a fallback, a clamp bound or a seam argument
    viewer-ruled the user has RULED this one viewer-local; the line must cite the ruling
    not-1to1     the read decides nothing the owner also draws (whether to render at all,
                 a local diagnostic, a cadence the user set for their own machine) — and
                 the line must say why the owner's picture cannot differ because of it
    OPEN         a CONFIRMED defect, with the review finding named. It ships red in the
                 review and green here so that the gate can exist at all; deleting the
                 line is part of the fix, and the stale-entry rule above forces it.

FALSE POSITIVES — THE POLICY
----------------------------
* Comments and string literals are blanked before matching. This repository has a named
  bug class for skipping the string pass ("a token quoted in its own explanation"), and
  `Net/Remote` is full of log lines naming the dials they obey.
* **A wrapper read must be QUALIFIED with the class that declares it.** Wrapper names
  collide with ordinary members — `Enabled`, `Mode`, `CardHeight` all exist as a config
  wrapper somewhere AND as a private field of a mirror. Requiring `PerfConfig.X` /
  `NetModule.X` removes every one of those; the cost is that a `using static` import or
  an alias would be missed, and neither is used in `Net/Remote` today.
* **One level of indirection only.** Two levels would need real name resolution, and the
  wrong answer here is worse than a miss: this gate's whole value is that its output is
  short enough to argue with line by line.
* **The scope is `Net/Remote` and nothing else.** That is where a mirror draws a peer's
  board. `Net/Board`, `Net/Avatar` and the local surfaces read their own dials
  legitimately all day, and a repo-wide version of this lint would be noise, and noise
  gets switched off.
* This gate says NOTHING about whether a dial is on the wire — that is
  `check-wire-coverage.py`'s question and it is still asked there.

    check-mirror-dials.py            verify
    check-mirror-dials.py report     print every read with its recorded verdict
    check-mirror-dials.py dials      print the entry/wrapper census the sweep found
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src" / "GloomhavenVR"
MIRROR = SRC / "Net" / "Remote"
ALLOW = ROOT / ".planning" / "refactor" / "MIRROR-DIALS.allow"

VERDICTS = ("owner", "viewer-ruled", "not-1to1", "OPEN")

# A dial: a static ConfigEntry field. The `?` covers the nullable declarations, which is
# how nearly all of them are written (an entry is null until the config binds).
DECL = re.compile(
    r"\b(?:internal|public|private|protected)\s+static\s+(?:readonly\s+)?"
    r"ConfigEntry\s*<[^>\n]*>\s*\??\s+(?P<name>[A-Z][A-Za-z0-9_]*)\s*[;=]")

# A one-level wrapper: a static member (property or expression-bodied) whose body reads a
# dial's `.Value`. Bounded to a short body on purpose — a long method that happens to
# mention a dial is not a wrapper for it, and this gate must stay arguable.
WRAP = re.compile(
    r"\b(?:internal|public|private|protected)\s+static\s+(?:readonly\s+)?"
    r"(?!ConfigEntry)[A-Za-z_][A-Za-z0-9_.<>,?\[\] ]*?\s+(?P<name>[A-Z][A-Za-z0-9_]*)\s*"
    r"(?:=>|\{\s*get\s*(?:=>|\{\s*return))(?P<body>[^;{}]{0,300})")

TYPE_DECL = re.compile(r"\b(?:class|struct)\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)")

_BLOCK = re.compile(r"/\*.*?\*/", re.S)
_LINE = re.compile(r"//[^\n]*")
_STRING = re.compile(r'"(?:[^"\\\n]|\\.)*"')


def _blank(match: re.Match) -> str:
    return re.sub(r"[^\n]", " ", match.group(0))


def strip_noncode(source: str) -> str:
    """Blank comments then strings, preserving newlines so line numbers survive."""
    return _STRING.sub(_blank, _LINE.sub(_blank, _BLOCK.sub(_blank, source)))


def enclosing_type(source: str, index: int) -> str:
    """The nearest `class`/`struct` name declared before `index`. Empty if none."""
    last = ""
    for match in TYPE_DECL.finditer(source, 0, index):
        last = match.group("name")
    return last


def census() -> tuple[dict[str, str], dict[str, tuple[str, list[str]]]]:
    """(dial name -> declaring file, wrapper `Type.Member` -> (file, dials it reads))."""
    dials: dict[str, str] = {}
    bodies: list[tuple[Path, str]] = []
    for path in sorted(SRC.rglob("*.cs")):
        code = strip_noncode(path.read_text(encoding="utf-8", errors="replace"))
        bodies.append((path, code))
        for match in DECL.finditer(code):
            dials.setdefault(match.group("name"), path.relative_to(SRC).as_posix())

    wrappers: dict[str, tuple[str, list[str]]] = {}
    for path, code in bodies:
        for match in WRAP.finditer(code):
            reads = [d for d in dials if re.search(r"\b" + d + r"\.Value\b", match.group("body"))]
            if not reads:
                continue
            owner = enclosing_type(code, match.start())
            if not owner:
                continue
            key = f"{owner}.{match.group('name')}"
            wrappers.setdefault(key, (path.relative_to(SRC).as_posix(), sorted(reads)))
    return dials, wrappers


def reads(dials: dict[str, str], wrappers: dict[str, tuple[str, list[str]]]):
    """(rel file, line, symbol, kind, source line) for every dial read under Net/Remote."""
    found = []
    wrapper_re = {
        key: re.compile(r"\b" + re.escape(key.split(".")[0]) + r"\s*\.\s*"
                        + re.escape(key.split(".")[1]) + r"\b")
        for key in wrappers
    }
    for path in sorted(MIRROR.rglob("*.cs")):
        rel = path.relative_to(SRC).as_posix()
        code = strip_noncode(path.read_text(encoding="utf-8", errors="replace"))
        for number, line in enumerate(code.splitlines(), 1):
            for match in re.finditer(r"\b([A-Z][A-Za-z0-9_]*)\s*\.\s*Value\b", line):
                name = match.group(1)
                if name in dials:
                    found.append((rel, number, name, "entry", line.strip()[:140]))
            for key, pattern in wrapper_re.items():
                # A wrapper declared inside Net/Remote is that file's own member, not a
                # dial reaching the mirror from outside. The entry it reads is reported at
                # the wrapper's own declaration instead.
                if wrappers[key][0].startswith("Net/Remote/"):
                    continue
                if pattern.search(line):
                    found.append((rel, number, key, "wrapper", line.strip()[:140]))
    return found


def load_allow() -> tuple[dict[tuple[str, str], tuple[str, str]], list[str]]:
    """{(file, symbol): (verdict, reason)}, plus complaints about malformed lines."""
    entries: dict[tuple[str, str], tuple[str, str]] = {}
    problems: list[str] = []
    if not ALLOW.is_file():
        problems.append(f"{ALLOW.relative_to(ROOT)} is missing — every dial read under "
                        f"Net/Remote needs a recorded verdict and there is no file to record "
                        f"one in.")
        return entries, problems
    for number, raw in enumerate(ALLOW.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = [p.strip() for p in line.split("|")]
        if len(parts) != 4:
            problems.append(f"{ALLOW.name}:{number}: expected "
                            f"`file | Symbol | verdict | reason`, got {len(parts)} field(s)")
            continue
        rel, symbol, verdict, reason = parts
        if verdict not in VERDICTS:
            problems.append(f"{ALLOW.name}:{number}: verdict '{verdict}' is not one of "
                            + ", ".join(VERDICTS))
            continue
        if len(reason) < 30:
            problems.append(f"{ALLOW.name}:{number}: the reason is {len(reason)} characters. "
                            f"An entry here is an argument about whose picture this value "
                            f"decides; write it.")
            continue
        entries[(rel, symbol)] = (verdict, reason)
    return entries, problems


def main() -> int:
    dials, wrappers = census()
    found = reads(dials, wrappers)
    allowed, problems = load_allow()

    if not dials:
        print("error: the sweep found ZERO ConfigEntry declarations in src/GloomhavenVR. The "
              "declaration pattern has drifted and this gate is now passing vacuously — fix "
              "DECL rather than deleting the check.", file=sys.stderr)
        return 1

    unrecorded = [r for r in found if (r[0], r[2]) not in allowed]
    seen = {(r[0], r[2]) for r in found}
    stale = [key for key in allowed if key not in seen]

    if problems:
        print("error: MIRROR-DIALS.allow is malformed:", file=sys.stderr)
        for problem in problems:
            print(f"         {problem}", file=sys.stderr)

    if unrecorded:
        print("error: a mirror reads a live config dial and nothing says WHOSE copy it is.",
              file=sys.stderr)
        for rel, number, symbol, kind, line in unrecorded:
            print(f"         {rel}:{number}  {symbol}  [{kind}]", file=sys.stderr)
            print(f"           {line}", file=sys.stderr)
        print("       A value read on the MIRROR side is the VIEWER's copy. If the owner also "
              "draws\n"
              "       something from it, the two clients draw two different pictures and no "
              "existing\n"
              "       gate can see it: check-remote-defaults.py compares frozen constants, "
              "check-wire-\n"
              "       coverage.py asks whether a dial is on the wire, check-mirrors.sh compares "
              "declared\n"
              "       groups. This one asks whose copy is read AT RUNTIME (review R2, "
              "2026-09-07).\n"
              "       Route it through the owner's tuning record, or add a line to "
              f"{ALLOW.relative_to(ROOT)}\n"
              "       saying which of " + "/".join(VERDICTS) + " it is and why.", file=sys.stderr)

    if stale:
        print("error: MIRROR-DIALS.allow records a verdict for a read that no longer exists:",
              file=sys.stderr)
        for rel, symbol in sorted(stale):
            print(f"         {rel}  {symbol}   ({allowed[(rel, symbol)][0]})", file=sys.stderr)
        print("       Delete the line. An allowance that outlives the thing it allowed is how a "
              "gate\n"
              "       stops meaning anything — and for an OPEN entry, deleting it is the last "
              "step of\n"
              "       the fix.", file=sys.stderr)

    if problems or unrecorded or stale:
        return 1

    by_verdict = {v: 0 for v in VERDICTS}
    for rel, _, symbol, _, _ in found:
        by_verdict[allowed[(rel, symbol)][0]] += 1
    tally = ", ".join(f"{by_verdict[v]} {v}" for v in VERDICTS)
    print(f"mirror dials: {len(found)} live config read(s) under Net/Remote, every one with a "
          f"recorded verdict ({tally}); swept {len(dials)} ConfigEntry declarations and "
          f"{len(wrappers)} one-level wrappers.")
    return 0


def report() -> int:
    dials, wrappers = census()
    allowed, _ = load_allow()
    for rel, number, symbol, kind, line in reads(dials, wrappers):
        verdict, reason = allowed.get((rel, symbol), ("UNRECORDED", ""))
        print(f"{rel}:{number}  {symbol} [{kind}]  -> {verdict}")
        print(f"    {line}")
        if reason:
            print(f"    {reason}")
    return 0


def show_dials() -> int:
    dials, wrappers = census()
    print(f"{len(dials)} ConfigEntry declaration(s):")
    for name in sorted(dials):
        print(f"    {name}   ({dials[name]})")
    print(f"\n{len(wrappers)} one-level wrapper(s):")
    for key in sorted(wrappers):
        path, entries = wrappers[key]
        print(f"    {key}   ({path})  <- {', '.join(entries)}")
    return 0


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else ""
    sys.exit(report() if mode == "report" else show_dials() if mode == "dials" else main())
